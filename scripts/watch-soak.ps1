param([Parameter(Mandatory=$true)][string]$RunDirectory,[switch]$Once)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($RunDirectory).TrimEnd('\');$run=Get-Content -LiteralPath (Join-Path $root 'run.json') -Raw | ConvertFrom-Json
if((Split-Path $root -Leaf) -notmatch '^[a-f0-9]{32}$' -or $run.schemaVersion -ne 1 -or $run.runId -ne (Split-Path $root -Leaf) -or $run.requiredHours -ne 24){throw 'Not an owned 24-hour run directory.'}
if($run.appPath -ne (Join-Path $root 'app\LingdongCapsule.exe') -or $run.diagnosticsDirectory -ne (Join-Path $root 'diagnostics')){throw 'Run paths do not belong to this snapshot.'}
function Save-Status($value){$temp=Join-Path $root ('status-'+[Guid]::NewGuid().ToString('N')+'.tmp');$value | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temp -Encoding UTF8;Move-Item -LiteralPath $temp -Destination (Join-Path $root 'status.json') -Force}
do {
    $terminal=$false
    try {
        $child=Get-Process -Id $run.pid -ErrorAction SilentlyContinue
        if(-not $child){Save-Status ([ordered]@{utc=[DateTime]::UtcNow.ToString('o');state='ended-before-review';durationAccepted=$false;reason='Recorded process is no longer running. No restart or crash attribution was assumed.'});break}
        if($child.Path -ne $run.appPath -or [Math]::Abs(($child.StartTime.ToUniversalTime()-[DateTime]::Parse($run.startedUtc).ToUniversalTime()).TotalMilliseconds) -gt 5){Save-Status ([ordered]@{utc=[DateTime]::UtcNow.ToString('o');state='identity-changed';durationAccepted=$false});break}
        $sessions=@(Get-ChildItem -LiteralPath $run.diagnosticsDirectory -Directory -ErrorAction SilentlyContinue)
        if($sessions.Count -ne 1){throw 'Expected one diagnostic session.'}
        $meta=Get-Content -LiteralPath (Join-Path $sessions[0].FullName 'session.json') -Raw | ConvertFrom-Json
        if($meta.pid -ne $run.pid){throw 'Diagnostic session PID mismatch.'}
        $sampleFile=Join-Path $sessions[0].FullName 'samples.jsonl'
        $sample=Get-Content -LiteralPath $sampleFile -Tail 1 | ConvertFrom-Json
        $wall=([DateTime]::UtcNow-[DateTime]::Parse($run.startedUtc).ToUniversalTime()).TotalSeconds
        $age=([DateTime]::UtcNow-[DateTime]::Parse($sample.utc).ToUniversalTime()).TotalSeconds
        $missing=@();foreach($scene in @('idle','media-playing','notification','hidden')){if(-not $sample.sceneSeconds.$scene -or $sample.sceneSeconds.$scene -le 0){$missing+=$scene}}
        $reached=$wall -ge 86400 -and $sample.elapsedSeconds -ge 86400
        $status=[ordered]@{utc=[DateTime]::UtcNow.ToString('o');state=if($reached){'needs-review'}elseif($age -gt 300){'stale-heartbeat'}else{'running'};wallSeconds=$wall;recordedSeconds=$sample.elapsedSeconds;heartbeatAgeSeconds=$age;missingScenes=$missing;resumeCount=$sample.resumeCount;unhandledCount=$sample.unhandledCount;writeErrors=$sample.writeErrors;durationReached=$reached;accepted=$false;note='Duration, scene counters and power events are evidence only. Resource trends, actual coverage and all delivery gates still require review.'}
        if($reached){
            $changes=@();foreach($file in (Get-Content -LiteralPath (Join-Path $root 'inventory.json') -Raw | ConvertFrom-Json)){if(-not(Test-Path -LiteralPath (Join-Path $root ('app\'+$file.Path))) -or (Get-FileHash -LiteralPath (Join-Path $root ('app\'+$file.Path)) -Algorithm SHA256).Hash -ne $file.SHA256){$changes+=$file.Path}}
            $status.snapshotChangedFiles=$changes;$terminal=$true
        }
        Save-Status $status
    } catch {Save-Status ([ordered]@{utc=[DateTime]::UtcNow.ToString('o');state='monitor-error';accepted=$false;errorType=$_.Exception.GetType().FullName;note='Read/monitor failure is not completion; app was not stopped or restarted.'})}
    if($Once -or $terminal){break}
    Start-Sleep -Seconds 60
}while($true)
