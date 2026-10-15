param([string]$OutputDirectory = 'dist\non-c', [string]$FfmpegPath = '')
$ErrorActionPreference = 'Stop'
$localTemp = Join-Path $PSScriptRoot '.build-temp'
New-Item -ItemType Directory -Path $localTemp -Force | Out-Null
$env:TEMP = $localTemp
$env:TMP = $localTemp
if (!$FfmpegPath) { $FfmpegPath = (Get-Command ffmpeg.exe -ErrorAction Stop).Source }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$output = Join-Path $PSScriptRoot $OutputDirectory
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& "$framework\csc.exe" /nologo /target:exe /codepage:65001 "/out:$output\PreprocessTests.exe" "/reference:$output\MemeManager.exe" "/reference:$output\MemePreprocessor.exe" @references "$PSScriptRoot\tests\PreprocessTests.cs"
if ($LASTEXITCODE -ne 0) { throw '预处理测试编译失败。' }
$testRoot = Join-Path $PSScriptRoot ('.test-data\preprocess-' + [Guid]::NewGuid().ToString('N'))
& "$output\PreprocessTests.exe" $FfmpegPath $testRoot
if ($LASTEXITCODE -ne 0) { throw '预处理测试失败。' }
Remove-Item -LiteralPath "$output\PreprocessTests.exe"
Write-Host "预处理测试记录：$testRoot"
