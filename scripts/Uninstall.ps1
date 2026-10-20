param([switch]$RemoveSettings)
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$admin = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $admin.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $extra = if ($RemoveSettings) { ' -RemoveSettings' } else { '' }
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"$extra"
    exit
}
$task = Get-ScheduledTask -TaskName 'RainReminder.Tray' -ErrorAction SilentlyContinue
if ($task) { Stop-ScheduledTask -TaskName 'RainReminder.Tray'; Unregister-ScheduledTask -TaskName 'RainReminder.Tray' -Confirm:$false }
Get-Process -Name 'RainReminder.Tray' -ErrorAction SilentlyContinue | Stop-Process -Force
$service = Get-Service -Name 'RainReminder' -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service RainReminder }
    & sc.exe delete RainReminder
    if ($LASTEXITCODE -ne 0) { throw 'Failed to delete service.' }
}
Remove-Item (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Rain Reminder.lnk') -Force -ErrorAction SilentlyContinue
if ($RemoveSettings) { Remove-Item (Join-Path $env:ProgramData 'RainReminder') -Recurse -Force }
# Copy this script to a temporary directory before invoking it if removing the
# installed copy; PowerShell has already loaded the script into memory.
Remove-Item (Join-Path $env:ProgramFiles 'RainReminder') -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Uninstalled. Settings are retained unless -RemoveSettings was supplied.'
