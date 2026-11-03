param([string]$InstallDir=(Join-Path $env:LOCALAPPDATA 'Programs\LingdongCapsule'))
$ErrorActionPreference='Stop'
$source=(Resolve-Path -LiteralPath $PSScriptRoot).Path
$target=[IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
if(-not(Test-Path -LiteralPath (Join-Path $source 'LingdongCapsule.exe'))){throw '请先完整解压发布包，再运行安装程序。'}
if($target -eq [IO.Path]::GetPathRoot($target).TrimEnd('\') -or $target -eq $source){throw '安装目录必须是独立文件夹，不能是磁盘根目录或当前发布包目录。'}
if((Test-Path -LiteralPath $target) -and (Get-ChildItem -LiteralPath $target -Force) -and -not(Test-Path -LiteralPath (Join-Path $target 'installed-files.json'))){throw '目标文件夹已有其他文件，请选择空文件夹。'}
$exe=Join-Path $target 'LingdongCapsule.exe'
Get-Process LingdongCapsule -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $exe} | Stop-Process
New-Item -ItemType Directory -Path $target -Force | Out-Null
$files=Get-ChildItem -LiteralPath $source -File -Recurse
foreach($file in $files){$relative=$file.FullName.Substring($source.Length+1);$destination=Join-Path $target $relative;New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null;Copy-Item -LiteralPath $file.FullName -Destination $destination -Force}
@($files | ForEach-Object {$_.FullName.Substring($source.Length+1)}) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'installed-files.json') -Encoding utf8
$shell=New-Object -ComObject WScript.Shell
$shortcut=$shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) '灵动胶囊.lnk'))
$shortcut.TargetPath=$exe;$shortcut.WorkingDirectory=$target;$shortcut.IconLocation="$exe,0";$shortcut.Description='灵动胶囊 · Windows 桌面交互胶囊';$shortcut.Save()
$uninstall='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LingdongCapsule'
New-Item -Path $uninstall -Force | Out-Null
(@{DisplayName='灵动胶囊';DisplayVersion='0.1.0';Publisher='LingdongCapsule contributors';DisplayIcon="$exe,0";InstallLocation=$target;UninstallString=('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $target 'Uninstall.ps1')+'"')}).GetEnumerator() | ForEach-Object {New-ItemProperty -Path $uninstall -Name $_.Key -Value $_.Value -PropertyType String -Force | Out-Null}
$run='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if((Get-ItemProperty -Path $run -Name LingdongCapsule -ErrorAction SilentlyContinue).LingdongCapsule){Set-ItemProperty -Path $run -Name LingdongCapsule -Value ('"'+$exe+'" --startup')}
Write-Host "已安装到 $target。首次使用引导即将打开。"
Start-Process -FilePath $exe
