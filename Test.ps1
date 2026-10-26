$ErrorActionPreference='Stop'
$root=$PSScriptRoot;$work=Join-Path $root ('work\tests-'+[guid]::NewGuid().ToString('N'))
$manifest=[xml](Get-Content -LiteralPath (Join-Path $root 'src\app.manifest') -Raw -Encoding UTF8)
$supported=@($manifest.SelectNodes('//*[local-name()="supportedOS"]')|ForEach-Object {$_.Id})
if($supported -notcontains '{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}' -or $supported -notcontains '{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}'){throw 'The application manifest must declare supported modern Windows versions.'}
[void][IO.Directory]::CreateDirectory($work)
$binary=Join-Path $work 'DesktopStacks'
& (Join-Path $root 'Build.ps1') -OutputDirectory $binary
$net=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
& (Join-Path $net 'csc.exe') /nologo /target:winexe /platform:x64 /reference:System.Web.Extensions.dll ('/out:'+(Join-Path $work 'ResidentProbe.exe')) (Join-Path $root 'tests\ResidentProbe.cs')
if($LASTEXITCODE -ne 0){throw 'Resident probe did not compile.'}
$probeResult=Join-Path $work 'resident-probe.json';$launchLog=Join-Path $work 'resident-launch.jsonl'
$argumentSamples=@('with spaces','quote"value','C:\ends-with-slash\','')
$firstLaunch=& (Join-Path $binary 'Start-Resident.ps1') -ExecutablePath (Join-Path $work 'ResidentProbe.exe') -ArgumentList (@($probeResult)+$argumentSamples) -LogPath $launchLog|ConvertFrom-Json
try{
 for($attempt=0;$attempt -lt 40 -and !(Test-Path -LiteralPath $probeResult);$attempt++){Start-Sleep -Milliseconds 100}
 $probe=Get-Content -LiteralPath $probeResult -Raw|ConvertFrom-Json
 $secondLaunch=& (Join-Path $binary 'Start-Resident.ps1') -ExecutablePath (Join-Path $work 'ResidentProbe.exe') -ArgumentList @($probeResult) -LogPath $launchLog|ConvertFrom-Json
 if($probe.inJob -or $firstLaunch.inJob -or $firstLaunch.processId -ne $secondLaunch.processId -or $secondLaunch.status -ne 'already-running'){throw 'Independent resident launch or reuse failed.'}
 if($probe.arguments.Count -ne $argumentSamples.Count -or [string]::Join([char]0,$probe.arguments) -ne [string]::Join([char]0,$argumentSamples)){throw 'Resident process arguments did not round-trip.'}
}finally{[IO.File]::WriteAllText($probeResult+'.stop','stop')}
& (Join-Path $net 'csc.exe') /nologo /debug+ /target:exe /reference:System.Web.Extensions.dll ('/out:'+(Join-Path $work 'AutoCollectTests.exe')) (Join-Path $root 'src\DesktopAutoCollect.cs') (Join-Path $root 'src\ShellLink.cs') (Join-Path $root 'tests\AutoCollectTests.cs')
if($LASTEXITCODE -ne 0){throw 'Collector tests did not compile.'}
& (Join-Path $work 'AutoCollectTests.exe') (Join-Path $work 'collector') (Join-Path $binary '图标提取.exe')
if($LASTEXITCODE -ne 0){Get-ChildItem -LiteralPath $work -Recurse -File -Filter '测试失败.txt'|ForEach-Object {Get-Content -LiteralPath $_.FullName -Encoding UTF8};throw 'Collector integration tests failed.'}
$fixture=Join-Path $work 'fixtures'
foreach($group in @('左边栏','中间栏','右边栏')){$dir=Join-Path $fixture $group;[void][IO.Directory]::CreateDirectory($dir);[IO.File]::WriteAllText((Join-Path $dir '示例资料.txt'),'Public synthetic sample.');[void][IO.Directory]::CreateDirectory((Join-Path $dir '示例文件夹'))}
& (Join-Path $binary 'Initialize.ps1') -AppDirectory $binary -FixtureRoot $fixture
$process=Start-Process -FilePath (Join-Path $binary 'DesktopStacks.exe') -ArgumentList ('--self-test "'+(Join-Path $work 'ui.json')+'"') -PassThru -Wait
if($process.ExitCode -ne 0){throw 'WPF self-test failed. See 最近启动错误.txt in the isolated test directory.'}
$layerRefs=@('System.Xaml.dll','System.Web.Extensions.dll')|ForEach-Object{'/reference:'+(Join-Path $net $_)}
$layerRefs+=@('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll')|ForEach-Object{'/reference:'+(Join-Path (Join-Path $net 'WPF') $_)}
& (Join-Path $net 'csc.exe') /nologo /target:exe ('/out:'+(Join-Path $binary 'DesktopLayerTests.exe')) ('/reference:'+(Join-Path $binary 'DesktopStacks.exe')) @layerRefs (Join-Path $root 'tests\DesktopLayerTests.cs')
if($LASTEXITCODE -ne 0){throw 'Desktop layer tests did not compile.'}
& (Join-Path $binary 'DesktopLayerTests.exe') $binary
if($LASTEXITCODE -ne 0){throw 'Desktop layer regression tests failed.'}
$settings=Get-Content -LiteralPath (Join-Path $binary '自动收纳设置.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($settings.enabled -or $settings.startWithWindows){throw 'First-run settings must be opt-in.'}
$dry=Join-Path $work 'restore-preview.json'
& (Join-Path $binary '桌面显示.ps1') -Action Restore -DryRun|Set-Content -LiteralPath $dry -Encoding UTF8
if((Get-Content -LiteralPath $dry -Raw -Encoding UTF8|ConvertFrom-Json).mutationsExecuted){throw 'DryRun must not mutate state.'}
if(Test-Path -LiteralPath (Join-Path $binary '桌面显示原状态.json')){throw 'DryRun created a permanent state snapshot.'}
Write-Output ('PASS: independent resident launch, collector, synthetic initialization, WPF, desktop layer, opt-in defaults, restore preview. Results: '+$work)
