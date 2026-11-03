function New-CapsulePublishStage([string]$OutputRoot) {
    $root=[IO.Path]::GetFullPath($OutputRoot).TrimEnd('\','/')
    if($root -eq [IO.Path]::GetPathRoot($root).TrimEnd('\','/')){throw 'Build output cannot be a drive root.'}
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    if((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Build output cannot be a link.'}
    $stage=Join-Path $root ('publish-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    return $stage
}

function Get-CapsulePluginArtifacts([string]$Folder) {
    $root=[IO.Path]::GetFullPath($Folder);$manifest=Join-Path $root 'SHA256SUMS.txt'
    if((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Plugin artifact directory cannot be a link.'}
    if(-not(Test-Path -LiteralPath $manifest -PathType Leaf) -or (Get-Item -LiteralPath $manifest).Length -gt 65536){throw 'Invalid plugin artifact manifest.'}
    if((Get-Item -LiteralPath $manifest).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Plugin artifact manifest cannot be a link.'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$files=[Collections.Generic.List[string]]::new()
    foreach($line in [IO.File]::ReadAllLines($manifest)){
        if($line -notmatch '^([A-Fa-f0-9]{64})  ([a-z][a-z0-9.-]{2,79}-[0-9]+\.[0-9]+(?:\.[0-9]+){0,2}\.zip)$'){throw 'Invalid plugin artifact entry.'}
        $hash=$Matches[1];$name=$Matches[2];$path=Join-Path $root $name
        if(-not $seen.Add($name) -or -not(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $hash){throw 'Plugin artifact does not match its manifest.'}
        $files.Add($path)
    }
    if($files.Count -eq 0){throw 'Plugin artifact manifest is empty.'}
    return $files.ToArray()
}

function Publish-CapsuleBundle([string]$Stage,[string]$OutputRoot) {
    $root=[IO.Path]::GetFullPath($OutputRoot).TrimEnd('\','/');$stage=[IO.Path]::GetFullPath($Stage)
    if($root -eq [IO.Path]::GetPathRoot($root).TrimEnd('\','/') -or [IO.Path]::GetDirectoryName($stage) -ne $root -or [IO.Path]::GetFileName($stage) -notmatch '^publish-[a-f0-9]{32}$'){throw 'Publish stage must be an owned direct child of the build output.'}
    foreach($path in @($root,$stage)){if(-not(Test-Path -LiteralPath $path -PathType Container) -or (Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Invalid or linked publish directory.'}}
    $directories=[Collections.Generic.Stack[string]]::new();$directories.Push($stage);$files=[Collections.Generic.List[IO.FileInfo]]::new()
    while($directories.Count -gt 0){foreach($item in Get-ChildItem -LiteralPath $directories.Pop() -Force){if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Publish tree cannot contain links.'};if($item.PSIsContainer){$directories.Push($item.FullName)}else{$files.Add($item)}}}
    foreach($name in @('LingdongCapsule.exe','LingdongCapsule.dll','LingdongCapsule.deps.json','LingdongCapsule.runtimeconfig.json','coreclr.dll','hostfxr.dll','hostpolicy.dll','PresentationFramework.dll','Install.ps1','Uninstall.ps1','Deployment.Common.ps1','README.md','LICENSE','THIRD-PARTY-NOTICES.md','BUILD-INFO.json','licenses/components.json')){
        if(-not(Test-Path -LiteralPath (Join-Path $stage $name) -PathType Leaf)){throw ('Missing bundle file: '+$name)}
    }
    $infoPath=Join-Path $stage 'BUILD-INFO.json'
    if((Get-Item -LiteralPath $infoPath).Length -gt 65536){throw 'Build information exceeds limit.'}
    $info=Get-Content -LiteralPath $infoPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if($info.schema -ne 1 -or $info.sourceCommit -isnot [string] -or $info.sourceCommit -notmatch '^[a-f0-9]{40}$' -or $info.sourceDirty -isnot [bool] -or $info.rid -isnot [string] -or $info.rid -ne 'win-x64' -or $info.selfContained -isnot [bool] -or $info.selfContained -ne $true -or $info.appSha256 -isnot [string] -or $info.appSha256 -ne (Get-FileHash -LiteralPath (Join-Path $stage 'LingdongCapsule.dll') -Algorithm SHA256).Hash){throw 'Build information does not match the staged application.'}
    $noticeRoot=Join-Path $stage 'licenses';$noticeManifest=Join-Path $noticeRoot 'components.json'
    if((Get-Item -LiteralPath $noticeManifest).Length -gt 65536 -or $info.noticesSha256 -isnot [string] -or $info.noticesSha256 -ne (Get-FileHash -LiteralPath $noticeManifest -Algorithm SHA256).Hash){throw 'Build information does not match dependency notices.'}
    $notices=Get-Content -LiteralPath $noticeManifest -Raw -Encoding UTF8 | ConvertFrom-Json
    if($notices.schema -ne 1 -or @($notices.components).Count -lt 1 -or @($notices.files).Count -lt 1 -or @($notices.files).Count -gt 128){throw 'Invalid dependency notice manifest.'}
    $noticeFiles=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($file in $notices.files){
        if($file.path -isnot [string] -or $file.sha256 -isnot [string] -or $file.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or [IO.Path]::IsPathRooted($file.path) -or $file.path.Contains(':') -or ($file.path -split '[\\/]') -contains '..' -or -not $noticeFiles.Add($file.path)){throw 'Invalid dependency notice path or digest.'}
        $path=[IO.Path]::GetFullPath((Join-Path $noticeRoot $file.path))
        if(-not $path.StartsWith($noticeRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256){throw 'Dependency notice bytes do not match the manifest.'}
    }
    foreach($component in $notices.components){foreach($notice in $component.notices){if(-not $noticeFiles.Contains($notice)){throw 'Dependency component references a missing notice.'}}}
    foreach($name in @('SOURCES.md','NAudio-LICENSE.txt','SPDX-MIT.txt','ModelSensingProtocol-NOTICE.txt','Microsoft.NETCore.App-LICENSE.txt','Microsoft.NETCore.App-THIRD-PARTY-NOTICES.txt','Microsoft.WindowsDesktop.App-LICENSE.txt','NET-Windows-Library-LICENSE.txt','Windows-SDK-LICENSE.txt','Windows-SDK-Targeting-LICENSE.rtf')){if(-not $noticeFiles.Contains($name)){throw ('Dependency notice missing: '+$name)}}
    $app=Join-Path $root 'app';$archive=Join-Path $root 'LingdongCapsule-win-x64.zip';$checksum=Join-Path $root 'SHA256SUMS.txt'
    foreach($path in @($app,$archive,$checksum)){if((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Published output cannot be a link.'}}
    foreach($process in Get-Process LingdongCapsule -ErrorAction SilentlyContinue){if($process.Path -eq (Join-Path $app 'LingdongCapsule.exe')){throw 'The generated application is running. Exit it before replacing build output.'}}
    $id=[Guid]::NewGuid().ToString('N');$zipStage=Join-Path $root ('bundle-'+$id+'.zip');$hashStage=Join-Path $root ('bundle-'+$id+'.sha256');$backup=Join-Path $root ('build-backup-'+$id)
    $outputs=@(@{Source=$stage;Target=$app;Name='app'},@{Source=$zipStage;Target=$archive;Name='LingdongCapsule-win-x64.zip'},@{Source=$hashStage;Target=$checksum;Name='SHA256SUMS.txt'})
    $saved=[Collections.Generic.List[object]]::new();$installed=[Collections.Generic.List[object]]::new();$success=$false;$recovered=$false
    try {
        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $stream=[IO.File]::Create($zipStage);$zip=$null
        try {
            $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
            foreach($file in $files | Sort-Object FullName){
                $name=$file.FullName.Substring($stage.Length+1).Replace('\','/')
                $entry=$zip.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
                $input=[IO.File]::OpenRead($file.FullName);$output=$null
                try{$output=$entry.Open();$input.CopyTo($output)}finally{if($output){$output.Dispose()};$input.Dispose()}
            }
        } finally {if($zip){$zip.Dispose()};$stream.Dispose()}
        $hash=(Get-FileHash -LiteralPath $zipStage -Algorithm SHA256).Hash
        [IO.File]::WriteAllText($hashStage,$hash+'  LingdongCapsule-win-x64.zip'+[Environment]::NewLine,[Text.Encoding]::ASCII)
        New-Item -ItemType Directory -Path $backup | Out-Null
        foreach($entry in $outputs){if(Test-Path -LiteralPath $entry.Target){Move-Item -LiteralPath $entry.Target -Destination (Join-Path $backup $entry.Name) -ErrorAction Stop;$saved.Add($entry)}}
        foreach($entry in $outputs){Move-Item -LiteralPath $entry.Source -Destination $entry.Target -ErrorAction Stop;$installed.Add($entry)}
        $success=$true
    } catch {
        $original=$_;$recovery=[Collections.Generic.List[string]]::new()
        for($index=$installed.Count-1;$index -ge 0;$index--){$entry=$installed[$index];try{Move-Item -LiteralPath $entry.Target -Destination $entry.Source -ErrorAction Stop}catch{$recovery.Add($_.Exception.Message)}}
        foreach($entry in $saved){try{Move-Item -LiteralPath (Join-Path $backup $entry.Name) -Destination $entry.Target -ErrorAction Stop}catch{$recovery.Add($_.Exception.Message)}}
        if($recovery.Count -gt 0){throw ('Build commit failed: '+$original.Exception.Message+'; recovery incomplete, retain '+$backup+': '+($recovery -join '; '))}
        $recovered=$true
        throw $original
    } finally {
        foreach($temporary in @($zipStage,$hashStage)){if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}}
        # Only this invocation's verified backup is removed; never enumerate old outputs for deletion.
        if(($success -or $recovered) -and (Test-Path -LiteralPath $backup)){
            $checked=[IO.Path]::GetFullPath($backup)
            if([IO.Path]::GetDirectoryName($checked) -ne $root -or [IO.Path]::GetFileName($checked) -ne ('build-backup-'+$id)){throw 'Backup cleanup escaped build output.'}
            try{Remove-Item -LiteralPath $checked -Recurse -Force -ErrorAction Stop}catch{Write-Warning ('Old generated output retained at '+$checked+': '+$_.Exception.Message)}
        }
    }
    return [pscustomobject]@{Archive=$archive;SHA256=$hash;Application=$app;SourceCommit=$info.sourceCommit;SourceDirty=$info.sourceDirty}
}
