param([ValidateSet('Hide','Restore','Check')][string]$Action='Check',[ValidateSet('All','Public')][string]$Scope='All',[switch]$AllowElevation,[switch]$DryRun)
$ErrorActionPreference='Stop';$app=$PSScriptRoot
$modePath=Join-Path $app '桌面显示模式.json'
$enabled=if(Test-Path -LiteralPath $modePath){[bool](Get-Content -LiteralPath $modePath -Raw -Encoding UTF8|ConvertFrom-Json).enabled}else{$false}
if($Action -ne 'Check'){$enabled=$Action -eq 'Hide'}
if($DryRun){[pscustomobject]@{action=$Action;strategy='desktop-view-layer';enabled=$enabled;dryRun=$true;filesystemEntries=@();registryEntries=@();mutationsExecuted=$false}|ConvertTo-Json -Depth 4;exit 0}
if($Scope -eq 'Public'){throw '桌面视图无需公共桌面提权；旧属性迁移请使用恢复旧隐藏属性.ps1'}
if($Action -eq 'Restore'){
 $configPath=Join-Path $app '自动收纳设置.json';$mutex=New-Object Threading.Mutex($false,'Local\DesktopStacksAutoStore');$locked=$false
 try{try{$locked=$mutex.WaitOne(10000)}catch [Threading.AbandonedMutexException]{$locked=$true};if(-not $locked){throw '自动收纳忙，请稍后重试'}
  if(Test-Path -LiteralPath $configPath){$config=Get-Content -LiteralPath $configPath -Raw -Encoding UTF8|ConvertFrom-Json;$config.enabled=$false;$temp=$configPath+'.'+[guid]::NewGuid().ToString('N')+'.tmp';[IO.File]::WriteAllText($temp,($config|ConvertTo-Json -Depth 5),(New-Object Text.UTF8Encoding($true)));[IO.File]::Replace($temp,$configPath,$configPath+'.bak')}
 }finally{if($locked){$mutex.ReleaseMutex()};$mutex.Dispose()}
}
if($Action -ne 'Check'){
 $shell=New-Object -ComObject Shell.Application;$hwnd=0;$view=$shell.Windows().FindWindowSW(0,$null,8,[ref]$hwnd,1)
 if($null -eq $view){throw 'Windows桌面尚未就绪；原文件保持可见'}
 $flags=[uint32]$view.Document.FolderFlags;$view.Document.FolderFlags=if($enabled){$flags -bor 0x1000}else{$flags -band (-bnot 0x1000)}
 $mode=[pscustomobject]@{enabled=$enabled;strategy='desktop-view-layer';saveDialogsKeepOriginalVisibility=$true;timestamp=(Get-Date).ToString('s')}
 $temp=$modePath+'.'+[guid]::NewGuid().ToString('N')+'.tmp';[IO.File]::WriteAllText($temp,($mode|ConvertTo-Json),(New-Object Text.UTF8Encoding($true)))
 if(Test-Path -LiteralPath $modePath){[IO.File]::Replace($temp,$modePath,$modePath+'.bak')}else{[IO.File]::Move($temp,$modePath)}
}
$result=[pscustomobject]@{action=$Action;strategy='desktop-view-layer';enabled=$enabled;fileAttributesChanged=0;registryValuesChanged=0;saveDialogsKeepOriginalVisibility=$true}
$result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $app '桌面显示状态.json') -Encoding UTF8
$result|ConvertTo-Json
