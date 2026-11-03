function Copy-CapsuleDependencyNotices([string]$Stage,[string]$RepositoryRoot,[string]$AssetsFile,[string]$DotnetRoot) {
    $Stage=[IO.Path]::GetFullPath($Stage);$RepositoryRoot=[IO.Path]::GetFullPath($RepositoryRoot)
    $assets=Get-Content -LiteralPath $AssetsFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $folders=@($assets.packageFolders.PSObject.Properties.Name)
    $source=Join-Path $RepositoryRoot 'licenses';$destination=Join-Path $Stage 'licenses'
    if(Test-Path -LiteralPath $destination){throw 'Dependency notices require a fresh publish directory.'}
    Copy-Item -LiteralPath $source -Destination $destination -Recurse
    $components=[Collections.Generic.List[object]]::new()
    function PackageFile([string]$id,[string]$version,[string]$name) {
        foreach($folder in $folders){$path=Join-Path $folder ($id.ToLowerInvariant()+'/'+$version.ToLowerInvariant()+'/'+$name);if(Test-Path -LiteralPath $path -PathType Leaf){return $path}}
        throw ('Dependency notice source missing: '+$id+'/'+$version+'/'+$name)
    }
    $known=@{'naudio.core'='NAudio-LICENSE.txt';'naudio.wasapi'='NAudio-LICENSE.txt';'modelsensingprotocol'='ModelSensingProtocol-NOTICE.txt'}
    foreach($entry in $assets.libraries.PSObject.Properties | Where-Object {$_.Value.type -eq 'package'}) {
        $parts=$entry.Name.Split('/');$id=$parts[0];$version=$parts[1]
        if(-not $known.ContainsKey($id)){throw ('Unreviewed distributed package: '+$entry.Name)}
        $metadataName=$id.ToLowerInvariant()+'.nuspec';$metadataPath=Join-Path $destination ('package-metadata/'+$metadataName)
        [xml]$metadata=Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8
        if($metadata.package.metadata.id -ne $id -or $metadata.package.metadata.version -ne $version -or $metadata.package.metadata.license.'#text' -ne 'MIT'){throw ('Dependency metadata needs review: '+$entry.Name)}
        $components.Add([ordered]@{name=$id;version=$version;declaredLicense='MIT';notices=@($known[$id],('package-metadata/'+$metadataName));source=('https://www.nuget.org/packages/'+$id+'/'+$version)})
    }
    $config=Get-Content -LiteralPath (Join-Path $Stage 'LingdongCapsule.runtimeconfig.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach($framework in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')) {
        $runtime=@($config.runtimeOptions.includedFrameworks | Where-Object {$_.name -eq $framework})
        if($runtime.Count -ne 1 -or $runtime[0].version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?$'){throw ('Cannot identify self-contained runtime: '+$framework)}
        $version=$runtime[0].version;$id=$framework+'.Runtime.win-x64';$licenseName=if($framework -eq 'Microsoft.NETCore.App'){'LICENSE.TXT'}else{'LICENSE'}
        Copy-Item -LiteralPath (PackageFile $id $version $licenseName) -Destination (Join-Path $destination ($framework+'-LICENSE.txt'))
        $notices=@($framework+'-LICENSE.txt')
        if($framework -eq 'Microsoft.NETCore.App'){$name=$framework+'-THIRD-PARTY-NOTICES.txt';Copy-Item -LiteralPath (PackageFile $id $version 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $destination $name);$notices+=$name}
        $components.Add([ordered]@{name=$framework;version=$version;declaredLicense='MIT with Windows native exceptions described in SOURCES.md';notices=$notices;source=('https://www.nuget.org/packages/'+$id+'/'+$version)})
    }
    $downloads=@($assets.project.frameworks.PSObject.Properties.Value.downloadDependencies | Where-Object {$_.name -eq 'Microsoft.Windows.SDK.NET.Ref'})
    if($downloads.Count -ne 1 -or $downloads[0].version -notmatch '^\[([0-9.]+),\s*\1\]$'){throw 'Cannot identify Windows SDK targeting package.'}
    $sdkVersion=$Matches[1];[xml]$sdkMetadata=Get-Content -LiteralPath (Join-Path $destination 'package-metadata/microsoft.windows.sdk.net.ref.nuspec') -Raw -Encoding UTF8
    if($sdkMetadata.package.metadata.version -ne $sdkVersion){throw 'Windows SDK metadata needs license review.'}
    $components.Add([ordered]@{name='Microsoft.Windows.SDK.NET.Ref';version=$sdkVersion;declaredLicense='Microsoft Windows SDK License';notices=@('Windows-SDK-Targeting-LICENSE.rtf','package-metadata/microsoft.windows.sdk.net.ref.nuspec');source='https://aka.ms/WinSDKLicenseURL'})
    $windowsLicense=Join-Path $DotnetRoot 'LICENSE.txt'
    if(-not(Test-Path -LiteralPath $windowsLicense) -or -not([IO.File]::ReadAllText($windowsLicense).Contains('MICROSOFT .NET LIBRARY'))){throw 'Windows .NET Library license is missing.'}
    Copy-Item -LiteralPath $windowsLicense -Destination (Join-Path $destination 'NET-Windows-Library-LICENSE.txt')
    $components.Add([ordered]@{name='Windows native runtime components';version='see runtime components above';declaredLicense='Microsoft .NET Library License; Microsoft Windows SDK License for D3DCompiler_47_cor3.dll';notices=@('NET-Windows-Library-LICENSE.txt','Windows-SDK-LICENSE.txt');source='https://github.com/dotnet/core/blob/main/license-information-windows.md'})
    $files=@(Get-ChildItem -LiteralPath $destination -Recurse -File | Sort-Object FullName | ForEach-Object {[ordered]@{path=$_.FullName.Substring($destination.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
    $manifest=[ordered]@{schema=1;components=$components.ToArray();files=$files}
    [IO.File]::WriteAllText((Join-Path $destination 'components.json'),($manifest | ConvertTo-Json -Depth 7),[Text.UTF8Encoding]::new($false))
}
