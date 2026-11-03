param([string]$OutputRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Bundle.Common.ps1')
if(-not $OutputRoot){$OutputRoot=Join-Path $repo ('artifacts\bundle-tests\'+[Guid]::NewGuid().ToString('N'))}
$root=[IO.Path]::GetFullPath($OutputRoot);New-Item -ItemType Directory -Path $root -Force | Out-Null
$results=[Collections.Generic.List[string]]::new();$global:CapsuleBundleFault=@{Armed=$false;Target=(Join-Path $root 'LingdongCapsule-win-x64.zip')}
function Check([bool]$ok,[string]$label){if(-not $ok){throw $label};$results.Add($label);Write-Host ('PASS '+$label)}
function Reject([scriptblock]$action,[string]$label){$failed=$false;try{& $action | Out-Null}catch{$failed=$true};Check $failed $label}
function Move-Item {
    [CmdletBinding()]param([string]$LiteralPath,[string]$Destination)
    if($global:CapsuleBundleFault.Armed -and $Destination -eq $global:CapsuleBundleFault.Target){$global:CapsuleBundleFault.Armed=$false;throw [IO.IOException]::new('Owned archive commit fault')}
    Microsoft.PowerShell.Management\Move-Item @PSBoundParameters
}
function Fixture([string]$value) {
    $stage=New-CapsulePublishStage $root
    foreach($name in @('LingdongCapsule.exe','LingdongCapsule.dll','LingdongCapsule.deps.json','LingdongCapsule.runtimeconfig.json','coreclr.dll','hostfxr.dll','hostpolicy.dll','PresentationFramework.dll','Install.ps1','Uninstall.ps1','Deployment.Common.ps1','README.md','LICENSE','THIRD-PARTY-NOTICES.md')){[IO.File]::WriteAllText((Join-Path $stage $name),$value+' '+$name)}
    New-Item -ItemType Directory -Path (Join-Path $stage 'docs') | Out-Null
    [IO.File]::WriteAllText((Join-Path $stage 'docs\说明.txt'),'Owned unicode file')
    [IO.File]::WriteAllText((Join-Path $stage '.owned-hidden'),'Owned hidden file');[IO.File]::SetAttributes((Join-Path $stage '.owned-hidden'),[IO.FileAttributes]::Hidden)
    $notices=Join-Path $stage 'licenses';New-Item -ItemType Directory -Path $notices | Out-Null
    $noticeNames=@('SOURCES.md','NAudio-LICENSE.txt','SPDX-MIT.txt','ModelSensingProtocol-NOTICE.txt','Microsoft.NETCore.App-LICENSE.txt','Microsoft.NETCore.App-THIRD-PARTY-NOTICES.txt','Microsoft.WindowsDesktop.App-LICENSE.txt','NET-Windows-Library-LICENSE.txt','Windows-SDK-LICENSE.txt','Windows-SDK-Targeting-LICENSE.rtf')
    foreach($name in $noticeNames){[IO.File]::WriteAllText((Join-Path $notices $name),'Owned notice fixture')}
    $noticeFiles=@($noticeNames | ForEach-Object {@{path=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $notices $_)).Hash}})
    @{schema=1;components=@(@{name='Owned fixture';notices=$noticeNames});files=$noticeFiles} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $notices 'components.json') -Encoding UTF8
    @{schema=1;sourceCommit=('a'*40);sourceDirty=$false;rid='win-x64';selfContained=$true;appSha256=(Get-FileHash -LiteralPath (Join-Path $stage 'LingdongCapsule.dll') -Algorithm SHA256).Hash;noticesSha256=(Get-FileHash -LiteralPath (Join-Path $notices 'components.json')).Hash} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'BUILD-INFO.json') -Encoding UTF8
    return $stage
}
function Snapshot {
    $files=@(Get-ChildItem -LiteralPath (Join-Path $root 'app') -File -Recurse -Force | Sort-Object FullName | ForEach-Object {$_.FullName.Substring($root.Length)+' '+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash})
    return (@($files,(Get-FileHash -LiteralPath (Join-Path $root 'LingdongCapsule-win-x64.zip')).Hash,[IO.File]::ReadAllText((Join-Path $root 'SHA256SUMS.txt'))) | ConvertTo-Json -Depth 4 -Compress)
}
try {
    $stage=Fixture 'owned v1';$first=Publish-CapsuleBundle $stage $root
    Check ($first.SHA256 -eq (Get-FileHash -LiteralPath $first.Archive -Algorithm SHA256).Hash -and [IO.File]::ReadAllText((Join-Path $root 'SHA256SUMS.txt')).StartsWith($first.SHA256)) 'Archive and external checksum commit the same bytes'
    $zip=[IO.Compression.ZipFile]::OpenRead($first.Archive)
    try{Check ($zip.GetEntry('.owned-hidden') -ne $null -and $zip.GetEntry('docs/说明.txt') -ne $null -and $zip.GetEntry('BUILD-INFO.json') -ne $null) 'ZIP retains hidden files, Unicode paths and build provenance'}finally{$zip.Dispose()}
    [IO.File]::WriteAllText((Join-Path $root 'app\obsolete.owned.dll'),'Owned obsolete output')
    $stage=Fixture 'owned v2';$second=Publish-CapsuleBundle $stage $root
    $zip=[IO.Compression.ZipFile]::OpenRead($second.Archive)
    try{Check (-not(Test-Path -LiteralPath (Join-Path $root 'app\obsolete.owned.dll')) -and $zip.GetEntry('obsolete.owned.dll') -eq $null) 'Fresh staging excludes obsolete output from application and archive'}finally{$zip.Dispose()}
    $before=Snapshot;$stage=Fixture 'owned locked failure';$hold=[IO.File]::Open($second.Archive,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
    try{Reject {Publish-CapsuleBundle $stage $root} 'Locked previous archive refuses the commit'}finally{$hold.Dispose()}
    Check ((Snapshot) -eq $before) 'Locked archive restores application, ZIP and checksum exactly'
    $stage=Fixture 'owned injected failure';$global:CapsuleBundleFault.Armed=$true
    Reject {Publish-CapsuleBundle $stage $root} 'Failure after application switch is injected'
    Check (-not $global:CapsuleBundleFault.Armed -and (Snapshot) -eq $before) 'Post-switch archive failure restores all previous output bytes'
    $stage=Fixture 'owned missing runtime';Remove-Item -LiteralPath (Join-Path $stage 'coreclr.dll')
    Reject {Publish-CapsuleBundle $stage $root} 'Missing self-contained runtime refuses publication'
    $stage=Fixture 'owned bad provenance';[IO.File]::AppendAllText((Join-Path $stage 'LingdongCapsule.dll'),'changed after manifest')
    Reject {Publish-CapsuleBundle $stage $root} 'Mismatched application provenance refuses publication'
    $stage=Fixture 'owned bad notice';[IO.File]::AppendAllText((Join-Path $stage 'licenses\NAudio-LICENSE.txt'),'changed after manifest')
    Reject {Publish-CapsuleBundle $stage $root} 'Mismatched dependency notice refuses publication'
    $outside=Join-Path $root 'outside';New-Item -ItemType Directory -Path $outside | Out-Null;[IO.File]::WriteAllText((Join-Path $outside 'owned.txt'),'keep')
    Reject {Publish-CapsuleBundle $outside $root} 'Unowned publish directory name refuses mutation'
    $stage=Fixture 'owned linked input';New-Item -ItemType Junction -Path (Join-Path $stage 'escape') -Target $outside | Out-Null
    Reject {Publish-CapsuleBundle $stage $root} 'Linked input is rejected before archive creation'
    Check ((Snapshot) -eq $before -and [IO.File]::ReadAllText((Join-Path $outside 'owned.txt')) -eq 'keep') 'Rejected sources leave published output and outside fixture unchanged'
    Check (@(Get-ChildItem -LiteralPath $root | Where-Object {$_.Name -match '^(bundle-|build-backup-)'}).Count -eq 0) 'Finished transactions leave no temporary archive or backup'
    $plugins=Join-Path $root 'plugin-artifacts';New-Item -ItemType Directory -Path $plugins | Out-Null
    $plugin=Join-Path $plugins 'owned.example-0.1.0.zip';[IO.File]::WriteAllText($plugin,'Owned plugin package fixture');[IO.File]::WriteAllText((Join-Path $plugins 'obsolete.example-0.0.1.zip'),'must not be included')
    [IO.File]::WriteAllText((Join-Path $plugins 'SHA256SUMS.txt'),(Get-FileHash -LiteralPath $plugin).Hash+'  owned.example-0.1.0.zip')
    $listed=@(Get-CapsulePluginArtifacts $plugins);Check ($listed.Count -eq 1 -and $listed[0] -eq $plugin) 'Only current checksum-listed plugin archives are included'
    [IO.File]::AppendAllText($plugin,'tampered');Reject {Get-CapsulePluginArtifacts $plugins} 'Mismatched plugin archive refuses packaging'
    $linkedPlugins=Join-Path $root 'linked-plugin-artifacts';New-Item -ItemType Junction -Path $linkedPlugins -Target $plugins | Out-Null
    Reject {Get-CapsulePluginArtifacts $linkedPlugins} 'Linked plugin artifact directory refuses packaging'
    $results.ToArray() | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding UTF8
    Write-Output ('Bundle evidence: '+$root)
} finally {Remove-Variable CapsuleBundleFault -Scope Global -ErrorAction SilentlyContinue}
