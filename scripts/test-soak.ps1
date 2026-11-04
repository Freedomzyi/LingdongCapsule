$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
foreach($script in @('start-soak.ps1','watch-soak.ps1')){
    $tokens=$null;$parseErrors=$null;[System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $script),[ref]$tokens,[ref]$parseErrors) | Out-Null
    if($parseErrors.Count){throw ('PowerShell parser failed: '+$script)}
}
$root=Join-Path $repo ('artifacts\soak-tests\'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root | Out-Null
$id=[Guid]::NewGuid().ToString('N');$runRoot=Join-Path $root $id;New-Item -ItemType Directory -Path $runRoot | Out-Null
$run=@{schemaVersion=1;runId=$id;pid=$PID;startedUtc=(Get-Process -Id $PID).StartTime.ToUniversalTime().ToString('o');requiredHours=24;appPath=(Join-Path $runRoot 'app\LingdongCapsule.exe');diagnosticsDirectory=(Join-Path $runRoot 'diagnostics')}
$run | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'run.json') -Encoding UTF8
& (Join-Path $PSScriptRoot 'watch-soak.ps1') -RunDirectory $runRoot -Once
if((Get-Content -LiteralPath (Join-Path $runRoot 'status.json') -Raw | ConvertFrom-Json).state -ne 'identity-changed'){throw 'Watcher accepted an unrelated live process.'}
$run.appPath=Join-Path $root 'outside.exe';$run | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'run.json') -Encoding UTF8
$refused=$false;try{& (Join-Path $PSScriptRoot 'watch-soak.ps1') -RunDirectory $runRoot -Once}catch{$refused=$true}
if(-not $refused){throw 'Watcher accepted paths outside owned snapshot.'}
$dirty=Join-Path $root 'dirty';New-Item -ItemType Directory -Path $dirty | Out-Null
@{sourceDirty=$true;sourceCommit=('a'*40);selfContained=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dirty 'BUILD-INFO.json') -Encoding UTF8
$refused=$false;try{& (Join-Path $PSScriptRoot 'start-soak.ps1') -AppDirectory $dirty}catch{$refused=$_.Exception.Message -like '*clean self-contained build*'}
if(-not $refused){throw 'Dirty source accepted by soak launcher.'}
Write-Output ('PASS PS5 soak parser, unrelated process identity, snapshot path and dirty source guards; no app launched or stopped. Evidence: '+$root)
