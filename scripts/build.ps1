param([string]$Dotnet = 'dotnet',[switch]$RequireClean)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Bundle.Common.ps1')
. (Join-Path $PSScriptRoot 'Notices.Common.ps1')
if($Dotnet -eq 'dotnet' -and (Test-Path -LiteralPath (Join-Path $root '.tools\dotnet\dotnet.exe'))) {
    $Dotnet=Join-Path $root '.tools\dotnet\dotnet.exe'
}
Push-Location $root
try {
    $revision=(& git rev-parse HEAD).Trim();if($LASTEXITCODE -ne 0){throw 'Cannot identify source revision.'}
    $dirty=-not [string]::IsNullOrWhiteSpace((& git status --porcelain --untracked-files=normal | Out-String))
    if($LASTEXITCODE -ne 0){throw 'Cannot inspect source worktree.'}
    if($RequireClean -and $dirty){throw 'A clean source tree is required.'}
    & (Join-Path $PSScriptRoot 'build-plugins.ps1') -Dotnet $Dotnet
    & $Dotnet run --project tests/Capsule.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'State tests failed' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'test-deployment.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Windows PowerShell deployment tests failed' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'test-registration.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Windows PowerShell registration tests failed' }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'test-bundle.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Windows PowerShell bundle tests failed' }
    & $Dotnet build tests/Capsule.WindowsTests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Windows integration test build failed' }
    $outputRoot=Join-Path $root 'artifacts';$stage=New-CapsulePublishStage $outputRoot
    & $Dotnet publish src/Capsule.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false -o $stage
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    foreach($name in 'Capsule.Core.pdb','LingdongCapsule.pdb') {
        $symbol=Join-Path $stage $name
        if(Test-Path -LiteralPath $symbol){Remove-Item -LiteralPath $symbol}
    }
    Copy-Item README.md,LICENSE,THIRD-PARTY-NOTICES.md,Install.ps1,Uninstall.ps1,Deployment.Common.ps1,安装.cmd -Destination $stage
    foreach($script in 'Install.ps1','Uninstall.ps1','Deployment.Common.ps1') {
        $path=Join-Path $stage $script
        [IO.File]::WriteAllText($path,[IO.File]::ReadAllText($path),[Text.UTF8Encoding]::new($true))
    }
    New-Item -ItemType Directory -Path (Join-Path $stage 'docs') -Force | Out-Null
    Copy-Item docs/* -Destination (Join-Path $stage 'docs') -Recurse -Force
    New-Item -ItemType Directory -Path (Join-Path $stage 'plugins') -Force | Out-Null
    $pluginFiles=@(Get-CapsulePluginArtifacts (Join-Path $outputRoot 'plugins'))
    Copy-Item -LiteralPath $pluginFiles -Destination (Join-Path $stage 'plugins')
    Copy-Item -LiteralPath (Join-Path $outputRoot 'plugins/SHA256SUMS.txt') -Destination (Join-Path $stage 'plugins')
    $dotnetRoot=Split-Path (Get-Command $Dotnet -CommandType Application -ErrorAction Stop).Source -Parent
    Copy-CapsuleDependencyNotices -Stage $stage -RepositoryRoot $root -AssetsFile (Join-Path $root 'src/Capsule.App/obj/project.assets.json') -DotnetRoot $dotnetRoot
    $sdk=(& $Dotnet --version).Trim();if($LASTEXITCODE -ne 0){throw 'Cannot identify build SDK.'}
    $endRevision=(& git rev-parse HEAD).Trim();if($LASTEXITCODE -ne 0 -or $endRevision -ne $revision){throw 'Source commit changed while building; candidate not committed.'}
    $endStatus=(& git status --porcelain --untracked-files=normal | Out-String);if($LASTEXITCODE -ne 0){throw 'Cannot inspect final source worktree.'}
    $dirty=$dirty -or -not [string]::IsNullOrWhiteSpace($endStatus)
    if($RequireClean -and $dirty){throw 'Source tree changed while building; candidate not committed.'}
    $info=[ordered]@{schema=1;sourceCommit=$revision;sourceDirty=[bool]$dirty;utc=[DateTime]::UtcNow.ToString('o');sdk=$sdk;rid='win-x64';selfContained=$true;version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $stage 'LingdongCapsule.dll')).ProductVersion;appSha256=(Get-FileHash -LiteralPath (Join-Path $stage 'LingdongCapsule.dll') -Algorithm SHA256).Hash;noticesSha256=(Get-FileHash -LiteralPath (Join-Path $stage 'licenses/components.json') -Algorithm SHA256).Hash}
    [IO.File]::WriteAllText((Join-Path $stage 'BUILD-INFO.json'),($info | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    Publish-CapsuleBundle -Stage $stage -OutputRoot $outputRoot
} finally { Pop-Location }
