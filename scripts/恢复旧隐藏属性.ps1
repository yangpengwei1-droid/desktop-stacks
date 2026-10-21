param([ValidateSet('All','Public')][string]$Scope='All',[switch]$AllowElevation,[switch]$DryRun)
$ErrorActionPreference='Stop';$app=$PSScriptRoot
$desktop=[Environment]::GetFolderPath('Desktop');$public=[Environment]::GetFolderPath('CommonDesktopDirectory')
$statePath=Join-Path $app '桌面显示原状态.json'
$state=if(Test-Path -LiteralPath $statePath){Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json}else{[pscustomobject]@{files=@();registry=@();globalIcons=$null}}
$autoPath=Join-Path $app '自动收纳记录.json'
if(Test-Path -LiteralPath $autoPath){$auto=Get-Content -LiteralPath $autoPath -Raw -Encoding UTF8|ConvertFrom-Json;foreach($entry in $auto.entries){$state.files+=@([pscustomobject]@{path=$entry.path;hidden=$entry.hidden;public=$false})}}
function Assert-Source([string]$Path){$full=[IO.Path]::GetFullPath($Path);$parent=[IO.Path]::GetDirectoryName($full);if($parent -ne $desktop -and $parent -ne $public){throw '旧记录不属于桌面顶层'};return $full}
if($DryRun){[pscustomobject]@{action='restore-legacy-attributes';dryRun=$true;fileCount=@($state.files).Count;mutationsExecuted=$false}|ConvertTo-Json;exit 0}
$failures=New-Object 'System.Collections.Generic.List[object]';$changed=0
foreach($entry in $state.files){
 if($Scope -eq 'Public' -and -not $entry.public){continue}
 try{$path=Assert-Source $entry.path;if(-not(Test-Path -LiteralPath $path)){continue};$item=Get-Item -LiteralPath $path -Force;$attrs=if($entry.hidden){$item.Attributes -bor [IO.FileAttributes]::Hidden}else{$item.Attributes -band (-bnot [IO.FileAttributes]::Hidden)};if($attrs -ne $item.Attributes){$item.Attributes=$attrs;$changed++}}
 catch{$failures.Add([pscustomobject]@{path=$entry.path;public=$entry.public;reason=$_.Exception.Message})}
}
if($Scope -eq 'All'){
 foreach($entry in $state.registry){
  try{if($entry.exists){if(-not(Test-Path -LiteralPath $entry.key)){[void](New-Item -Path $entry.key -Force)};[void](New-ItemProperty -LiteralPath $entry.key -Name $entry.name -Value $entry.value -PropertyType $entry.kind -Force)}elseif(Test-Path -LiteralPath $entry.key){Remove-ItemProperty -LiteralPath $entry.key -Name $entry.name -ErrorAction SilentlyContinue}}
  catch{$failures.Add([pscustomobject]@{path=$entry.key;public=$false;reason=$_.Exception.Message})}
 }
 $g=$state.globalIcons
 if($null -ne $g){if($g.exists){[void](New-ItemProperty -LiteralPath $g.key -Name $g.name -Value $g.value -PropertyType $g.kind -Force)}elseif(Test-Path -LiteralPath $g.key){Remove-ItemProperty -LiteralPath $g.key -Name $g.name -ErrorAction SilentlyContinue}}
}
if($Scope -eq 'All' -and $AllowElevation -and @($failures|Where-Object public).Count){
 try{$p=Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList ('-NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "'+$PSCommandPath+'" -Scope Public') -Verb RunAs -WindowStyle Hidden -PassThru -Wait;if($p.ExitCode -eq 0){$remaining=@($failures|Where-Object {-not $_.public});$failures.Clear();foreach($entry in $remaining){$failures.Add($entry)}}}
 catch{$failures.Add([pscustomobject]@{path='公共桌面';public=$true;reason=$_.Exception.Message})}
}
$result=[pscustomobject]@{strategy='restore-legacy-attributes-only';changed=$changed;failures=@($failures.ToArray());timestamp=(Get-Date).ToString('s')}
$result|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $app ('旧属性恢复结果-'+$Scope+'.json')) -Encoding UTF8
$result|ConvertTo-Json -Depth 5
if($failures.Count){exit 2}
