param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if($Dotnet -eq 'dotnet' -and (Test-Path -LiteralPath (Join-Path $root '.tools/dotnet/dotnet.exe'))){$Dotnet=Join-Path $root '.tools/dotnet/dotnet.exe'}
Push-Location $root
try {
    $folder=Join-Path $root 'artifacts/plugins'
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $hashes=[Collections.Generic.List[string]]::new()
    foreach($name in @('Capsule.CenterClock')) {
        $project=Join-Path $root ('plugins/'+$name)
        & $Dotnet build $project -c Release -p:DebugType=none -p:DebugSymbols=false
        if($LASTEXITCODE -ne 0){throw ('Plugin build failed: '+$name)}
        $metadata=Get-Content -LiteralPath (Join-Path $project 'plugin.json') -Raw | ConvertFrom-Json
        $stage=Join-Path $folder ($name+'-'+[Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $stage | Out-Null
        Copy-Item -LiteralPath (Join-Path $project ('bin/Release/net10.0-windows10.0.19041.0/'+$metadata.entry)),(Join-Path $project 'plugin.json'),(Join-Path $project 'README.md'),(Join-Path $root 'LICENSE') -Destination $stage
        $archive=Join-Path $folder ($metadata.id+'-'+$metadata.version+'.zip')
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
        $hash=Get-FileHash -LiteralPath $archive -Algorithm SHA256
        $hashes.Add($hash.Hash+'  '+[IO.Path]::GetFileName($archive))
        Write-Output $hash
        $resolvedStage=[IO.Path]::GetFullPath($stage)
        $stageBoundary=[IO.Path]::GetFullPath($folder).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
        if(!$resolvedStage.StartsWith($stageBoundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Plugin staging cleanup escaped artifact directory'}
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
    $hashes | Set-Content -LiteralPath (Join-Path $folder 'SHA256SUMS.txt') -Encoding ascii
} finally {Pop-Location}
