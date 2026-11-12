[CmdletBinding()]
param([string]$ErpRoot = 'D:\Grafica CA CP\ERP')
. (Join-Path $PSScriptRoot 'ERP-Recovery.Common.ps1')
Assert-Administrator
$layout = Get-ErpLayout $ErpRoot
$configuration = Test-ErpConfiguration $layout
$identity = Test-ErpIdentity $layout

Write-Host 'Estrutura persistente localizada e validada.'
Write-Host "Banco existente: $($layout.Database)"
Write-Host "Identidade: válida (certificado $($identity.CertificateThumbprint.Substring(0,12))...)"
if (-not (Test-Path -LiteralPath $layout.ServiceExecutable -PathType Leaf)) { throw "Servidor self-contained ausente: $($layout.ServiceExecutable)" }

$tailscale = Get-Command tailscale.exe -ErrorAction SilentlyContinue
if (-not $tailscale) { Write-Warning 'Tailscale precisa ser instalado/conectado para acesso remoto.' }
else {
    $status = & $tailscale.Source status --json 2>$null | ConvertFrom-Json -ErrorAction SilentlyContinue
    if (-not $status -or $status.BackendState -ne 'Running') { Write-Warning 'Tailscale precisa ser instalado/conectado para acesso remoto.' }
}

$shareName = 'ERP'
$sharePath = $layout.Root
$share = Get-SmbShare -Name $shareName -ErrorAction SilentlyContinue
if (-not $share -or ([IO.Path]::GetFullPath($share.Path).TrimEnd('\') -ne [IO.Path]::GetFullPath($sharePath).TrimEnd('\'))) {
    Write-Warning "Compartilhamento SMB esperado não localizado: nome '$shareName', pasta '$sharePath'. Recrie-o somente após revisar permissões de compartilhamento e NTFS; este script não altera SMB."
} else { Write-Host "Compartilhamento SMB: $shareName -> $sharePath" }

& (Join-Path $PSScriptRoot 'Instalar_Servico_ERP.ps1') -ErpRoot $layout.Root
if ($LASTEXITCODE -ne 0) { throw 'A instalação do serviço não foi concluída.' }
Write-Host 'RECUPERAÇÃO CONCLUÍDA: mesma base, mesma identidade e porta configurada.'
Write-Host 'Valide agora o PC1, o PC2 e o notebook. Nenhum banco foi criado ou copiado.'

