param([Parameter(Mandatory=$true)][string]$Root,[Parameter(Mandatory=$true)][int]$WaitPid,[Parameter(Mandatory=$true)][long]$StartedTicks)
$ErrorActionPreference='Stop'
$full=[IO.Path]::GetFullPath($Root).TrimEnd('\')
$cache=[IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'LingdongCapsule-maintenance')).TrimEnd('\')
if([IO.Path]::GetDirectoryName($full) -ne $cache -or (Split-Path $full -Leaf) -notmatch '^[a-f0-9]{32}$'){throw 'Unowned maintenance directory refused.'}
for($cursor=$full;$cursor;$cursor=Split-Path $cursor -Parent){if((Test-Path -LiteralPath $cursor) -and (Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked maintenance path refused.'}}
$marker=Join-Path $full '.capsule-maintenance.json'
if(-not(Test-Path -LiteralPath $marker) -or (Get-Item -LiteralPath $marker).Length -gt 16384){throw 'Missing maintenance identity.'}
$identity=[IO.File]::ReadAllText($marker) | ConvertFrom-Json
if($identity.Id -ne (Split-Path $full -Leaf)){throw 'Maintenance identity mismatch.'}
trap {[IO.File]::WriteAllText((Join-Path $full 'cleanup-error.log'),$_.Exception.ToString());exit 1}
[Environment]::CurrentDirectory=$cache
Set-Location -LiteralPath $cache
$owner=Get-Process -Id $WaitPid -ErrorAction SilentlyContinue
if($owner){if($owner.Path -ne (Join-Path $full 'LingdongCapsule.exe') -or $owner.StartTime.ToUniversalTime().Ticks -ne $StartedTicks){throw 'Different process identity; no cleanup.'};if(-not $owner.WaitForExit(600000)){throw 'Maintenance process remains active; no cleanup.'}}
$pending=[Collections.Generic.Stack[string]]::new();$pending.Push($full)
while($pending.Count){foreach($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force){if($entry.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked maintenance content refused.'};if($entry.PSIsContainer){$pending.Push($entry.FullName)}}}
# The absolute, identity-checked GUID directory is the only recursive target.
Remove-Item -LiteralPath $full -Recurse -Force
