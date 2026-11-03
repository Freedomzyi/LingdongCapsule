param([string]$Archive,[string]$Checksum,[switch]$RequireClean,[switch]$MainStartup)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
if(-not $Archive){$Archive=Join-Path $repo 'artifacts\LingdongCapsule-win-x64.zip'}
$Archive=[IO.Path]::GetFullPath($Archive);if(-not $Checksum){$Checksum=Join-Path ([IO.Path]::GetDirectoryName($Archive)) 'SHA256SUMS.txt'}
if($MainStartup -and (Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'LingdongCapsule'))){throw 'Main startup smoke requires a fresh user profile; existing application data will not be used.'}
$sum=[IO.File]::ReadAllText($Checksum)
if($sum -notmatch '^([A-Fa-f0-9]{64})  LingdongCapsule-win-x64\.zip\s*$' -or (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash -ne $Matches[1]){throw 'Published archive checksum mismatch.'}
$archiveHash=(Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash
$root=Join-Path $repo ('artifacts\published-tests\'+[Guid]::NewGuid().ToString('N'));$extract=Join-Path $root 'extracted';New-Item -ItemType Directory -Path $root | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($Archive);$names=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase);$length=0L
try{
    foreach($entry in $zip.Entries){
        if([IO.Path]::IsPathRooted($entry.FullName) -or $entry.FullName.Contains(':') -or $entry.FullName.Contains('\') -or ($entry.FullName -split '/') -contains '..' -or -not $names.Add($entry.FullName)){throw 'Unsafe or duplicate archive path.'}
        $length+=$entry.Length;if($names.Count -gt 4096 -or $length -gt 1GB){throw 'Published archive exceeds verification bounds.'}
    }
}finally{$zip.Dispose()}
[IO.Compression.ZipFile]::ExtractToDirectory($Archive,$extract)
$info=Get-Content -LiteralPath (Join-Path $extract 'BUILD-INFO.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if($info.appSha256 -ne (Get-FileHash -LiteralPath (Join-Path $extract 'LingdongCapsule.dll')).Hash -or $info.noticesSha256 -ne (Get-FileHash -LiteralPath (Join-Path $extract 'licenses\components.json')).Hash -or $info.selfContained -ne $true -or $info.rid -ne 'win-x64' -or ($RequireClean -and $info.sourceDirty)){throw 'Extracted application does not match provenance or clean-source requirement.'}
foreach($name in @('Install.ps1','Uninstall.ps1','Deployment.Common.ps1')){$bytes=[IO.File]::ReadAllBytes((Join-Path $extract $name));if($bytes.Length -lt 3 -or $bytes[0] -ne 239 -or $bytes[1] -ne 187 -or $bytes[2] -ne 191){throw 'Windows PowerShell script is missing its UTF-8 BOM.'}}
if(-not ('CapsuleBundleSmokeNative' -as [type])){
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class CapsuleBundleSmokeNative {
    public delegate bool Callback(IntPtr window,IntPtr value);
    [DllImport("user32.dll")] public static extern bool EnumWindows(Callback callback,IntPtr value);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr window,StringBuilder text,int count);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window,uint message,IntPtr wparam,IntPtr lparam);
}
'@
}
$arguments=if($MainStartup){''}else{'--install'};$expectedTitle=if($MainStartup){'灵动胶囊'}else{'安装灵动胶囊'}
$start=[Diagnostics.ProcessStartInfo]::new((Join-Path $extract 'LingdongCapsule.exe'),$arguments);$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden;$start.RedirectStandardError=$true;$start.RedirectStandardOutput=$true
$start.EnvironmentVariables['DOTNET_ROOT']=Join-Path $root 'no-shared-dotnet';$start.EnvironmentVariables['DOTNET_ROOT_X64']=$start.EnvironmentVariables['DOTNET_ROOT'];$start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP']='0'
$child=[Diagnostics.Process]::Start($start);$stderr=$child.StandardError.ReadToEndAsync();$stdout=$child.StandardOutput.ReadToEndAsync();$localRuntime=$false;$window=[IntPtr]::Zero;$guideFound=$false
try{
    $child.WaitForInputIdle(10000) | Out-Null
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while($watch.Elapsed.TotalSeconds -lt 30 -and -not $child.HasExited){
        $child.Refresh();foreach($module in $child.Modules){if($module.ModuleName -eq 'coreclr.dll'){$localRuntime=[IO.Path]::GetDirectoryName($module.FileName).Equals($extract,[StringComparison]::OrdinalIgnoreCase)}}
        $owned=[Collections.Generic.List[IntPtr]]::new();$guides=[Collections.Generic.List[IntPtr]]::new();$ownedId=[uint32]$child.Id
        [CapsuleBundleSmokeNative]::EnumWindows([CapsuleBundleSmokeNative+Callback]{param($handle,$unused) [uint32]$windowProcessId=0;[CapsuleBundleSmokeNative]::GetWindowThreadProcessId($handle,[ref]$windowProcessId) | Out-Null;if($windowProcessId -eq $ownedId){$title=[Text.StringBuilder]::new(128);[CapsuleBundleSmokeNative]::GetWindowText($handle,$title,128) | Out-Null;if($title.ToString() -eq $expectedTitle){$owned.Add($handle)};if($title.ToString() -eq '欢迎使用灵动胶囊'){$guides.Add($handle)}};return $true},[IntPtr]::Zero) | Out-Null
        $guideFound=$guides.Count -gt 0
        if($localRuntime -and $owned.Count -gt 0 -and (-not $MainStartup -or $guideFound)){$window=$owned[0];break}
        Start-Sleep -Milliseconds 100
    }
    if(-not $localRuntime -or $window -eq [IntPtr]::Zero){throw 'Extracted application did not initialize expected windows using its local runtime.'}
    [CapsuleBundleSmokeNative]::PostMessage($window,0x0010,[IntPtr]::Zero,[IntPtr]::Zero) | Out-Null
    if(-not $child.WaitForExit(5000) -or $child.ExitCode -ne 0){throw 'Owned installer did not exit cleanly.'}
    $result=[ordered]@{sourceCommit=$info.sourceCommit;sourceDirty=$info.sourceDirty;archiveSha256=$archiveHash;files=$names.Count;localRuntime=$localRuntime;mode=$(if($MainStartup){'fresh-profile-main-and-guide'}else{'installer'});guideInitialized=$guideFound;gracefulExit=$true;method='Extracted self-contained package; shared DOTNET_ROOT points to an absent owned directory. Only own process windows are closed; installation is not invoked. Existing Windows environment, not clean-system acceptance.'}
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'result.json') -Encoding UTF8
    Write-Output ('PASS extracted package checksum/provenance, script BOMs, local runtime, '+$result.mode+' initialization and clean exit; evidence: '+$root)
}finally{
    if(-not $child.HasExited){$child.Kill();$child.WaitForExit()}
    [IO.File]::WriteAllText((Join-Path $root 'stderr.txt'),$stderr.GetAwaiter().GetResult());$stdout.GetAwaiter().GetResult() | Out-Null;$child.Dispose()
}
