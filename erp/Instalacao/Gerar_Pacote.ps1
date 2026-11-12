# Gera o pacote de atualização do ERP (uso do desenvolvimento).
# Uso:  powershell -ExecutionPolicy Bypass -File Instalacao\Gerar_Pacote.ps1 -Notas <O_QUE_MUDOU.txt> [-Saida <pasta>]
# - confere se a versão está igual nos 5 lugares (programa, servidor e os 3 da PWA);
# - publica programa e servidor (arquivo único, autocontido), copia o código-fonte (sem bin/obj/.git/work);
# - inclui os scripts de Instalacao\Pacote, versao.txt, LEIA-ME e O_QUE_MUDOU;
# - gera pacote.sha256 (o botão "Atualizar ERP" recusa pacote alterado ou incompleto).
param([Parameter(Mandatory)][string]$Notas, [string]$Saida)
$ErrorActionPreference = 'Stop'
$fonte = Split-Path $PSScriptRoot -Parent
function Versao([string]$arquivo, [string]$padrao) { $m = Select-String -LiteralPath (Join-Path $fonte $arquivo) -Pattern $padrao | Select-Object -First 1; if (-not $m) { throw "Versão não encontrada em $arquivo" }; $m.Matches[0].Groups[1].Value }
$versoes = [ordered]@{
    'Wpf/GraficaCaixa.Wpf.csproj'               = Versao 'Wpf\GraficaCaixa.Wpf.csproj' '<Version>([\d.]+)</Version>'
    'ServerHost/GraficaCaixa.ServerHost.csproj' = Versao 'ServerHost\GraficaCaixa.ServerHost.csproj' '<Version>([\d.]+)</Version>'
    'Core/Servidor/BrowserGateway.cs'           = Versao 'Core\Servidor\BrowserGateway.cs' 'Version = "([\d.]+)"'
    'Pwa/app.js'                                = Versao 'Pwa\app.js' "VERSION = '([\d.]+)'"
    'Pwa/sw.js'                                 = Versao 'Pwa\sw.js' "VERSION = '([\d.]+)'"
}
$unicas = $versoes.Values | Select-Object -Unique
if (@($unicas).Count -ne 1) { throw "Versões diferentes: $(($versoes.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '; ')" }
$versao = @($unicas)[0]
if (-not $Saida) { $Saida = Join-Path (Split-Path $fonte -Parent) 'Pacotes' }
$pacote = Join-Path $Saida "pacote-$versao"
if (Test-Path $pacote) { throw "Já existe $pacote. Suba a versão ou remova a pasta antes." }
if (-not (Test-Path $Notas)) { throw "Arquivo de notas não encontrado: $Notas" }
Write-Host "Gerando pacote $versao em $pacote"

$publicar = '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-v', 'q', '-nologo'
dotnet publish (Join-Path $fonte 'ServerHost\GraficaCaixa.ServerHost.csproj') @publicar -o (Join-Path $pacote 'Servidor'); if ($LASTEXITCODE) { throw 'Falha ao publicar o servidor.' }
dotnet publish (Join-Path $fonte 'Wpf\GraficaCaixa.Wpf.csproj') @publicar -o (Join-Path $pacote 'Programa'); if ($LASTEXITCODE) { throw 'Falha ao publicar o programa.' }
Get-ChildItem $pacote -Recurse -Filter '*.pdb' | Remove-Item -Force

robocopy $fonte (Join-Path $pacote 'Codigo-fonte') /E /XD bin obj work node_modules .git /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'Falha ao copiar o código-fonte.' }
Copy-Item (Join-Path $PSScriptRoot 'Pacote\*') $pacote
Copy-Item $Notas (Join-Path $pacote 'O_QUE_MUDOU.txt')
[IO.File]::WriteAllText((Join-Path $pacote 'versao.txt'), $versao, (New-Object System.Text.UTF8Encoding($false)))

foreach ($exe in 'Programa\Caixa da Grafica.exe', 'Servidor\GraficaCaixaServer.exe') {
    $v = ((Get-Item (Join-Path $pacote $exe)).VersionInfo.ProductVersion -split '\+')[0]
    if ($v -ne $versao) { throw "$exe saiu com a versão $v (esperado $versao)." }
}

$linhas = Get-ChildItem $pacote -Recurse -File | Where-Object { $_.Name -notin 'pacote.sha256', 'resultado.txt' -and $_.Name -notlike 'instalacao-*.log' } | ForEach-Object {
    "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash)  $($_.FullName.Substring($pacote.Length + 1))"
}
[IO.File]::WriteAllLines((Join-Path $pacote 'pacote.sha256'), $linhas, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Pronto: $pacote ($(@($linhas).Count) arquivos conferidos em pacote.sha256)"
