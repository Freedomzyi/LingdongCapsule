param([string]$InstallDir=(Join-Path $env:LOCALAPPDATA 'Programs\LingdongCapsule'),[switch]$NoLaunch,[switch]$NoRegistration,[string]$TestRegistrationId='',[switch]$ReportProgress)
$ErrorActionPreference='Stop'
if($ReportProgress){[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$OutputEncoding=[Console]::OutputEncoding}
. (Join-Path $PSScriptRoot 'Deployment.Common.ps1')
$source=(Resolve-Path -LiteralPath $PSScriptRoot).Path
$target=Assert-CapsulePath $InstallDir
if(-not(Test-Path -LiteralPath (Join-Path $source 'LingdongCapsule.exe'))){throw '请先完整解压发布包，再运行安装程序。'}
if($target -eq $source -or $target.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase) -or $source.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase)){throw '安装目录和发布包目录必须相互独立。'}
Assert-CapsuleTree $source
if((Test-Path -LiteralPath $target) -and (Get-ChildItem -LiteralPath $target -Force) -and -not(Test-Path -LiteralPath (Join-Path $target 'installed-files.json'))){throw '目标文件夹已有其他文件，请选择空文件夹。'}
$exe=Join-Path $target 'LingdongCapsule.exe'
$oldFiles=@();if(Test-Path -LiteralPath $target){Assert-CapsuleTree $target;if(Test-Path -LiteralPath (Join-Path $target 'installed-files.json')){$oldFiles=@(Read-CapsuleFiles $target)}}
$stage=Assert-CapsulePath ($target+'.staging-'+[Guid]::NewGuid().ToString('N'))
$backup=Assert-CapsulePath ($target+'.previous-'+[Guid]::NewGuid().ToString('N'))
$committed=$false;$movedOld=$false;$registrationStarted=$false
$registration=Get-CapsuleRegistration $TestRegistrationId
$run=$registration.Run;$uninstall=$registration.Key;$link=$registration.Link;$registrationName=$registration.Name
$oldRun=(Get-ItemProperty -Path $run -Name $registrationName -ErrorAction SilentlyContinue).$registrationName
$oldLink=$null;$oldRegistry=$null
if(-not $NoRegistration){
    if(Test-Path -LiteralPath $link){$oldLink=[IO.File]::ReadAllBytes($link)}
    if(Test-Path -LiteralPath $uninstall){$key=Get-Item -LiteralPath $uninstall;try{$oldRegistry=@{};foreach($name in $key.GetValueNames()){$oldRegistry[$name]=@{Value=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);Kind=$key.GetValueKind($name)}}}finally{$key.Dispose()}}
}
try {
    Write-CapsuleProgress $ReportProgress preparing
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    if(Test-Path -LiteralPath $target){foreach($entry in Get-ChildItem -LiteralPath $target -Force){Copy-Item -LiteralPath $entry.FullName -Destination $stage -Recurse -Force}}
    foreach($relative in $oldFiles){$old=Get-CapsuleChild $stage $relative;if(Test-Path -LiteralPath $old){Remove-Item -LiteralPath $old -Force}}
    Write-CapsuleProgress $ReportProgress copying
    $files=@(Get-ChildItem -LiteralPath $source -File -Recurse -Force | Where-Object {$_.Name -ne 'installed-files.json'})
    foreach($file in $files){$relative=$file.FullName.Substring($source.Length+1);$destination=Get-CapsuleChild $stage $relative;if(Test-Path -LiteralPath $destination){throw '新程序文件与用户自有文件冲突，停止升级。'};New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null;Copy-Item -LiteralPath $file.FullName -Destination $destination -Force;if((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $destination).Hash){throw '安装文件校验失败。'}}
    @($files | ForEach-Object {$_.FullName.Substring($source.Length+1)}) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'installed-files.json') -Encoding utf8
    $null=Read-CapsuleFiles $stage
    Write-CapsuleProgress $ReportProgress committing
    Stop-CapsuleExecutable $exe
    if(Test-Path -LiteralPath $target){Move-Item -LiteralPath $target -Destination $backup;$movedOld=$true}
    Move-Item -LiteralPath $stage -Destination $target;$committed=$true
if(-not $NoRegistration){
    Write-CapsuleProgress $ReportProgress registering
    $registrationStarted=$true
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut($link)
$shortcut.TargetPath=$exe;$shortcut.WorkingDirectory=$target;$shortcut.IconLocation="$exe,0";$shortcut.Description='灵动胶囊 · Windows 桌面交互胶囊';$shortcut.Save()
New-Item -Path $uninstall -Force | Out-Null
$uninstallCommand='powershell.exe -NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $target 'Uninstall.ps1')+'"'
if(-not $TestRegistrationId){$uninstallCommand='"'+$exe+'" --uninstall'}
if($TestRegistrationId){$uninstallCommand+=' -TestRegistrationId '+$TestRegistrationId}
$displayVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
if([string]::IsNullOrWhiteSpace($displayVersion)){$displayVersion='0.1.0'}
(@{DisplayName='灵动胶囊';DisplayVersion=$displayVersion;Publisher='LingdongCapsule contributors';DisplayIcon="$exe,0";InstallLocation=$target;UninstallString=$uninstallCommand}).GetEnumerator() | ForEach-Object {New-ItemProperty -Path $uninstall -Name $_.Key -Value $_.Value -PropertyType String -Force | Out-Null}
if($oldRun -and -not(Test-CapsuleStartup $oldRun $exe) -and $oldRegistry -and $oldRegistry.ContainsKey('InstallLocation')){
    $oldExe=Join-Path $oldRegistry['InstallLocation'].Value 'LingdongCapsule.exe'
    if(Test-CapsuleStartup $oldRun $oldExe){$tail=[regex]::Match($oldRun,'^\s*(?:"[^"]+"|\S+)(.*)$').Groups[1].Value;Set-ItemProperty -Path $run -Name $registrationName -Value ('"'+$exe+'"'+$tail)}
}
}
} catch {
    $failure=$_
    if($registrationStarted){
      try {
        if(Test-Path -LiteralPath $uninstall){Remove-Item -LiteralPath $uninstall -Recurse -Force}
        if($oldRegistry -ne $null){New-Item -Path $uninstall -Force | Out-Null;$restore=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($registration.Relative,$true);try{foreach($name in $oldRegistry.Keys){$restore.SetValue($name,$oldRegistry[$name].Value,$oldRegistry[$name].Kind)}}finally{$restore.Dispose()}}
        if($oldLink -ne $null){[IO.File]::WriteAllBytes($link,$oldLink)}elseif(Test-Path -LiteralPath $link){Remove-Item -LiteralPath $link}
        if($oldRun){Set-ItemProperty -Path $run -Name $registrationName -Value $oldRun}
      } catch {Write-Warning ('登记恢复未完成，旧版文件仍将恢复：'+$_.Exception.Message)}
    }
    if($committed){Remove-CapsuleStage $target $target}
    if($movedOld){Move-Item -LiteralPath $backup -Destination $target}
    throw $failure
} finally {if(Test-Path -LiteralPath $stage){Remove-CapsuleStage $stage $stage}}
if($movedOld){Write-Host "旧版完整保留在 $backup。"}
Write-Host "已安装到 $target。"
# Interactive launch requested by the installer user, not a background helper.
Write-CapsuleProgress $ReportProgress completed
if(-not $NoLaunch){Start-Process -FilePath $exe}
