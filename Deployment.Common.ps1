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
function Get-CapsuleRegistration([string]$TestRegistrationId) {
    if($TestRegistrationId -and $TestRegistrationId -notmatch '^[a-f0-9]{32}$'){throw '测试登记身份无效。'}
    $name=if($TestRegistrationId){'LingdongCapsule.Test.'+$TestRegistrationId}else{'LingdongCapsule'}
    $relative='Software\Microsoft\Windows\CurrentVersion\Uninstall\'+$name
    $linkName=if($TestRegistrationId){$name+'.lnk'}else{'灵动胶囊.lnk'}
    return @{Name=$name;Relative=$relative;Key=('HKCU:\'+$relative);Run='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run';Link=(Join-Path ([Environment]::GetFolderPath('Programs')) $linkName)}
}
function Test-CapsuleStartup([string]$Command,[string]$Exe) {
    $match=[regex]::Match($Command,'^\s*(?:"([^"]+)"|(\S+))(?:\s|$)')
    if(-not $match.Success){return $false}
    $first=if($match.Groups[1].Success){$match.Groups[1].Value}else{$match.Groups[2].Value}
    return $first.Equals($Exe,[StringComparison]::OrdinalIgnoreCase)
}
function Stop-CapsuleExecutable([string]$Exe) {
    $expected=Assert-CapsulePath $Exe
    $self=[Diagnostics.Process]::GetCurrentProcess();try{$session=$self.SessionId}finally{$self.Dispose()}
    foreach($owned in @(Get-Process LingdongCapsule -ErrorAction SilentlyContinue)){
        try {
            if($owned.HasExited -or $owned.Path -ne $expected){continue}
            if($owned.SessionId -ne $session){throw '其他登录会话仍在运行此安装目录，请先手动退出；不会自动结束其他会话。'}
            $closed=$false;try{$closed=$owned.CloseMainWindow()}catch [InvalidOperationException] {if(-not $owned.HasExited){throw}}
            if($closed -and $owned.WaitForExit(3000)){continue}
            if(-not $owned.HasExited){try{$owned.Kill()}catch [InvalidOperationException] {if(-not $owned.HasExited){throw}}}
            if(-not $owned.WaitForExit(10000)){throw '目标程序尚未完成退出，保留原安装目录，请稍后重试。'}
        } finally {$owned.Dispose()}
    }
}
function Get-CapsuleChild([string]$Root,[string]$Relative) {
    if([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or ($Relative -split '[\\/]') -contains '..'){throw '安装清单含非法相对路径。'}
    $full=[IO.Path]::GetFullPath((Join-Path $Root $Relative))
    if(-not $full.StartsWith($Root+'\',[StringComparison]::OrdinalIgnoreCase)){throw '安装清单含越界路径。'}
    return (Assert-CapsulePath $full)
}
function Write-CapsuleProgress([bool]$Enabled,[string]$Stage,[int]$Completed=0,[int]$Total=0) {
    if($Enabled){Write-Output ('CAPSULE_PROGRESS|'+$Stage+'|'+$Completed+'|'+$Total)}
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
