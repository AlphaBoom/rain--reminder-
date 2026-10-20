param([string]$UserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value)
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$admin = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $admin.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-UserSid', "`"$UserSid`"")
    exit
}
# Validate the passed identity rather than accepting arbitrary icacls arguments.
$sid = New-Object Security.Principal.SecurityIdentifier($UserSid)
$UserSid = $sid.Value
$account = $sid.Translate([Security.Principal.NTAccount]).Value
$source = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $env:ProgramFiles 'RainReminder'
$shared = Join-Path $env:ProgramData 'RainReminder'

function Invoke-Native([string]$Exe, [string[]]$NativeArgs) {
    & $Exe @NativeArgs
    if ($LASTEXITCODE -ne 0) { throw "$Exe failed with exit code $LASTEXITCODE" }
}

if (-not (Test-Path (Join-Path $source 'service/RainReminder.Service.exe')) -or
    -not (Test-Path (Join-Path $source 'tray/RainReminder.Tray.exe'))) {
    throw 'Please run Install.cmd from the extracted Windows release package.'
}
$task = Get-ScheduledTask -TaskName 'RainReminder.Tray' -ErrorAction SilentlyContinue
if ($task) { Stop-ScheduledTask -TaskName 'RainReminder.Tray' }
Get-Process -Name 'RainReminder.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force
$service = Get-Service -Name 'RainReminder' -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') { Stop-Service RainReminder }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item (Join-Path $source 'service') $destination -Recurse -Force
Copy-Item (Join-Path $source 'tray') $destination -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'Uninstall.ps1') $destination -Force

New-Item -ItemType Directory -Path $shared -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $shared 'config') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $shared 'status') -Force | Out-Null
# Service runs as LocalService. Only the installing user can change settings.
# Administrators/SYSTEM retain ownership; writable data never contains executable code.
Invoke-Native 'icacls.exe' @($shared, '/inheritance:r', '/grant:r', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-544:(OI)(CI)F', '*S-1-5-19:(RX)', "*$($UserSid):(RX)")
Invoke-Native 'icacls.exe' @((Join-Path $shared 'config'), '/inheritance:r', '/grant:r', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-544:(OI)(CI)F', '*S-1-5-19:(OI)(CI)RX', "*$($UserSid):(OI)(CI)M")
Invoke-Native 'icacls.exe' @((Join-Path $shared 'status'), '/inheritance:r', '/grant:r', '*S-1-5-18:(OI)(CI)F', '*S-1-5-32-544:(OI)(CI)F', '*S-1-5-19:(OI)(CI)M', "*$($UserSid):(OI)(CI)RX")
$binaryPath = '"' + (Join-Path $destination 'service/RainReminder.Service.exe') + '"'
if ($service) {
    $existing = Get-CimInstance Win32_Service -Filter "Name='RainReminder'"
    $change = Invoke-CimMethod -InputObject $existing -MethodName Change -Arguments @{ PathName = $binaryPath; StartMode = 'Automatic'; StartName = 'NT AUTHORITY\LocalService' }
    if ($change.ReturnValue -ne 0) { throw "Service update failed: $($change.ReturnValue)" }
    Invoke-Native 'sc.exe' @('config', 'RainReminder', 'start=', 'delayed-auto')
} else {
    New-Service -Name 'RainReminder' -DisplayName 'Rain Reminder' -BinaryPathName $binaryPath -StartupType Automatic -Credential (New-Object Management.Automation.PSCredential('NT AUTHORITY\LocalService', (New-Object Security.SecureString))) | Out-Null
    Invoke-Native 'sc.exe' @('config', 'RainReminder', 'start=', 'delayed-auto')
}
Invoke-Native 'sc.exe' @('description', 'RainReminder', 'Checks morning rain forecasts and publishes reminders to the desktop companion.')
Invoke-Native 'sc.exe' @('failure', 'RainReminder', 'reset=', '86400', 'actions=', 'restart/60000/restart/60000/restart/60000')
Start-Service RainReminder

$tray = Join-Path $destination 'tray/RainReminder.Tray.exe'
$action = New-ScheduledTaskAction -Execute $tray
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $account
$principal = New-ScheduledTaskPrincipal -UserId $UserSid -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
Register-ScheduledTask -TaskName 'RainReminder.Tray' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Rain Reminder.lnk'))
$shortcut.TargetPath = $tray
$shortcut.Arguments = '--settings'
$shortcut.Save()
Start-ScheduledTask -TaskName 'RainReminder.Tray'
Write-Host 'Installed. Choose your location using the tray settings window.'
