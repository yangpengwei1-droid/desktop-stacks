param(
 [string]$ExecutablePath=(Join-Path $PSScriptRoot 'DesktopStacks.exe'),
 [string[]]$ArgumentList=@(),
 [switch]$Background,
 [string]$LogPath
)
$ErrorActionPreference='Stop'
$executable=(Get-Item -LiteralPath $ExecutablePath).FullName
if([IO.Path]::GetExtension($executable) -ne '.exe'){throw 'Specify an existing executable.'}
if(!$LogPath){$LogPath=Join-Path ([IO.Path]::GetDirectoryName($executable)) 'resident-launch.jsonl'}
if(-not ('ResidentLaunchNative' -as [type])){
 Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ResidentLaunchNative {
 [DllImport("kernel32.dll",SetLastError=true)] public static extern bool IsProcessInJob(IntPtr process,IntPtr job,out bool inside);
}
'@
}
function Quote-ProcessArgument([string]$Value){
 $quoted=New-Object Text.StringBuilder;[void]$quoted.Append([char]34);$slashes=0
 foreach($character in $Value.ToCharArray()){
  if($character -eq [char]92){$slashes++;continue}
  if($character -eq [char]34){[void]$quoted.Append([char]92,($slashes*2+1));[void]$quoted.Append([char]34)}
  else{[void]$quoted.Append([char]92,$slashes);[void]$quoted.Append($character)}
  $slashes=0
 }
 [void]$quoted.Append([char]92,($slashes*2));[void]$quoted.Append([char]34);$quoted.ToString()
}
$existing=@(Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($executable)) -ErrorAction SilentlyContinue|Where-Object {$_.Path -eq $executable})
if($existing.Count -gt 1){throw 'Multiple instances exist; inspect them before relaunching.'}
$status='already-running'
if($existing.Count -eq 1){$processId=$existing[0].Id}
else{
 $command=Quote-ProcessArgument $executable
 foreach($argument in $ArgumentList){$command+=' '+(Quote-ProcessArgument $argument)}
 if($Background){$command+=' --background'}
 # Win32_Process.Create does not inherit the calling tool's cleanup job.
 $created=Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine=$command;CurrentDirectory=[IO.Path]::GetDirectoryName($executable)}
 if($created.ReturnValue -ne 0){throw ('Independent process creation failed: '+$created.ReturnValue)}
 $processId=[int]$created.ProcessId;$status='started'
}
$process=$null
for($attempt=0;$attempt -lt 40 -and !$process;$attempt++){
 $process=Get-Process -Id $processId -ErrorAction SilentlyContinue
 if(!$process){Start-Sleep -Milliseconds 100}
}
if(!$process){throw 'The process did not remain running.'}
if($process.HasExited){throw ('The process exited during startup. Exit code: '+$process.ExitCode)}
if($process.Path -ne $executable){throw 'The returned process path did not match the requested executable.'}
$inside=$false
if(![ResidentLaunchNative]::IsProcessInJob($process.Handle,[IntPtr]::Zero,[ref]$inside)){throw 'Could not verify process lifetime ownership.'}
if($inside){throw 'The existing instance is still in a managed job. Exit this app, then relaunch with this script.'}
$metadata=Get-CimInstance Win32_Process -Filter ('ProcessId='+$processId)
$result=[ordered]@{timeUtc=[DateTime]::UtcNow.ToString('o');executable=[IO.Path]::GetFileName($executable);status=$status;processId=$processId;parentProcessId=$metadata.ParentProcessId;inJob=$inside}
$json=$result|ConvertTo-Json -Compress
[IO.File]::AppendAllText([IO.Path]::GetFullPath($LogPath),$json+[Environment]::NewLine,(New-Object Text.UTF8Encoding($false)))
$json
