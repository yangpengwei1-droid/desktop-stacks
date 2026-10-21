param(
 [ValidateSet('Hide','Restore','Check')][string]$Action='Check',
 [ValidateSet('All','Public')][string]$Scope='All',
 [switch]$AllowElevation,
 [switch]$DryRun
)
$ErrorActionPreference='Stop'
$app=$PSScriptRoot
$statePath=Join-Path $app '桌面显示原状态.json'
$resultPath=Join-Path $app '桌面显示状态.json'
$desktop=[Environment]::GetFolderPath('Desktop')
$public=[Environment]::GetFolderPath('CommonDesktopDirectory')
$namespaceKeys=@('HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel','HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\ClassicStartMenu')
$namespaceIds=@('{20D04FE0-3AEA-1069-A2D8-08002B30309D}','{645FF040-5081-101B-9F08-00AA002F954E}')
function Read-RegistryValue([string]$Key,[string]$Name){
 $exists=Test-Path -LiteralPath $Key
 $has=$false;$value=$null;$kind='DWord'
 if($exists){$node=Get-Item -LiteralPath $Key;$has=$node.GetValueNames() -contains $Name;if($has){$value=$node.GetValue($Name);$kind=$node.GetValueKind($Name).ToString()}}
 [pscustomobject]@{key=$Key;name=$Name;keyExisted=$exists;exists=$has;value=$value;kind=$kind}
}
function Assert-DesktopSource([string]$Path){
 $full=[IO.Path]::GetFullPath($Path);$parent=[IO.Path]::GetDirectoryName($full)
 if(-not $parent.Equals($desktop,[StringComparison]::OrdinalIgnoreCase) -and -not $parent.Equals($public,[StringComparison]::OrdinalIgnoreCase)){throw ('不属于原桌面顶层的目标：'+$Path)}
 if([IO.Path]::GetFileName($full) -eq 'Desktop Stacks.lnk'){throw '办公收纳入口不得被隐藏'}
 return $full
}
if(-not(Test-Path -LiteralPath $statePath)){
 if($Action -ne 'Hide' -or $Scope -ne 'All'){$state=[pscustomobject]@{version=1;files=@();registry=@();globalIcons=$null}}else{
 $plan=Get-Content -LiteralPath (Join-Path $app '桌面分区清单.json') -Raw -Encoding UTF8|ConvertFrom-Json
 $files=@(foreach($entry in $plan){if($entry.virtual){continue};$path=Assert-DesktopSource $entry.source;$item=Get-Item -LiteralPath $path -Force;[pscustomobject]@{path=$path;attributes=[int]$item.Attributes;hidden=[bool]($item.Attributes -band [IO.FileAttributes]::Hidden);public=([IO.Path]::GetDirectoryName($path).Equals($public,[StringComparison]::OrdinalIgnoreCase))}})
 $namespaceIds=@($plan|Where-Object virtual|ForEach-Object {$_.source.Substring(2)})
 $registry=@(foreach($key in $namespaceKeys){foreach($id in $namespaceIds){Read-RegistryValue $key $id}})
 $global=Read-RegistryValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'HideIcons'
 $state=[pscustomobject]@{version=1;files=$files;registry=$registry;globalIcons=$global;created=(Get-Date).ToString('yyyy-MM-dd HH:mm:ss')}
 if(-not $DryRun){$state|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $statePath -Encoding UTF8}
 }
}else{$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json}
$autoPath=Join-Path $app '自动收纳记录.json'
$autoConfigPath=Join-Path $app '自动收纳设置.json'
$autoEntries=@()
$autoMutex=New-Object Threading.Mutex($false,'Local\DesktopStacksAutoStore')
$autoLock=$false
try{
 try{$autoLock=$autoMutex.WaitOne(10000)}catch [Threading.AbandonedMutexException]{$autoLock=$true}
 if(-not $autoLock){throw '自动收纳正在处理，请稍后重试'}
 if($Scope -eq 'All' -and $Action -eq 'Restore' -and -not $DryRun -and (Test-Path -LiteralPath $autoConfigPath)){
  $config=Get-Content -LiteralPath $autoConfigPath -Raw -Encoding UTF8|ConvertFrom-Json
  $config.enabled=$false
  $temporary=$autoConfigPath+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
  [IO.File]::WriteAllText($temporary,($config|ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($true)))
  [IO.File]::Replace($temporary,$autoConfigPath,$autoConfigPath+'.bak')
 }
 if(Test-Path -LiteralPath $autoPath){$autoState=Get-Content -LiteralPath $autoPath -Raw -Encoding UTF8|ConvertFrom-Json;$autoEntries=@($autoState.entries)}
 foreach($entry in $autoEntries){$null=Assert-DesktopSource $entry.path;$state.files+=@([pscustomobject]@{path=$entry.path;attributes=$entry.attributes;hidden=$entry.hidden;public=$false})}
}finally{if($autoLock){$autoMutex.ReleaseMutex()};$autoMutex.Dispose()}
$autoSources=@($autoEntries|ForEach-Object path)
if($DryRun){
 [pscustomobject]@{action=$Action;dryRun=$true;filesystemEntries=@($state.files|Where-Object {$Scope -eq 'All' -or $_.public}|ForEach-Object{[pscustomobject]@{path=$_.path;public=$_.public;hidden=$(if($Action -eq 'Hide'){$true}else{$_.hidden})}});registryEntries=$(if($Scope -eq 'All'){@($state.registry)}else{@()});mutationsExecuted=$false}|ConvertTo-Json -Depth 7
 exit 0
}
$failures=New-Object 'System.Collections.Generic.List[object]';$changed=0
if($Action -ne 'Check'){
 foreach($entry in $state.files){
  if($Scope -eq 'Public' -and -not $entry.public){continue}
  try{
   $path=Assert-DesktopSource $entry.path
   if(-not(Test-Path -LiteralPath $path)){if($autoSources -contains $path){continue};throw '原位置不存在'}
   $item=Get-Item -LiteralPath $path -Force;$hidden=($Action -eq 'Hide') -or $entry.hidden
   $attrs=$(if($hidden){$item.Attributes -bor [IO.FileAttributes]::Hidden}else{$item.Attributes -band (-bnot [IO.FileAttributes]::Hidden)})
   if($item.Attributes -ne $attrs){$item.Attributes=$attrs;$changed++}
  }catch{$failures.Add([pscustomobject]@{path=$entry.path;public=$entry.public;reason=$_.Exception.Message})}
 }
 if($Scope -eq 'All'){
  foreach($entry in $state.registry){
   try{
    if($Action -eq 'Hide'){if(-not(Test-Path -LiteralPath $entry.key)){[void](New-Item -Path $entry.key -Force)};[void](New-ItemProperty -LiteralPath $entry.key -Name $entry.name -Value 1 -PropertyType DWord -Force)}
    elseif($entry.exists){if(-not(Test-Path -LiteralPath $entry.key)){[void](New-Item -Path $entry.key -Force)};[void](New-ItemProperty -LiteralPath $entry.key -Name $entry.name -Value $entry.value -PropertyType $entry.kind -Force)}
    elseif(Test-Path -LiteralPath $entry.key){Remove-ItemProperty -LiteralPath $entry.key -Name $entry.name -ErrorAction SilentlyContinue}
   }catch{$failures.Add([pscustomobject]@{path=$entry.key+'\'+$entry.name;public=$false;reason=$_.Exception.Message})}
  }
  $g=$state.globalIcons
  if($Action -eq 'Restore'){
   foreach($key in @($state.registry|Where-Object {-not $_.keyExisted}|Select-Object -ExpandProperty key -Unique)){
    if(Test-Path -LiteralPath $key){$node=Get-Item -LiteralPath $key;if($node.GetValueNames().Count -eq 0 -and $node.GetSubKeyNames().Count -eq 0){Remove-Item -LiteralPath $key}}
   }
  }
  if($null -ne $g){
  if($Action -eq 'Hide'){
   if($g.exists -and [int]$g.value -ne 0){[void](New-ItemProperty -LiteralPath $g.key -Name $g.name -Value 0 -PropertyType DWord -Force)}
  }elseif($g.exists){[void](New-ItemProperty -LiteralPath $g.key -Name $g.name -Value $g.value -PropertyType $g.kind -Force)}
  elseif(Test-Path -LiteralPath $g.key){Remove-ItemProperty -LiteralPath $g.key -Name $g.name -ErrorAction SilentlyContinue}
  }
 }
 if($AllowElevation -and $Scope -eq 'All' -and @($failures|Where-Object public).Count){
  try{
   $publicResult=Join-Path $app '公共桌面显示状态.json';if(Test-Path -LiteralPath $publicResult){Remove-Item -LiteralPath $publicResult}
   $arguments='-NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "'+$PSCommandPath+'" -Action '+$Action+' -Scope Public'
   $process=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru -Wait
   if($process.ExitCode -eq 0){$left=@($failures|Where-Object {-not $_.public});$failures.Clear();foreach($entry in $left){$failures.Add($entry)}}
  }catch{$failures.Add([pscustomobject]@{path='公共桌面';public=$true;reason='Windows管理员确认未完成：'+$_.Exception.Message})}
 }
}
if(-not('OfficeDesktopDisplayNotify' -as [type])){
 Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class OfficeDesktopDisplayNotify {
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern void SHChangeNotify(uint e,uint f,string a,IntPtr b);
}
'@
}
if($Action -ne 'Check'){foreach($path in @($desktop,$public)){[OfficeDesktopDisplayNotify]::SHChangeNotify(0x1000,5,$path,[IntPtr]::Zero)};[OfficeDesktopDisplayNotify]::SHChangeNotify(0x08000000,0,$null,[IntPtr]::Zero)}
$visible=@(foreach($root in @($desktop,$public)){Get-ChildItem -LiteralPath $root -Force|Where-Object {($_.Attributes -band [IO.FileAttributes]::Hidden) -eq 0}|ForEach-Object FullName})
$hiddenFiles=@($state.files|Where-Object {(Test-Path -LiteralPath $_.path) -and ((Get-Item -LiteralPath $_.path -Force).Attributes -band [IO.FileAttributes]::Hidden)}).Count
$result=[pscustomobject]@{action=$Action;scope=$Scope;changed=$changed;hiddenRealEntries=$hiddenFiles;visibleFilesystemItems=$visible;failures=@($failures.ToArray());timestamp=(Get-Date).ToString('yyyy-MM-dd HH:mm:ss')}
$outputPath=$(if($Scope -eq 'Public'){Join-Path $app '公共桌面显示状态.json'}else{$resultPath})
$result|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $outputPath -Encoding UTF8
$result|ConvertTo-Json -Depth 6
if($failures.Count){exit 2}
exit 0
