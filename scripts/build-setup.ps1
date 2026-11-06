param([string]$Dotnet='dotnet',[string]$Archive,[switch]$RequireClean)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if($Dotnet -eq 'dotnet' -and (Test-Path -LiteralPath (Join-Path $repo '.tools/dotnet/dotnet.exe'))){$Dotnet=Join-Path $repo '.tools/dotnet/dotnet.exe'}
if(-not $Archive){$Archive=Join-Path $repo 'artifacts/LingdongCapsule-win-x64.zip'}
$Archive=[IO.Path]::GetFullPath($Archive)
if(-not(Test-Path -LiteralPath $Archive)){throw 'Build the main ZIP before setup.'}
$hash=(Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash
$revision=(& git -C $repo rev-parse HEAD).Trim();if($LASTEXITCODE -ne 0){throw 'Cannot identify setup source.'}
$dirty=-not [string]::IsNullOrWhiteSpace((& git -C $repo status --porcelain --untracked-files=normal | Out-String))
if($RequireClean -and $dirty){throw 'Standalone setup requires clean source.'}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($Archive)
try{
    $entry=$zip.GetEntry('BUILD-INFO.json');if(-not $entry){throw 'Payload has no provenance.'}
    $reader=[IO.StreamReader]::new($entry.Open());try{$info=$reader.ReadToEnd() | ConvertFrom-Json}finally{$reader.Dispose()}
}finally{$zip.Dispose()}
[xml]$project=Get-Content -LiteralPath (Join-Path $repo 'src/Capsule.Setup/Capsule.Setup.csproj') -Raw
$version=[string]$project.Project.PropertyGroup.Version
if($info.sourceCommit -ne $revision -or $info.rid -ne 'win-x64' -or $info.selfContained -ne $true -or ($info.version -split '\+')[0] -ne $version -or ($RequireClean -and $info.sourceDirty)){throw 'Payload source, version or clean provenance does not match setup.'}
$stage=Join-Path $repo ('artifacts/setup-publish-'+[guid]::NewGuid().ToString('N'))
& $Dotnet publish (Join-Path $repo 'src/Capsule.Setup') -c Release -r win-x64 --self-contained true -p:PayloadArchive=$Archive -p:PayloadSHA256=$hash -o $stage
if($LASTEXITCODE -ne 0){throw 'Standalone setup publish failed.'}
$endRevision=(& git -C $repo rev-parse HEAD).Trim()
$endDirty=-not [string]::IsNullOrWhiteSpace((& git -C $repo status --porcelain --untracked-files=normal | Out-String))
if($endRevision -ne $revision -or ($RequireClean -and $endDirty) -or (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash -ne $hash){throw 'Source or embedded payload changed during setup build.'}
$setup=Join-Path $repo 'artifacts/LingdongCapsule-Setup-win-x64.exe'
Copy-Item -LiteralPath (Join-Path $stage 'LingdongCapsule.Setup.exe') -Destination $setup -Force
$sum=(Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
[IO.File]::WriteAllText((Join-Path $repo 'artifacts/SETUP-SHA256SUMS.txt'),($sum+'  LingdongCapsule-Setup-win-x64.exe'+[Environment]::NewLine),[Text.UTF8Encoding]::new($false))
$metadata=[ordered]@{schema=1;version=$version;sourceCommit=$revision;sourceDirty=[bool]($dirty -or $endDirty -or $info.sourceDirty);payloadSha256=$hash;setupSha256=$sum;rid='win-x64';selfContained=$true}
[IO.File]::WriteAllText((Join-Path $repo 'artifacts/SETUP-BUILD-INFO.json'),($metadata | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output ('Standalone setup: '+$setup+'; embedded ZIP SHA256 '+$hash)
