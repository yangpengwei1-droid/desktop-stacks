param([string]$AppDirectory=$PSScriptRoot,[string]$LayoutPath,[string]$FixtureRoot,[switch]$IncludePublic)
$ErrorActionPreference='Stop'
$AppDirectory=[IO.Path]::GetFullPath($AppDirectory)
if(Test-Path -LiteralPath (Join-Path $AppDirectory '设置.json')){Write-Output 'Already initialized; original layout preserved.';exit 0}
$desktop=[Environment]::GetFolderPath('Desktop')
$public=[Environment]::GetFolderPath('CommonDesktopDirectory')
$groups=@('左边栏','中间栏','右边栏')
$folders=Join-Path $AppDirectory '收纳文件夹'
$cache=Join-Path $AppDirectory '原图标缓存'
foreach($folder in @($folders,$cache)+@($groups|ForEach-Object {Join-Path $folders $_})){[void][IO.Directory]::CreateDirectory($folder)}
if($FixtureRoot){
 $layout=@(Get-ChildItem -LiteralPath $FixtureRoot -Directory|Where-Object {$groups -contains $_.Name}|ForEach-Object {$g=$_.Name;Get-ChildItem -LiteralPath $_.FullName|ForEach-Object {[pscustomobject]@{source=$_.FullName;name=$_.Name;group=$g;x=0;y=0}}})
}elseif($LayoutPath){
 $layout=@(Get-Content -LiteralPath $LayoutPath -Raw -Encoding UTF8|ConvertFrom-Json)
}else{
 Add-Type -AssemblyName System.Windows.Forms
 Add-Type -Path (Join-Path $AppDirectory 'DesktopLayout.cs')
 $shell=New-Object -ComObject Shell.Application;$hwnd=0
 $view=$shell.Windows().FindWindowSW(0,$null,8,[ref]$hwnd,1)
 $layout=@([DesktopLayoutReader]::Read($view,[System.Windows.Forms.SystemInformation]::VirtualScreen.Width)|Sort-Object y,x)
}
$entries=[ordered]@{};$icons=[ordered]@{};$columns=[ordered]@{'左边栏'=4;'中间栏'=5;'右边栏'=4};$plan=New-Object 'System.Collections.Generic.List[object]'
[void][Reflection.Assembly]::LoadFrom((Join-Path $AppDirectory 'DesktopStacks.exe'))
$extract=Join-Path $AppDirectory '图标提取.exe'
function Export-Icon([string]$Source,[string]$Destination,[bool]$Thumbnail=$false,[int]$Size=48){
 $mode=if($Thumbnail){'thumbnail'}else{'icon'}
 $p=Start-Process -FilePath $extract -ArgumentList ('"'+$Source+'" "'+$Destination+'" '+$mode+' '+$Size) -WindowStyle Hidden -PassThru
 if(-not $p.WaitForExit(2500)){try{$p.Kill()}catch{}}
}
foreach($group in $groups){
 $entries[$group]=@();$directory=Join-Path $folders $group;$categoryIcon=Join-Path $cache ($group+'.png')
 Export-Icon $directory $categoryIcon $false 128
 $icons['folder:'+$group]=@{source=$directory;icon=$categoryIcon;stamp=[IO.Directory]::GetLastWriteTimeUtc($directory).Ticks.ToString()}
}
Copy-Item -LiteralPath (Join-Path $cache '左边栏.png') -Destination (Join-Path $cache '默认文件夹.png')
foreach($entry in $layout){
 if($groups -notcontains $entry.group){throw 'Layout group must be 左边栏, 中间栏 or 右边栏.'}
 $source=[string]$entry.source;$virtual=$source -in @('::{20D04FE0-3AEA-1069-A2D8-08002B30309D}','::{645FF040-5081-101B-9F08-00AA002F954E}')
 if(-not $virtual){
  if(-not(Test-Path -LiteralPath $source)){continue}
  $item=Get-Item -LiteralPath $source -Force
  if($item.Name.StartsWith('.') -or $item.Name.StartsWith('~') -or $item.Name.EndsWith('~') -or $item.Extension.ToLowerInvariant() -in @('.tmp','.temp','.part','.partial','.download','.crdownload','.swp')){continue}
  if(($item.Attributes -band ([IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System)) -ne 0){continue}
  if(-not $FixtureRoot){$parent=[IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($source));if($parent -ne $desktop -and (-not $IncludePublic -or $parent -ne $public)){continue}}
  if($item.Name -in @('Desktop Stacks.lnk','desktop.ini')){continue}
  $name=if($item.Extension -eq '.lnk'){$item.BaseName}else{$item.Name}
 }else{$name=[string]$entry.name}
 $leaf=if($name.Length -gt 96){$name.Substring(0,96)}else{$name}
 $linkName=$leaf+'.lnk';$destination=Join-Path (Join-Path $folders $entry.group) $linkName;$suffix=2
 while(Test-Path -LiteralPath $destination){$linkName=$leaf+' ('+$suffix+').lnk';$suffix++;$destination=Join-Path (Join-Path $folders $entry.group) $linkName}
 if(-not $virtual -and [IO.Path]::GetExtension($source) -eq '.lnk'){Copy-Item -LiteralPath $source -Destination $destination}else{
  if($virtual){[DesktopStacks.NativeShortcut]::Create($destination,(Join-Path $env:SystemRoot 'explorer.exe'),('shell:'+$source),$null,$null)}else{[DesktopStacks.NativeShortcut]::Create($destination,$source,$null,$null,$null)}
 }
 $sha=[Security.Cryptography.SHA256]::Create();try{$key=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($source)))).Replace('-','')}finally{$sha.Dispose()}
 $icon=Join-Path $cache ($key.Substring(0,20)+'.png')
 $thumbnail=(-not $virtual -and ((Test-Path -LiteralPath $source -PathType Container) -or [IO.Path]::GetExtension($source) -in @('.png','.jpg','.jpeg')))
 Export-Icon $source $icon $thumbnail
 $icons[$entry.group+'/'+$linkName]=@{source=$source;icon=$icon;displayName=$name;thumbnail=$thumbnail;stamp=$(if($virtual){'namespace'}else{[IO.File]::GetLastWriteTimeUtc($source).Ticks.ToString()})}
 $entries[$entry.group]+=$linkName
 $plan.Add([pscustomobject]@{source=$source;name=$name;group=$entry.group;virtual=$virtual;x=$entry.x;y=$entry.y})
}
$icons|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $AppDirectory '原图标来源.json') -Encoding UTF8
ConvertTo-Json -InputObject @($plan.ToArray()) -Depth 5|Set-Content -LiteralPath (Join-Path $AppDirectory '桌面分区清单.json') -Encoding UTF8
@{enabled=$false;group='左边栏';startWithWindows=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $AppDirectory '自动收纳设置.json') -Encoding UTF8
@{mode='grid';order=$groups;entries=$entries;columns=$columns}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $AppDirectory '设置.json') -Encoding UTF8
if(-not $FixtureRoot){
 [DesktopStacks.NativeShortcut]::Create((Join-Path $desktop 'Desktop Stacks.lnk'),(Join-Path $AppDirectory 'DesktopStacks.exe'),$null,$AppDirectory,(Join-Path $AppDirectory '入口图标\办公收纳.ico'))
}
Write-Output ('Initialized '+$plan.Count+' entries. Nothing moved or hidden; auto-collection and login startup are off.')
