param([string]$OutputDirectory = 'dist\non-c')
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot\build.ps1" -OutputDirectory $OutputDirectory
$buildOutput = Join-Path $PSScriptRoot $OutputDirectory
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& "$framework\csc.exe" /nologo /target:exe /codepage:65001 "/out:$buildOutput\SmokeTests.exe" "/reference:$buildOutput\MemeManager.exe" @references "$PSScriptRoot\tests\SmokeTests.cs" "$PSScriptRoot\tests\DuplicateTests.cs" "$PSScriptRoot\tests\GroupingTests.cs"
if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
$testRoot = Join-Path $PSScriptRoot ('.test-data\' + [Guid]::NewGuid().ToString('N'))
& "$buildOutput\SmokeTests.exe" $testRoot
if ($LASTEXITCODE -ne 0) { throw '测试失败。' }
Remove-Item -LiteralPath "$buildOutput\SmokeTests.exe"
Write-Host "测试渲染保存在 $testRoot"
