function Assert-CapsulePath([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path).TrimEnd('\')
    if($full -eq [IO.Path]::GetPathRoot($full).TrimEnd('\')){throw '不能将磁盘根目录作为程序目录。'}
    $cursor=$full
    while($cursor){
        if(Test-Path -LiteralPath $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw '程序路径不能经过符号链接或目录联接。'}}
        $parent=Split-Path $cursor -Parent;if($parent -eq $cursor){break};$cursor=$parent
    }
    return $full
}
function Get-CapsuleChild([string]$Root,[string]$Relative) {
    if([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or ($Relative -split '[\\/]') -contains '..'){throw '安装清单含非法相对路径。'}
    $full=[IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if(-not $full.StartsWith($Root+'\',[StringComparison]::OrdinalIgnoreCase)){throw '安装清单含越界路径。'}
    return (Assert-CapsulePath $full)
}
function Read-CapsuleFiles([string]$Root) {
    $manifest=Join-Path $Root 'installed-files.json'
    if(-not(Test-Path -LiteralPath $manifest) -or (Get-Item -LiteralPath $manifest).Length -gt 4194304){throw '没有可用的安装清单。'}
    $parsed=ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($manifest))
    $items=@($parsed);if($items.Count -eq 0 -or $items.Count -gt 50000){throw '安装清单大小无效。'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($item in $items){
        if($item -isnot [string] -or $item -eq 'installed-files.json' -or -not $seen.Add($item)){throw '安装清单含无效或重复项。'}
        $path=Get-CapsuleChild $Root $item
        if((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path -Force).PSIsContainer){throw '安装清单不能把目录作为文件。'}
    }
    if(-not $seen.Contains('LingdongCapsule.exe')){throw '安装清单不属于灵动胶囊。'}
    return $items
}
function Assert-CapsuleTree([string]$Root) {
    $null=Assert-CapsulePath $Root
    $pending=[Collections.Generic.Stack[string]]::new();$pending.Push($Root)
    while($pending.Count -gt 0){foreach($entry in Get-ChildItem -LiteralPath $pending.Pop() -Force){$null=Assert-CapsulePath $entry.FullName;if($entry.PSIsContainer){$pending.Push($entry.FullName)}}}
}
function Remove-CapsuleStage([string]$Path,[string]$Expected) {
    $full=Assert-CapsulePath $Path
    if($full -ne $Expected){throw '临时目录范围不匹配。'}
    if(Test-Path -LiteralPath $full){Assert-CapsuleTree $full;Remove-Item -LiteralPath $full -Recurse -Force}
}
