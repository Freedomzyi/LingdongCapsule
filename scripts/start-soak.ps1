param([string]$AppDirectory,[switch]$ValidateOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(-not $AppDirectory){$AppDirectory=Join-Path $repo 'artifacts\app'}
$source=[IO.Path]::GetFullPath($AppDirectory).TrimEnd('\')
if($source -eq [IO.Path]::GetPathRoot($source).TrimEnd('\')){throw 'Drive-root build input refused.'}
$info=Get-Content -LiteralPath (Join-Path $source 'BUILD-INFO.json') -Raw | ConvertFrom-Json
if($info.sourceDirty -isnot [bool] -or $info.sourceDirty -or $info.sourceCommit -notmatch '^[a-f0-9]{40}$' -or $info.selfContained -isnot [bool] -or -not $info.selfContained){throw 'A clean self-contained build is required.'}
foreach($file in @('LingdongCapsule.exe','LingdongCapsule.dll','coreclr.dll','hostfxr.dll','hostpolicy.dll','PresentationFramework.dll')){if(-not(Test-Path -LiteralPath (Join-Path $source $file) -PathType Leaf)){throw ('Missing standalone app file: '+$file)}}
if((Get-FileHash -LiteralPath (Join-Path $source 'LingdongCapsule.dll') -Algorithm SHA256).Hash -ne $info.appSha256){throw 'App hash differs from build provenance.'}
$entries=@(Get-Item -LiteralPath $source)+@(Get-ChildItem -LiteralPath $source -Recurse -Force)
if(@($entries | Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0}).Count){throw 'Linked build input refused.'}
if($ValidateOnly){Write-Output ('Validated standalone snapshot input: '+$info.sourceCommit+'; app SHA256 '+$info.appSha256+'. No app started.');return}
if(Get-Process -Name LingdongCapsule -ErrorAction SilentlyContinue){throw 'Existing capsule process detected; close it yourself before starting a fixed run. No process was stopped.'}
$id=[Guid]::NewGuid().ToString('N');$root=Join-Path $repo ('artifacts\soak\'+$id);$snapshot=Join-Path $root 'app';$diagnostics=Join-Path $root 'diagnostics'
New-Item -ItemType Directory -Path $snapshot -Force | Out-Null
$inventory=@($entries | Where-Object {-not $_.PSIsContainer} | ForEach-Object {[pscustomobject]@{Path=$_.FullName.Substring($source.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
foreach($entry in Get-ChildItem -LiteralPath $source -Force){Copy-Item -LiteralPath $entry.FullName -Destination $snapshot -Recurse -Force}
foreach($entry in $inventory){if((Get-FileHash -LiteralPath (Join-Path $snapshot $entry.Path) -Algorithm SHA256).Hash -ne $entry.SHA256){throw 'Snapshot copy changed; app not launched.'}}
$inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'inventory.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'watch-soak.ps1') -Destination (Join-Path $root 'watch-soak.ps1')
$child=Start-Process -FilePath (Join-Path $snapshot 'LingdongCapsule.exe') -ArgumentList @('--startup','--diagnostics-dir',('"'+$diagnostics+'"')) -WorkingDirectory $snapshot -WindowStyle Hidden -PassThru
$run=[ordered]@{schemaVersion=1;runId=$id;pid=$child.Id;startedUtc=$child.StartTime.ToUniversalTime().ToString('o');sourceCommit=$info.sourceCommit;appPath=(Join-Path $snapshot 'LingdongCapsule.exe');diagnosticsDirectory=$diagnostics;requiredHours=24;scope='Actual packaged app with existing preferences. No synthetic player, notification, sleep, startup registration or settings changes are injected.'}
$run | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'run.json') -Encoding UTF8
for($i=0;$i -lt 50;$i++){Start-Sleep -Milliseconds 200;$child.Refresh();if($child.HasExited){throw ('App exited before readiness; retained run '+$root)};if(Test-Path -LiteralPath $diagnostics){break}}
if(-not(Test-Path -LiteralPath $diagnostics)){throw ('Diagnostic readiness missing; app was left running for inspection: '+$root)}
$watcher=Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+(Join-Path $root 'watch-soak.ps1')+'"'),'-RunDirectory',('"'+$root+'"')) -WindowStyle Hidden -RedirectStandardOutput (Join-Path $root 'watcher.log') -RedirectStandardError (Join-Path $root 'watcher.err') -PassThru
[pscustomobject]@{WatcherPid=$watcher.Id;StartedUtc=$watcher.StartTime.ToUniversalTime().ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'watcher.json') -Encoding UTF8
Write-Output ('Started actual fixed app; run evidence: '+$root)
Write-Output ('App PID '+$child.Id+'; watcher PID '+$watcher.Id+'. This starts measurement, not acceptance.')
