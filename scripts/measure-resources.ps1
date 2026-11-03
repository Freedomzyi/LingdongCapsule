param([int]$SampleSeconds=30,[string]$Dotnet='dotnet',[string]$OutputDirectory)
$ErrorActionPreference='Stop'
if($SampleSeconds -lt 10 -or $SampleSeconds -gt 300){throw 'SampleSeconds must be 10..300.'}
$root=Split-Path $PSScriptRoot -Parent
if($Dotnet -eq 'dotnet' -and (Test-Path -LiteralPath (Join-Path $root '.tools\dotnet\dotnet.exe'))){$Dotnet=Join-Path $root '.tools\dotnet\dotnet.exe'}
if(-not $OutputDirectory){$OutputDirectory=Join-Path $root ('artifacts\resource-probe\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'build-plugins.ps1') -Dotnet $Dotnet
    & $Dotnet run --project tests/Capsule.WindowsTests -c Release -- --resource-test $OutputDirectory --sample-seconds $SampleSeconds
    if($LASTEXITCODE -ne 0){throw 'Resource probe failed; inspect partial evidence.'}
    Write-Output ('Resource evidence: '+$OutputDirectory)
} finally {Pop-Location}
