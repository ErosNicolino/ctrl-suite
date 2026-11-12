[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ErpRoot = 'D:\Grafica CA CP\ERP',
    [string]$ServiceName = 'GraficaCaixaServer',
    [string]$ServiceAccount = 'LocalSystem',
    [switch]$SkipStart
)
. (Join-Path $PSScriptRoot 'ERP-Recovery.Common.ps1')
Assert-Administrator
$layout = Get-ErpLayout $ErpRoot
$configuration = Test-ErpConfiguration $layout
$null = Test-ErpIdentity $layout
if (-not (Test-Path -LiteralPath $layout.ServiceExecutable -PathType Leaf)) { throw "Executável do serviço ausente: $($layout.ServiceExecutable)" }

Set-ErpIdentityAcl $layout $ServiceAccount
$binaryPath = '"{0}" --configuration "{1}"' -f $layout.ServiceExecutable,$layout.ConfigurationFile
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force; $existing.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30)) }
    & sc.exe config $ServiceName binPath= $binaryPath start= delayed-auto obj= $ServiceAccount DisplayName= 'Gráfica CACP - Servidor ERP' | Out-Null
} else {
    & sc.exe create $ServiceName binPath= $binaryPath start= auto obj= $ServiceAccount DisplayName= 'Gráfica CACP - Servidor ERP' | Out-Null
}
if ($LASTEXITCODE -ne 0) { throw 'Falha ao registrar/configurar o Windows Service.' }
& sc.exe config $ServiceName start= delayed-auto | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Falha ao configurar Automatic / Delayed Start.' }
& sc.exe failure $ServiceName reset= 86400 actions= 'restart/5000/restart/15000/restart/60000' | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null

if (-not $SkipStart) {
    Start-Service -Name $ServiceName
    (Get-Service -Name $ServiceName).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $listener = Get-NetTCPConnection -LocalPort $configuration.Port -State Listen -ErrorAction SilentlyContinue
        if ($listener) { break }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $listener) { throw "O serviço iniciou, mas a porta $($configuration.Port) não entrou em escuta." }
    Test-ErpPing $configuration.Port $layout.Key
}

$network = Get-ErpNetworkDiagnostic
Write-Host "Serviço: $ServiceName"
Write-Host "Inicialização: Automatic / Delayed Start"
Write-Host "Recovery: configurado"
Write-Host "Banco validado: $($layout.Database)"
Write-Host "Identidade persistente: válida"
Write-Host "Porta: $($configuration.Port)"
Write-Host "LAN: $($network.Lan -join ', ')"
Write-Host "Tailscale: $($network.Tailscale -join ', ')"
if ($network.Tailscale.Count -eq 0) { Write-Warning 'Tailscale precisa ser instalado/conectado para acesso remoto.' }

