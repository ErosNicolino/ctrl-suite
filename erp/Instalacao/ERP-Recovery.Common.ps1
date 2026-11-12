Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Execute este script como Administrador.'
    }
}

function Get-ErpLayout([string]$ErpRoot) {
    $root = [IO.Path]::GetFullPath($ErpRoot).TrimEnd('\')
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        if ($root.StartsWith('D:\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Estrutura ERP não encontrada em D:.' }
        throw "Estrutura ERP não encontrada em $root."
    }
    $layout = [ordered]@{
        Root = $root
        Program = Join-Path $root 'Programa'
        Data = Join-Path $root 'Dados'
        Configuration = Join-Path $root 'Configuracao'
        ConfigurationFile = Join-Path $root 'Configuracao\servidor.json'
        Identity = Join-Path $root 'Servidor\Identidade'
        Logs = Join-Path $root 'Servidor\Logs'
        Installation = Join-Path $root 'Instalacao'
        Database = Join-Path $root 'Dados\caixa_dados.db'
        Certificate = Join-Path $root 'Servidor\Identidade\servidor.pfx'
        Key = Join-Path $root 'Servidor\Identidade\chave-servidor.txt'
        ServiceExecutable = Join-Path $root 'Programa\Servidor\GraficaCaixaServer.exe'
    }
    foreach ($name in @('Program','Data','Configuration','Identity')) {
        if (-not (Test-Path -LiteralPath $layout[$name] -PathType Container)) { throw "Pasta obrigatória ausente: $($layout[$name])" }
    }
    [pscustomobject]$layout
}

function Test-ErpConfiguration($Layout) {
    if (-not (Test-Path -LiteralPath $Layout.ConfigurationFile -PathType Leaf)) { throw "Configuração ausente: $($Layout.ConfigurationFile)" }
    try { $configuration = Get-Content -LiteralPath $Layout.ConfigurationFile -Raw | ConvertFrom-Json }
    catch { throw "servidor.json inválido: $($_.Exception.Message)" }
    if ([string]::IsNullOrWhiteSpace([string]$configuration.DataDirectory)) { throw 'DataDirectory não foi informado em servidor.json.' }
    $configuredData = [IO.Path]::GetFullPath([string]$configuration.DataDirectory).TrimEnd('\')
    if ($configuredData -ne $Layout.Data) { throw "DataDirectory não corresponde à pasta persistente esperada: $($Layout.Data)" }
    if (-not (Test-Path -LiteralPath $Layout.Database -PathType Leaf)) { throw "Banco ausente: $($Layout.Database). Nenhuma base vazia será criada." }
    $portProperty = $configuration.PSObject.Properties['Port']
    $port = if ($null -ne $portProperty -and [int]$portProperty.Value -gt 0) { [int]$portProperty.Value } else { 5074 }
    if ($port -lt 1 -or $port -gt 65535) { throw 'Port inválido em servidor.json.' }
    [pscustomobject]@{ Configuration = $configuration; Port = $port }
}

function Test-ErpIdentity($Layout) {
    if (-not (Test-Path -LiteralPath $Layout.Certificate -PathType Leaf) -or -not (Test-Path -LiteralPath $Layout.Key -PathType Leaf)) {
        throw "Identidade persistente ausente ou incompleta: $($Layout.Identity). Nenhuma nova identidade será gerada."
    }
    try {
        $keyText = (Get-Content -LiteralPath $Layout.Key -Raw).Trim()
        $keyBytes = [Convert]::FromBase64String($keyText)
        if ($keyBytes.Length -lt 32) { throw 'chave curta' }
        $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($Layout.Certificate)
        if (-not $certificate.HasPrivateKey -or $certificate.NotAfter.ToUniversalTime() -le [DateTime]::UtcNow) { throw 'certificado sem chave privada válida ou expirado' }
        [pscustomobject]@{ CertificateThumbprint = (Get-FileHash -LiteralPath $Layout.Certificate -Algorithm SHA256).Hash; KeyHash = (Get-FileHash -LiteralPath $Layout.Key -Algorithm SHA256).Hash }
    } catch { throw "Identidade persistente inválida: $($_.Exception.Message)" }
}

function Set-ErpIdentityAcl($Layout, [string]$ServiceAccount = 'SYSTEM') {
    New-Item -ItemType Directory -Path $Layout.Identity -Force | Out-Null
    & icacls.exe $Layout.Identity /inheritance:r | Out-Null
    & icacls.exe $Layout.Identity /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível aplicar as ACLs de SYSTEM e Administradores.' }
    if ($ServiceAccount -and $ServiceAccount -notin @('SYSTEM','LocalSystem','NT AUTHORITY\SYSTEM')) {
        & icacls.exe $Layout.Identity /grant:r "${ServiceAccount}:(OI)(CI)RX" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Não foi possível conceder leitura à conta do serviço: $ServiceAccount" }
    }
}

function Test-ErpPing([int]$Port, [string]$KeyPath) {
    $previousValidation = [Net.ServicePointManager]::ServerCertificateValidationCallback
    try {
        [Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
        $headers = @{ Authorization = 'Bearer ' + (Get-Content -LiteralPath $KeyPath -Raw).Trim() }
        $response = Invoke-WebRequest -Uri "https://127.0.0.1:$Port/ping" -Headers $headers -UseBasicParsing -TimeoutSec 5
        if ([int]$response.StatusCode -lt 200 -or [int]$response.StatusCode -ge 300) { throw "HTTP $([int]$response.StatusCode)" }
    } finally { [Net.ServicePointManager]::ServerCertificateValidationCallback = $previousValidation }
}

function Get-ErpNetworkDiagnostic {
    $addresses = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -notlike '127.*' -and $_.AddressState -eq 'Preferred' }
    $lan = $addresses | Where-Object { $_.IPAddress -notlike '100.*' } | Select-Object -ExpandProperty IPAddress
    $tailscale = $addresses | Where-Object { $octets = $_.IPAddress.Split('.'); [int]$octets[0] -eq 100 -and [int]$octets[1] -ge 64 -and [int]$octets[1] -le 127 } | Select-Object -ExpandProperty IPAddress
    [pscustomobject]@{ Lan = @($lan); Tailscale = @($tailscale) }
}

