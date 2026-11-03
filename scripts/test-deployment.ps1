param([string]$OutputRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(-not $OutputRoot){$OutputRoot=Join-Path $repo ('artifacts\deployment-tests\'+[Guid]::NewGuid().ToString('N'))}
$root=[IO.Path]::GetFullPath($OutputRoot);New-Item -ItemType Directory -Path $root -Force | Out-Null
$package=Join-Path $root 'package';$target=Join-Path $root '安装目录';New-Item -ItemType Directory -Path $package -Force | Out-Null
foreach($name in @('Install.ps1','Uninstall.ps1','Deployment.Common.ps1')){[IO.File]::WriteAllText((Join-Path $package $name),[IO.File]::ReadAllText((Join-Path $repo $name)),[Text.UTF8Encoding]::new($true))}
[IO.File]::WriteAllText((Join-Path $package 'LingdongCapsule.exe'),'owned fake binary v1')
[IO.File]::WriteAllText((Join-Path $package 'obsolete.dll'),'old library')
$results=[Collections.Generic.List[string]]::new()
function Check([bool]$Value,[string]$Label){if(-not $Value){throw $Label};$results.Add($Label);Write-Host ('PASS '+$Label)}
function Reject([scriptblock]$Action,[string]$Label){$rejected=$false;try{& $Action}catch{$rejected=$true};Check $rejected $Label}
function Install {& (Join-Path $package 'Install.ps1') -InstallDir $target -NoLaunch -NoRegistration}
Install
Check (([IO.File]::ReadAllText((Join-Path $target 'LingdongCapsule.exe'))) -eq 'owned fake binary v1') 'First install commits complete file set'
[IO.File]::WriteAllText((Join-Path $target 'user-note.txt'),'keep user file')
[IO.File]::WriteAllText((Join-Path $package 'LingdongCapsule.exe'),'owned fake binary v2')
Remove-Item -LiteralPath (Join-Path $package 'obsolete.dll')
Install
Check (([IO.File]::ReadAllText((Join-Path $target 'LingdongCapsule.exe'))) -eq 'owned fake binary v2' -and -not(Test-Path -LiteralPath (Join-Path $target 'obsolete.dll')) -and (Test-Path -LiteralPath (Join-Path $target 'user-note.txt'))) 'Upgrade removes obsolete owned files and preserves untracked user files'
$previous=@(Get-ChildItem -LiteralPath $root -Directory | Where-Object {$_.Name.StartsWith('安装目录.previous-')})
Check ($previous.Count -eq 1 -and [IO.File]::ReadAllText((Join-Path $previous[0].FullName 'LingdongCapsule.exe')) -eq 'owned fake binary v1') 'Prior installation remains recoverable'
$locked=Join-Path $package 'locked.dll';[IO.File]::WriteAllText($locked,'owned locked file');$handle=[IO.File]::Open($locked,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try{Reject {Install} 'Copy failure rejects upgrade before replacing previous version'}finally{$handle.Dispose();Remove-Item -LiteralPath $locked}
Check ([IO.File]::ReadAllText((Join-Path $target 'LingdongCapsule.exe')) -eq 'owned fake binary v2' -and @(Get-ChildItem -LiteralPath $root -Directory | Where-Object {$_.Name.Contains('.staging-')}).Count -eq 0) 'Copy failure preserves current binary and cleans staging'
[IO.File]::WriteAllText((Join-Path $package 'user-note.txt'),'would replace user file')
try{Reject {Install} 'New release cannot overwrite an untracked user file'}finally{Remove-Item -LiteralPath (Join-Path $package 'user-note.txt')}
Check ([IO.File]::ReadAllText((Join-Path $target 'user-note.txt')) -eq 'keep user file') 'Conflicting user file remains unchanged'
$candidate=Join-Path $root 'owned-candidate-restore'
foreach($path in @($target,$candidate,$previous[0].FullName)){if(-not ([IO.Path]::GetFullPath($path)).StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Rollback test escaped evidence root.'}}
Move-Item -LiteralPath $target -Destination $candidate
try {
    Move-Item -LiteralPath $previous[0].FullName -Destination $target
    Check ([IO.File]::ReadAllText((Join-Path $target 'LingdongCapsule.exe')) -eq 'owned fake binary v1' -and (Test-Path -LiteralPath (Join-Path $target 'obsolete.dll')) -and [IO.File]::ReadAllText((Join-Path $target 'user-note.txt')) -eq 'keep user file') 'Restoring prior directory recovers old binary, libraries and untracked files'
} finally {
    if(Test-Path -LiteralPath $target){Move-Item -LiteralPath $target -Destination $previous[0].FullName}
    Move-Item -LiteralPath $candidate -Destination $target
}
Reject {& (Join-Path $package 'Install.ps1') -InstallDir (Join-Path $package 'nested') -NoLaunch -NoRegistration} 'Nested source/target installation is rejected'
$manifest=Join-Path $target 'installed-files.json';$saved=[IO.File]::ReadAllText($manifest)
[IO.File]::WriteAllText($manifest,'["LingdongCapsule.exe","..\\outside.txt"]')
Reject {& (Join-Path $target 'Uninstall.ps1')} 'Traversal manifest rejected before uninstall mutations'
[IO.File]::WriteAllText($manifest,$saved)
$outside=Join-Path $root 'owned-outside';New-Item -ItemType Directory -Path $outside | Out-Null;[IO.File]::WriteAllText((Join-Path $outside 'sentinel.txt'),'do not delete')
$junction=Join-Path $target 'linked';New-Item -ItemType Junction -Path $junction -Value $outside | Out-Null
try{Reject {& (Join-Path $target 'Uninstall.ps1')} 'Directory junction rejected before uninstall mutations';Check (Test-Path -LiteralPath (Join-Path $outside 'sentinel.txt')) 'Junction target remains untouched'}finally{if((Get-Item -LiteralPath $junction -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){[IO.Directory]::Delete($junction,$false)}}
& (Join-Path $target 'Uninstall.ps1')
Check ((Test-Path -LiteralPath (Join-Path $target 'user-note.txt')) -and -not(Test-Path -LiteralPath (Join-Path $target 'LingdongCapsule.exe'))) 'Uninstall removes owned files and preserves user files'
$results.ToArray() | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding utf8
Write-Output ('Deployment evidence: '+$root)
