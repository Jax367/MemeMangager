param([switch]$Finalize)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$expectedLegacyRoot = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\Local\MemeManager')).TrimEnd('\')
$legacyRoot = $expectedLegacyRoot
$destination = [IO.Path]::GetFullPath((Join-Path $workspace 'data')).TrimEnd('\')
$backupParent = [IO.Path]::GetFullPath((Join-Path $workspace 'data-migration-backup')).TrimEnd('\')
if ($legacyRoot -ne $expectedLegacyRoot -or $legacyRoot -eq $workspace -or [IO.Path]::GetFileName($legacyRoot) -ne 'MemeManager') { throw '旧资料库路径校验失败。' }
foreach ($target in @($destination,$backupParent)) {
 if (!$target.StartsWith($workspace+'\',[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetPathRoot($target) -eq 'C:\') { throw "目标必须位于项目内的非 C 盘路径：$target" }
}
$mutex = New-Object Threading.Mutex($false,'Local\MemeManager.8241D6BA')
$owned = $false
try {
 try { $owned = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }
 if (!$owned) { throw '请先从托盘退出旧版 Manager，再迁移资料库。' }
 if (!(Test-Path -LiteralPath $legacyRoot -PathType Container)) { throw '旧资料库不存在。' }
 $items = @(Get-ChildItem -LiteralPath $legacyRoot -Recurse -Force)
 if ($items | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }) { throw '资料库含链接或联接点，请先检查。' }
 $files = @($items | Where-Object { !$_.PSIsContainer })
 New-Item -ItemType Directory -Path $backupParent -Force | Out-Null
 $manifestFile = Join-Path $backupParent 'migration-manifest.json'
 if (!$Finalize) {
  if (Test-Path -LiteralPath $destination) { throw '目标 data 目录已存在，为避免覆盖已停止。' }
  Copy-Item -LiteralPath $legacyRoot -Destination $destination -Recurse
  $manifest = @($files | ForEach-Object { [pscustomobject]@{ Relative=$_.FullName.Substring($legacyRoot.Length+1); Length=$_.Length; SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } })
  $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestFile -Encoding utf8
 } else {
  $manifest = @(Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json)
  if ($files.Count -ne $manifest.Count) { throw '源资料库在复制后发生变化，请先重新核对。' }
 }
 foreach ($entry in $manifest) {
  if ($entry.Relative -match '(^|[\/])\.\.([\/]|$)' -or [IO.Path]::IsPathRooted($entry.Relative)) { throw '清单包含无效相对路径。' }
  foreach ($base in @($legacyRoot,$destination)) {
   $file = Join-Path $base $entry.Relative
   if (!(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -ne $entry.Length -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "文件校验失败：$file" }
  }
 }
 $db = Get-Content -LiteralPath (Join-Path $destination 'library.json') -Raw | ConvertFrom-Json
 foreach ($meme in $db.Memes) { if (!(Test-Path -LiteralPath (Join-Path $destination ('images\'+$meme.Id+$meme.Extension)) -PathType Leaf)) { throw '迁移后的表情文件不完整。' } }
 Write-Host ("已校验 {0} 个文件，{1} 张表情。" -f $manifest.Count,$db.Memes.Count)
 if ($Finalize) {
  $backup = [IO.Path]::GetFullPath((Join-Path $backupParent ('legacy-profile-'+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))))
  if (!$backup.StartsWith($backupParent+'\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $backup)) { throw '备份目标校验失败。' }
  Move-Item -LiteralPath $legacyRoot -Destination $backup
  foreach ($entry in $manifest) { if ((Get-FileHash -LiteralPath (Join-Path $backup $entry.Relative) -Algorithm SHA256).Hash -ne $entry.SHA256) { throw '备份移动后校验失败，请保留所有文件。' } }
  if (Test-Path -LiteralPath $legacyRoot) { throw 'C 盘旧目录仍存在，请检查迁移状态。' }
  Write-Host "C 盘旧资料库已迁至 $backup"
 }
} finally { if ($owned) { $mutex.ReleaseMutex() };$mutex.Dispose() }
