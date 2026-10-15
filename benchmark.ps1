param([string]$BaselineDirectory = '.test-data\baseline')
$ErrorActionPreference = 'Stop'
$localTemp = Join-Path $PSScriptRoot '.build-temp'
New-Item -ItemType Directory -Path $localTemp -Force | Out-Null
$env:TEMP = $localTemp
$env:TMP = $localTemp
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$current = Join-Path $PSScriptRoot 'dist\compact'
$baseline = Join-Path $PSScriptRoot $BaselineDirectory
foreach ($directory in @($baseline,$current)) {
 & "$framework\csc.exe" /nologo /target:exe /codepage:65001 "/out:$directory\MemoryBenchmark.exe" "/reference:$directory\MemeManager.exe" @references "$PSScriptRoot\tests\MemoryBenchmark.cs"
 if ($LASTEXITCODE -ne 0) { throw '基准程序编译失败。' }
}
$fixtures = Join-Path $PSScriptRoot '.test-data\memory-fixtures'
if (!(Test-Path (Join-Path $fixtures 'library.json'))) {
 & "$current\MemoryBenchmark.exe" prepare $fixtures
 if ($LASTEXITCODE -ne 0) { throw '准备样本失败。' }
}
foreach ($entry in @(@{Name='baseline';Directory=$baseline},@{Name='compact';Directory=$current})) {
 Write-Host $entry.Name
 $report = & (Join-Path $entry.Directory 'MemoryBenchmark.exe') run $fixtures
 if ($LASTEXITCODE -ne 0) { throw '基准运行失败。' }
 $report | Tee-Object -FilePath (Join-Path $PSScriptRoot ('.test-data\memory-' + $entry.Name + '.csv'))
}
Remove-Item -LiteralPath (Join-Path $current 'MemoryBenchmark.exe')
