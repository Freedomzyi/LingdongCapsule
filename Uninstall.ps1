param([string]$TestRegistrationId='')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Deployment.Common.ps1')
$target=Assert-CapsulePath ((Resolve-Path -LiteralPath $PSScriptRoot).Path)
$manifest=Join-Path $target 'installed-files.json'
if(-not(Test-Path -LiteralPath $manifest) -or -not(Test-Path -LiteralPath (Join-Path $target 'LingdongCapsule.exe'))){throw '没有找到本程序的安装清单，停止卸载。便携版可退出后手动移除解压目录。'}
$exe=Join-Path $target 'LingdongCapsule.exe'
Assert-CapsuleTree $target
$paths=@(Read-CapsuleFiles $target)
$checked=@($paths | ForEach-Object {Get-CapsuleChild $target $_})
Stop-CapsuleExecutable $exe
$registration=Get-CapsuleRegistration $TestRegistrationId;$run=$registration.Run;$registrationName=$registration.Name
$startup=(Get-ItemProperty -Path $run -Name $registrationName -ErrorAction SilentlyContinue).$registrationName
if($startup -and (Test-CapsuleStartup $startup $exe)){Remove-ItemProperty -Path $run -Name $registrationName}
$link=$registration.Link
if(Test-Path -LiteralPath $link){$shell=New-Object -ComObject WScript.Shell;if($shell.CreateShortcut($link).TargetPath -eq $exe){Remove-Item -LiteralPath $link}}
$uninstall=$registration.Key
if((Get-ItemProperty -Path $uninstall -ErrorAction SilentlyContinue).InstallLocation -eq $target){Remove-Item -LiteralPath $uninstall}
foreach($path in $checked){if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}}
Remove-Item -LiteralPath $manifest
Get-ChildItem -LiteralPath $target -Directory -Recurse | Sort-Object FullName -Descending | ForEach-Object {$folder=Assert-CapsulePath $_.FullName;if(-not(Get-ChildItem -LiteralPath $folder -Force)){Remove-Item -LiteralPath $folder}}
if(-not(Get-ChildItem -LiteralPath $target -Force)){Remove-Item -LiteralPath $target}
Write-Host '灵动胶囊已卸载。个人设置已保留在 LocalAppData\LingdongCapsule。'
