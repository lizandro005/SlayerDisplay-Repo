#Requires -RunAsAdministrator
<#
  Instala o SlayerDisplay como serviço do Windows e desativa o serviço original da NTECH.

  Funciona de duas formas:
    - a partir de uma release (SlayerDisplay.exe na mesma pasta que este script);
    - a partir do código-fonte, depois de: dotnet publish src/SlayerDisplay -c Release -o publish

  O TempComm.dll (proprietário da NTECH) é copiado da instalação original do
  "SLAYER CPU Live Temperature", que tem de estar instalada.
#>

$ErrorActionPreference = 'Stop'
$orig = 'C:\Program Files (x86)\SLAYER CPU Live Temperature'
$dst  = 'C:\Program Files\SlayerDisplay'

# Onde estão os binários?
$candidates = @($PSScriptRoot, (Join-Path $PSScriptRoot 'publish'))
$src = $candidates | Where-Object { Test-Path (Join-Path $_ 'SlayerDisplay.exe') } | Select-Object -First 1
if (-not $src) {
    throw "SlayerDisplay.exe não encontrado. Descarrega uma release ou corre: dotnet publish src/SlayerDisplay -c Release -o publish"
}

# TempComm.dll
$tempComm = Join-Path $orig 'TempComm.dll'
if (-not (Test-Path (Join-Path $src 'TempComm.dll')) -and -not (Test-Path $tempComm)) {
    throw "TempComm.dll não encontrado em '$orig'. Instala primeiro o software original da NTECH (SLAYER CPU Live Temperature)."
}

# 1. Parar e desativar o serviço original (prende o display e carrega o driver WinRing0)
if (Get-Service SLAYERCPULiveTemperature -ErrorAction SilentlyContinue) {
    Write-Host 'A desativar o serviço original SLAYERCPULiveTemperature...'
    Stop-Service SLAYERCPULiveTemperature -Force -ErrorAction SilentlyContinue
    Set-Service  SLAYERCPULiveTemperature -StartupType Disabled
}

# 2. Copiar ficheiros
if (Get-Service SlayerDisplay -ErrorAction SilentlyContinue) {
    Stop-Service SlayerDisplay -Force -ErrorAction SilentlyContinue
}
New-Item $dst -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $src '*') $dst -Recurse -Force -Exclude 'install.ps1','uninstall.ps1'
if (-not (Test-Path (Join-Path $dst 'TempComm.dll'))) { Copy-Item $tempComm $dst -Force }

# 3. Registar e arrancar
if (-not (Get-Service SlayerDisplay -ErrorAction SilentlyContinue)) {
    New-Service -Name SlayerDisplay -BinaryPathName "`"$dst\SlayerDisplay.exe`"" `
        -DisplayName 'Slayer RXA50 Display' `
        -Description 'Envia a temperatura do CPU para o display do NTECH Slayer RXA50 (LibreHardwareMonitor).' `
        -StartupType Automatic | Out-Null
    sc.exe failure SlayerDisplay reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
}
Start-Service SlayerDisplay
Get-Service SlayerDisplay
Write-Host "`nInstalado em $dst."
