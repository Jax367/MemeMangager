param([string]$OutputDirectory = 'dist\non-c')
$ErrorActionPreference = 'Stop'
$buildTemp = Join-Path $PSScriptRoot '.build-temp'
New-Item -ItemType Directory -Path $buildTemp -Force | Out-Null
$env:TEMP = $buildTemp
$env:TMP = $buildTemp
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path $compiler)) { throw '需要 Windows .NET Framework 4.8。' }
$output = Join-Path $PSScriptRoot $OutputDirectory
New-Item -ItemType Directory -Path $output -Force | Out-Null
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Web.Extensions.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /win32manifest:"$PSScriptRoot\app.manifest" /win32icon:"$PSScriptRoot\assets\app-icon.ico" "/out:$output\MemeManager.exe" "/resource:$PSScriptRoot\MainWindow.xaml,MemeManager.MainWindow.xaml" "/resource:$PSScriptRoot\assets\app-icon-64.png,MemeManager.Icon.png" @references "$PSScriptRoot\App.cs" "$PSScriptRoot\Picker.cs" "$PSScriptRoot\ClipboardImages.cs" "$PSScriptRoot\Duplicates.cs" "$PSScriptRoot\DuplicateWindow.cs" "$PSScriptRoot\TrayMenu.cs" "$PSScriptRoot\Library.cs" "$PSScriptRoot\Grouping.cs" "$PSScriptRoot\WindowActions.cs" "$PSScriptRoot\ImportBridge.cs" "$PSScriptRoot\StoragePaths.cs" "$PSScriptRoot\Native.cs"
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item "$PSScriptRoot\MemeManager.exe.config" $output
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /win32manifest:"$PSScriptRoot\app.manifest" /win32icon:"$PSScriptRoot\assets\app-icon.ico" "/out:$output\MemePreprocessor.exe" "/reference:$output\MemeManager.exe" /reference:System.Xml.dll "/resource:$PSScriptRoot\PreprocessWindow.xaml,MemeManager.Preprocess.xaml" @references "$PSScriptRoot\PreprocessEngine.cs" "$PSScriptRoot\PreprocessApp.cs"
if ($LASTEXITCODE -ne 0) { throw '预处理工具编译失败。' }
Copy-Item "$PSScriptRoot\MemeManager.exe.config" (Join-Path $output 'MemePreprocessor.exe.config')
[IO.File]::WriteAllText((Join-Path $output 'storage-path.txt'),(Join-Path $PSScriptRoot 'data'))
Write-Host "已生成 $output\MemeManager.exe"
