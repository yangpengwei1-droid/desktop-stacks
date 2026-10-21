param([string]$OutputDirectory=(Join-Path $PSScriptRoot 'dist\DesktopStacks'))
$ErrorActionPreference='Stop'
$output=[IO.Path]::GetFullPath($OutputDirectory);[void][IO.Directory]::CreateDirectory($output)
$net=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
if(-not(Test-Path -LiteralPath (Join-Path $net 'csc.exe'))){throw '.NET Framework 4.x compiler is required on Windows.'}
$refs=@('System.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll')|ForEach-Object {Join-Path $net $_}
$refs+=@('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll')|ForEach-Object {Join-Path (Join-Path $net 'WPF') $_}
$common=@('/nologo','/optimize+')+@($refs|ForEach-Object {'/reference:'+$_})
$sources=Join-Path $PSScriptRoot 'src'
& (Join-Path $net 'csc.exe') @common /target:exe ('/out:'+(Join-Path $output '图标提取.exe')) (Join-Path $sources 'ShellImage.cs')
if($LASTEXITCODE -ne 0){throw 'Icon helper compilation failed.'}
& (Join-Path $net 'csc.exe') @common /target:winexe /platform:anycpu ('/out:'+(Join-Path $output 'DesktopStacks.exe')) ('/win32icon:'+(Join-Path $PSScriptRoot 'assets\desktop-stacks.ico')) ('/win32manifest:'+(Join-Path $sources 'app.manifest')) (Join-Path $sources 'FolderStack.cs') (Join-Path $sources 'DesktopAutoCollect.cs') (Join-Path $sources 'ShellLink.cs') (Join-Path $sources 'DesktopPresentation.cs')
if($LASTEXITCODE -ne 0){throw 'Desktop Stacks compilation failed.'}
foreach($name in @('Initialize.ps1','DesktopLayout.cs','桌面显示.ps1','恢复旧隐藏属性.ps1')){Copy-Item -LiteralPath (Join-Path (Join-Path $PSScriptRoot 'scripts') $name) -Destination (Join-Path $output $name) -Force}
[void][IO.Directory]::CreateDirectory((Join-Path $output '入口图标'))
foreach($ext in @('ico','png','svg')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('assets\desktop-stacks.'+$ext)) -Destination (Join-Path $output ('入口图标\办公收纳.'+$ext)) -Force}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\使用说明.txt') -Destination (Join-Path $output '使用说明.txt') -Force
Write-Output ('BUILT='+$output)
