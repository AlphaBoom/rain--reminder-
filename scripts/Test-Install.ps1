# Intended only for an ephemeral, elevated Windows CI runner.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$package = Join-Path $root 'dist/win-x64'
try {
    & "$package/scripts/Install.ps1"
    $service = Get-CimInstance Win32_Service -Filter "Name='RainReminder'"
    if ($service.State -ne 'Running' -or $service.StartName -ne 'NT AUTHORITY\LocalService') {
        throw 'Service is not running under LocalService.'
    }
    $task = Get-ScheduledTask -TaskName 'RainReminder.Tray'
    if ($task.Principal.RunLevel -ne 'Limited') { throw 'Desktop task must run without elevation.' }
    $config = Join-Path $env:ProgramData 'RainReminder/config'
    if (-not (Get-Acl $config).AreAccessRulesProtected) { throw 'Config ACL inheritance was not disabled.' }
} finally {
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$package/scripts/Uninstall.ps1" -RemoveSettings
    if ($LASTEXITCODE -ne 0) { throw 'Uninstall failed.' }
}
if (Get-Service -Name RainReminder -ErrorAction SilentlyContinue) { throw 'Service still exists after uninstall.' }
if (Get-ScheduledTask -TaskName RainReminder.Tray -ErrorAction SilentlyContinue) { throw 'Task still exists after uninstall.' }
Write-Host 'Install/uninstall integration passed.'
