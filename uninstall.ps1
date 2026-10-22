#Requires -RunAsAdministrator
# Remove o SlayerDisplay e volta a ativar o serviço original da NTECH (se ainda existir).

$ErrorActionPreference = 'Stop'
$dst = 'C:\Program Files\SlayerDisplay'

if (Get-Service SlayerDisplay -ErrorAction SilentlyContinue) {
    Stop-Service SlayerDisplay -Force -ErrorAction SilentlyContinue
    sc.exe delete SlayerDisplay | Out-Null
}
Remove-Item $dst -Recurse -Force -ErrorAction SilentlyContinue

if (Get-Service SLAYERCPULiveTemperature -ErrorAction SilentlyContinue) {
    Set-Service SLAYERCPULiveTemperature -StartupType Automatic
    Start-Service SLAYERCPULiveTemperature -ErrorAction SilentlyContinue
}
Write-Host 'SlayerDisplay removido.'
