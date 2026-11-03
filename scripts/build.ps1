param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if($Dotnet -eq 'dotnet' -and (Test-Path -LiteralPath (Join-Path $root '.tools\dotnet\dotnet.exe'))) {
    $Dotnet=Join-Path $root '.tools\dotnet\dotnet.exe'
}
Push-Location $root
try {
    & $Dotnet run --project tests/Capsule.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'State tests failed' }
    & $Dotnet publish src/Capsule.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false -o artifacts/app
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    foreach($name in 'Capsule.Core.pdb','LingdongCapsule.pdb') {
        $symbol=Join-Path $root ('artifacts/app/'+$name)
        if(Test-Path -LiteralPath $symbol){Remove-Item -LiteralPath $symbol}
    }
    Copy-Item README.md,LICENSE,THIRD-PARTY-NOTICES.md,Install.ps1,Uninstall.ps1,安装.cmd -Destination artifacts/app
    foreach($script in 'Install.ps1','Uninstall.ps1') {
        $path=Join-Path $root ('artifacts/app/'+$script)
        [IO.File]::WriteAllText($path,[IO.File]::ReadAllText($path),[Text.UTF8Encoding]::new($true))
    }
    New-Item -ItemType Directory -Path artifacts/app/docs -Force | Out-Null
    Copy-Item docs/* -Destination artifacts/app/docs -Recurse -Force
    Compress-Archive -Path artifacts/app/* -DestinationPath artifacts/LingdongCapsule-win-x64.zip -Force
    $hash = Get-FileHash artifacts/LingdongCapsule-win-x64.zip -Algorithm SHA256
    "$($hash.Hash)  LingdongCapsule-win-x64.zip" | Set-Content artifacts/SHA256SUMS.txt -Encoding ascii
    Write-Output $hash
} finally { Pop-Location }
