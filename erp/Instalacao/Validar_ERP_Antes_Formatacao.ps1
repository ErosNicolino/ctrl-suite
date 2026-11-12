[CmdletBinding()]
param(
    [string]$ErpRoot = 'D:\Grafica CA CP\ERP',
    [string]$ReportPath = ''
)
. (Join-Path $PSScriptRoot 'ERP-Recovery.Common.ps1')
$layout = Get-ErpLayout $ErpRoot
$configuration = Test-ErpConfiguration $layout
$identity = Test-ErpIdentity $layout
$backupRoot = Join-Path $layout.Root 'Backups'
$lastBackup = Get-ChildItem -LiteralPath $backupRoot -Recurse -File -Filter '*.db' -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
$requiredScripts = @('Restaurar_ERP_Pos_Formatacao.ps1','Instalar_Servico_ERP.ps1','ERP-Recovery.Common.ps1')
$missingScripts = @($requiredScripts | Where-Object { -not (Test-Path -LiteralPath (Join-Path $layout.Installation $_) -PathType Leaf) })
$shareName = 'ERP'
$sharePath = $layout.Root
$documentsExists = Test-Path -LiteralPath $layout.Root -PathType Container
$lines = @(
    '# Diagnóstico do ERP antes da formatação',
    '',
    "Gerado em: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')",
    '',
    "- Banco: OK — $($layout.Database)",
    "- Último backup: $(if($lastBackup){$lastBackup.FullName}else{'NÃO ENCONTRADO'})",
    "- servidor.json: OK — $($layout.ConfigurationFile)",
    "- Identidade: OK — certificado SHA-256 $($identity.CertificateThumbprint); arquivo de chave SHA-256 $($identity.KeyHash)",
    "- Programa do servidor: $(if(Test-Path -LiteralPath $layout.ServiceExecutable){'OK'}else{'NÃO ENCONTRADO'}) — $($layout.ServiceExecutable)",
    "- Scripts de recuperação: $(if($missingScripts.Count -eq 0){'OK'}else{'AUSENTES: '+($missingScripts -join ', ')})",
    "- DocumentsRoot físico: $(if($documentsExists){'OK'}else{'NÃO ENCONTRADO'}) — $($layout.Root)",
    "- Compartilhamento esperado: $shareName -> $sharePath",
    "- Porta configurada: $($configuration.Port)",
    '',
    'Nenhuma chave, senha, PairingCode ou conteúdo do PFX foi incluído neste relatório.'
)
if ([string]::IsNullOrWhiteSpace($ReportPath)) { $ReportPath = Join-Path $PSScriptRoot ("Diagnostico-Antes-Formatacao-{0}.md" -f (Get-Date -Format 'yyyyMMdd-HHmmss')) }
$lines | Set-Content -LiteralPath $ReportPath -Encoding UTF8
Write-Host "Relatório criado: $ReportPath"

