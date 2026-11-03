param([string]$OutputRoot)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
. (Join-Path $repo 'Deployment.Common.ps1')
if(-not $OutputRoot){$OutputRoot=Join-Path $repo ('artifacts\registration-tests\'+[Guid]::NewGuid().ToString('N'))}
$root=[IO.Path]::GetFullPath($OutputRoot);New-Item -ItemType Directory -Path $root -Force | Out-Null
$package=Join-Path $root 'package';$target=Join-Path $root 'installed';New-Item -ItemType Directory -Path $package -Force | Out-Null
foreach($file in @('Install.ps1','Uninstall.ps1','Deployment.Common.ps1')){[IO.File]::WriteAllText((Join-Path $package $file),[IO.File]::ReadAllText((Join-Path $repo $file)),[Text.UTF8Encoding]::new($true))}
$id=[Guid]::NewGuid().ToString('N');$scope=Get-CapsuleRegistration $id;$global:CapsuleRegistrationFault=@{Armed=$false;Key=$scope.Key};$results=[Collections.Generic.List[string]]::new()
function Check([bool]$ok,[string]$label){if(-not $ok){throw $label};$results.Add($label);Write-Host ('PASS '+$label)}
function New-ItemProperty {
    [CmdletBinding()]param([string]$Path,[string]$LiteralPath,[AllowEmptyString()][string]$Name,[object]$Value,[Microsoft.Win32.RegistryValueKind]$PropertyType,[switch]$Force)
    if($global:CapsuleRegistrationFault.Armed -and $Path -eq $global:CapsuleRegistrationFault.Key -and $Name -eq 'DisplayVersion'){$global:CapsuleRegistrationFault.Armed=$false;throw [IO.IOException]::new('Owned injected registration write failure')}
    Microsoft.PowerShell.Management\New-ItemProperty @PSBoundParameters
}
function Snapshot {
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($scope.Relative,$false)
    try {return (@($key.GetValueNames() | Sort-Object | ForEach-Object {[ordered]@{Name=$_;Kind=$key.GetValueKind($_).ToString();Value=$key.GetValue($_,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}}) | ConvertTo-Json -Depth 8 -Compress)}finally{$key.Dispose()}
}
function Install {& (Join-Path $package 'Install.ps1') -InstallDir $target -NoLaunch -TestRegistrationId $id}
try {
    [IO.File]::WriteAllText((Join-Path $package 'LingdongCapsule.exe'),'owned registered v1');Install
    $entry=Get-ItemProperty -LiteralPath $scope.Key
    $shell=New-Object -ComObject WScript.Shell;$shortcut=$shell.CreateShortcut($scope.Link)
    Check ($entry.InstallLocation -eq $target -and $entry.UninstallString.Contains($id) -and $shortcut.TargetPath -eq (Join-Path $target 'LingdongCapsule.exe')) 'Real test registration and shortcut point to installed target'
    Check (-not(Get-ItemProperty -LiteralPath $scope.Run -Name $scope.Name -ErrorAction SilentlyContinue)) 'Fresh installation does not enable startup'
    $startup='"'+(Join-Path $target 'LingdongCapsule.exe')+'" --startup --owned-extra'
    New-Item -Path $scope.Run -Force | Out-Null;New-ItemProperty -LiteralPath $scope.Run -Name $scope.Name -Value $startup -PropertyType String -Force | Out-Null
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($scope.Relative,$true)
    try{$key.SetValue('','owned default',[Microsoft.Win32.RegistryValueKind]::String);$key.SetValue('OwnedNumber',7,[Microsoft.Win32.RegistryValueKind]::DWord);$key.SetValue('OwnedExpand','%TEMP%\owned',[Microsoft.Win32.RegistryValueKind]::ExpandString)}finally{$key.Dispose()}
    $before=Snapshot;$linkBefore=[Convert]::ToBase64String([IO.File]::ReadAllBytes($scope.Link))
    [IO.File]::WriteAllText((Join-Path $package 'LingdongCapsule.exe'),'owned registered v2');$global:CapsuleRegistrationFault.Armed=$true;$failed=$false
    try{Install}catch{$failed=$true}
    Check ($failed -and -not $global:CapsuleRegistrationFault.Armed) 'Registry write failure is exercised after file switch and shortcut save'
    Check ([IO.File]::ReadAllText((Join-Path $target 'LingdongCapsule.exe')) -eq 'owned registered v1' -and (Snapshot) -eq $before -and [Convert]::ToBase64String([IO.File]::ReadAllBytes($scope.Link)) -eq $linkBefore) 'Failed registration restores files, default/typed registry values and exact shortcut bytes'
    Check ((Get-ItemProperty -LiteralPath $scope.Run -Name $scope.Name).($scope.Name) -eq $startup) 'Failed upgrade preserves startup command and arguments'
    Install
    Check ([IO.File]::ReadAllText((Join-Path $target 'LingdongCapsule.exe')) -eq 'owned registered v2' -and (Get-ItemProperty -LiteralPath $scope.Run -Name $scope.Name).($scope.Name) -eq $startup) 'Successful upgrade preserves exact existing startup choice'
    $oldTarget=$target;$target=Join-Path $root 'relocated';Install
    $relocatedStartup='"'+(Join-Path $target 'LingdongCapsule.exe')+'" --startup --owned-extra'
    Check ((Get-ItemProperty -LiteralPath $scope.Run -Name $scope.Name).($scope.Name) -eq $relocatedStartup) 'Moving installation updates executable path while retaining startup arguments'
    & (Join-Path $oldTarget 'Uninstall.ps1') -TestRegistrationId $id
    Check ((Get-ItemProperty -LiteralPath $scope.Key).InstallLocation -eq $target -and (Get-ItemProperty -LiteralPath $scope.Run -Name $scope.Name).($scope.Name) -eq $relocatedStartup -and (Test-Path -LiteralPath $scope.Link)) 'Uninstalling previous location preserves current installation registration'
    & (Join-Path $target 'Uninstall.ps1') -TestRegistrationId $id
    Check (-not(Test-Path -LiteralPath $scope.Key) -and -not(Test-Path -LiteralPath $scope.Link) -and -not(Get-ItemProperty -LiteralPath $scope.Run -Name $scope.Name -ErrorAction SilentlyContinue)) 'Uninstall removes only matching test registration, shortcut and startup value'
    Install;$custom='"'+(Join-Path $target 'LingdongCapsule.exe.backup')+'" --owned-custom'
    New-ItemProperty -LiteralPath $scope.Run -Name $scope.Name -Value $custom -PropertyType String -Force | Out-Null;Install
    & (Join-Path $target 'Uninstall.ps1') -TestRegistrationId $id
    Check ((Get-ItemProperty -LiteralPath $scope.Run -Name $scope.Name).($scope.Name) -eq $custom) 'A similar executable prefix cannot be overwritten or removed as owned startup'
    $results.ToArray() | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding utf8
    Write-Output ('Registration evidence: '+$root)
} finally {
    Remove-Variable CapsuleRegistrationFault -Scope Global -ErrorAction SilentlyContinue
    if(Test-Path -LiteralPath $scope.Key){Remove-Item -LiteralPath $scope.Key -Recurse -Force}
    Remove-ItemProperty -LiteralPath $scope.Run -Name $scope.Name -ErrorAction SilentlyContinue
    if(Test-Path -LiteralPath $scope.Link){Remove-Item -LiteralPath $scope.Link -Force}
}
