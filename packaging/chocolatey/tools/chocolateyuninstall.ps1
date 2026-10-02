$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# The tray app holds its icon and its files until it is told to go. Chocolatey
# deleting from under a running process is how an uninstall ends up half done
# with no error to show for it.
Get-Process -Name 'AudioWinFix' -ErrorAction SilentlyContinue |
  Stop-Process -Force -ErrorAction SilentlyContinue

$shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'AudioWinFix.lnk'
if (Test-Path $shortcut) { Remove-Item $shortcut -Force -ErrorAction SilentlyContinue }

Write-Host 'Your settings remain in %AppData%\AudioWinFix.'
