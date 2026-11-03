param([string]$Dotnet='dotnet',[switch]$IncludeLiveMedia)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if($Dotnet -eq 'dotnet' -and (Test-Path -LiteralPath (Join-Path $root '.tools\dotnet\dotnet.exe'))){$Dotnet=Join-Path $root '.tools\dotnet\dotnet.exe'}
$evidence=Join-Path $root ('artifacts/verification/'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-ffff'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$results=[Collections.Generic.List[object]]::new()
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'build-plugins.ps1') -Dotnet $Dotnet
    $suites=@(
        @{Name='core';Project='tests/Capsule.Tests';Args=@()},
        @{Name='materials';Project='tests/Capsule.WindowsTests';Args=@('--material-test')},
        @{Name='plugins';Project='tests/Capsule.WindowsTests';Args=@('--plugin-test')},
        @{Name='msp';Project='tests/Capsule.WindowsTests';Args=@('--msp-test')},
        @{Name='solo';Project='tests/Capsule.WindowsTests';Args=@('--solo-test')},
        @{Name='release';Project='tests/Capsule.WindowsTests';Args=@('--release-test')},
        @{Name='shortcuts';Project='tests/Capsule.WindowsTests';Args=@('--shortcut-test')},
        @{Name='notifications';Project='tests/Capsule.WindowsTests';Args=@('--notification-test',(Join-Path $evidence 'notifications'))},
        @{Name='overview';Project='tests/Capsule.WindowsTests';Args=@('--overview-test',(Join-Path $evidence 'overview'))}
    )
    if($IncludeLiveMedia){$suites+=@{Name='live-media';Project='tests/Capsule.WindowsTests';Args=@('--audio-probe','--render-media',(Join-Path $evidence 'media'))}}
    foreach($suite in $suites){
        $watch=[Diagnostics.Stopwatch]::StartNew()
        $arguments=@('run','--project',$suite.Project,'-c','Release','--')+$suite.Args
        & $Dotnet @arguments 2>&1 | Tee-Object -FilePath (Join-Path $evidence ($suite.Name+'.log'))
        $code=$LASTEXITCODE
        $watch.Stop()
        $results.Add([pscustomobject]@{Suite=$suite.Name;ExitCode=$code;Seconds=[Math]::Round($watch.Elapsed.TotalSeconds,2)})
        if($code -ne 0){throw ('Verification failed: '+$suite.Name+'; see '+$evidence)}
    }
} finally {
    $results.ToArray() | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'results.json') -Encoding utf8
    Pop-Location
}
Write-Output ('Verification completed. Evidence: '+$evidence)
