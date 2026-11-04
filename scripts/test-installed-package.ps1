param([Parameter(Mandatory=$true)][string]$OldPackageDirectory,[Parameter(Mandatory=$true)][string]$NewPackageDirectory,[int]$ProtectedProcessId=0)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
. (Join-Path $repo 'Deployment.Common.ps1')
function Read-Package([string]$Directory){
    $path=Assert-CapsulePath $Directory;Assert-CapsuleTree $path
    $info=Get-Content -LiteralPath (Join-Path $path 'BUILD-INFO.json') -Raw | ConvertFrom-Json
    if($info.sourceDirty -isnot [bool] -or $info.sourceDirty -or (Get-FileHash -LiteralPath (Join-Path $path 'LingdongCapsule.dll') -Algorithm SHA256).Hash -ne $info.appSha256){throw 'Package provenance refused.'}
    return [pscustomobject]@{Path=$path;Info=$info}
}
$old=Read-Package $OldPackageDirectory;$new=Read-Package $NewPackageDirectory
$root=Assert-CapsulePath (Join-Path $repo ('artifacts\installed-package-tests\'+[Guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Path $root | Out-Null
$target=Join-Path $root 'installed';$id=[Guid]::NewGuid().ToString('N');$scope=Get-CapsuleRegistration $id
if((Test-Path -LiteralPath $scope.Key) -or (Test-Path -LiteralPath $scope.Link)){throw 'Own registration identity already exists.'}
$protected=$null;if($ProtectedProcessId){$protected=Get-Process -Id $ProtectedProcessId;$protectedPath=$protected.Path;$protectedStart=$protected.StartTime.ToUniversalTime()}
$profileFile=Join-Path $env:LOCALAPPDATA 'LingdongCapsule\settings.json';$profileBefore=if(Test-Path -LiteralPath $profileFile){(Get-FileHash -LiteralPath $profileFile).Hash}else{''}
$results=[Collections.Generic.List[string]]::new()
function Check([bool]$Passed,[string]$Message){if(-not $Passed){throw $Message};$results.Add($Message);Write-Host ('PASS '+$Message)}
function Guard-Protected {
    if($ProtectedProcessId){$live=Get-Process -Id $ProtectedProcessId -ErrorAction Stop;if($live.Path -ne $protectedPath -or $live.StartTime.ToUniversalTime() -ne $protectedStart){throw 'Protected candidate identity changed.'}}
}
function Own-Path([string]$Path){$full=Assert-CapsulePath $Path;if(-not $full.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Lifecycle path escaped owned root.'};return $full}
function Install-Package($Package){Guard-Protected;& (Join-Path $Package.Path 'Install.ps1') -InstallDir $target -NoLaunch -TestRegistrationId $id;Guard-Protected}
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CapsuleInstalledLifecycleNative {
    private delegate bool Callback(IntPtr window,IntPtr unused);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback,IntPtr unused);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window,uint message,IntPtr wp,IntPtr lp);
    public static IntPtr Find(uint pid) { IntPtr found=IntPtr.Zero;EnumWindows((w,u)=>{uint owner;GetWindowThreadProcessId(w,out owner);if(owner==pid){var text=new StringBuilder(128);GetWindowText(w,text,128);if(text.ToString()=="安装灵动胶囊")found=w;}return true;},IntPtr.Zero);return found; }
}
'@
function Start-Probe {
    $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $target 'LingdongCapsule.exe'),'--install');$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $start.EnvironmentVariables['DOTNET_ROOT']=Join-Path $root 'no-shared-dotnet';$start.EnvironmentVariables['DOTNET_ROOT_X64']=$start.EnvironmentVariables['DOTNET_ROOT'];$start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP']='0'
    $child=[Diagnostics.Process]::Start($start)
    try {
        $child.WaitForInputIdle(10000) | Out-Null;$watch=[Diagnostics.Stopwatch]::StartNew();$window=[IntPtr]::Zero;$runtime=$false
        while($watch.Elapsed.TotalSeconds -lt 30 -and -not $child.HasExited){$child.Refresh();$window=[CapsuleInstalledLifecycleNative]::Find([uint32]$child.Id);foreach($module in $child.Modules){if($module.ModuleName -eq 'coreclr.dll'){$runtime=[IO.Path]::GetDirectoryName($module.FileName) -eq $target}};if($runtime -and $window -ne [IntPtr]::Zero){return [pscustomobject]@{Process=$child;Window=$window}};Start-Sleep -Milliseconds 100}
        throw 'Installed executable did not initialize installer from its own runtime.'
    } catch {if(-not $child.HasExited){$child.Kill();$child.WaitForExit()};$child.Dispose();throw}
}
function Close-Probe($Probe){[CapsuleInstalledLifecycleNative]::PostMessage($Probe.Window,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null;if(-not $Probe.Process.WaitForExit(5000) -or $Probe.Process.ExitCode -ne 0){throw 'Own installed window did not close gracefully.'};$Probe.Process.Dispose()}
$probe=$null
try {
    Install-Package $old
    Check ((Get-FileHash -LiteralPath (Join-Path $target 'LingdongCapsule.dll')).Hash -eq $old.Info.appSha256) 'Real old package files installed with matching app hash'
    $entry=Get-ItemProperty -LiteralPath $scope.Key;$shell=New-Object -ComObject WScript.Shell
    Check ($entry.InstallLocation -eq $target -and $shell.CreateShortcut($scope.Link).TargetPath -eq (Join-Path $target 'LingdongCapsule.exe')) 'Real per-user test registration and shortcut target installed executable'
    $marker=Join-Path $target 'owned-user-file.txt';[IO.File]::WriteAllText($marker,'Own user file must survive upgrade, rollback and uninstall.');$markerHash=(Get-FileHash -LiteralPath $marker).Hash
    $probe=Start-Probe
    Install-Package $new
    Check ($probe.Process.WaitForExit(5000)) 'Upgrade stopped only its own installed test executable'
    $probe.Process.Dispose();$probe=$null
    Check ((Get-FileHash -LiteralPath (Join-Path $target 'LingdongCapsule.dll')).Hash -eq $new.Info.appSha256 -and (Get-FileHash -LiteralPath $marker).Hash -eq $markerHash) 'Real newer binary replaced old one and preserved unowned user file'
    $backups=@(Get-ChildItem -LiteralPath $root -Directory | Where-Object {$_.Name -like 'installed.previous-*'})
    Check ($backups.Count -eq 1 -and (Get-FileHash -LiteralPath (Join-Path $backups[0].FullName 'LingdongCapsule.dll')).Hash -eq $old.Info.appSha256) 'Upgrade retains complete matching old package for rollback'
    $from=Own-Path $target;$held=Own-Path (Join-Path $root 'newer-before-rollback');$backup=Own-Path $backups[0].FullName
    Move-Item -LiteralPath $from -Destination $held;Move-Item -LiteralPath $backup -Destination $from
    Check ((Get-FileHash -LiteralPath (Join-Path $target 'LingdongCapsule.dll')).Hash -eq $old.Info.appSha256 -and (Get-ItemProperty -LiteralPath $scope.Key).InstallLocation -eq $target) 'Real directory rollback restores old binary at registered path'
    $probe=Start-Probe;Close-Probe $probe;$probe=$null
    Install-Package $new;$probe=Start-Probe;Close-Probe $probe;$probe=$null
    Check ((Get-FileHash -LiteralPath $marker).Hash -eq $markerHash) 'Rollback and forward install retain own user data and initialize local runtime'
    Guard-Protected;& (Join-Path $target 'Uninstall.ps1') -TestRegistrationId $id;Guard-Protected
    Check (-not(Test-Path -LiteralPath $scope.Key) -and -not(Test-Path -LiteralPath $scope.Link) -and -not(Test-Path -LiteralPath (Join-Path $target 'LingdongCapsule.exe')) -and (Get-FileHash -LiteralPath $marker).Hash -eq $markerHash) 'Real uninstall removes owned files/registration and preserves unowned user file'
    $profileAfter=if(Test-Path -LiteralPath $profileFile){(Get-FileHash -LiteralPath $profileFile).Hash}else{''}
    [ordered]@{passed=$true;oldCommit=$old.Info.sourceCommit;newCommit=$new.Info.sourceCommit;protectedCandidateUnchanged=$true;dailySettingsUnchanged=($profileBefore -eq $profileAfter);checks=$results.ToArray();scope='Real installed self-contained app binaries, own GUID registration/shortcut and test directory. Only installer windows launched; no daily main profile, media, login or power test was injected.'} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'result.json') -Encoding UTF8
    Write-Output ('PASS real installed package lifecycle; evidence: '+$root)
} finally {
    if($probe){if(-not $probe.Process.HasExited){[CapsuleInstalledLifecycleNative]::PostMessage($probe.Window,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null;if(-not $probe.Process.WaitForExit(5000)){$probe.Process.Kill();$probe.Process.WaitForExit()}};$probe.Process.Dispose()}
    # Keep failed files as evidence, but do not leave test entries in daily menus.
    try{
        if((Get-ItemProperty -LiteralPath $scope.Key -ErrorAction SilentlyContinue).InstallLocation -eq $target){Remove-Item -LiteralPath $scope.Key}
        if(Test-Path -LiteralPath $scope.Link){$shell=New-Object -ComObject WScript.Shell;if($shell.CreateShortcut($scope.Link).TargetPath -eq (Join-Path $target 'LingdongCapsule.exe')){Remove-Item -LiteralPath $scope.Link}}
    }catch{Write-Warning ('Own test registration cleanup failed: '+$_.Exception.GetType().Name)}
}
