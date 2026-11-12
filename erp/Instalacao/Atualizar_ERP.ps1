# Botão "Atualizar ERP" (PC1). Só roda quando alguém clica: nada é agendado.
# 1) procura em <ERP>\Atualizacoes o pacote mais novo que a versão instalada;
# 2) confere a integridade (pacote.sha256: nenhum arquivo alterado ou faltando);
# 3) pergunta se pode atualizar; 4) pede a confirmação de administrador do Windows e roda o instalador sem perguntas;
# 5) mostra o resultado. Fica em <ERP>\Atualizacoes\ junto com Atualizar_ERP.cmd.
param([switch]$Conferir)  # -Conferir: só mostra no console qual pacote seria instalado e se está íntegro (não instala nada)
Add-Type -AssemblyName PresentationFramework
$ErrorActionPreference = 'Stop'
function Aviso([string]$texto, [string]$icone = 'Information') { if ($Conferir) { Write-Output "[$icone] $texto"; return }; [void][System.Windows.MessageBox]::Show($texto, 'Atualizar ERP', 'OK', $icone) }

try {
    $pasta = $PSScriptRoot
    $erp = Split-Path $pasta -Parent
    $exe = Join-Path $erp 'Programa\Caixa da Grafica.exe'
    if (-not (Test-Path $exe)) { Aviso "Não encontrei o ERP em:`n$erp`n`nEste botão deve ficar em <ERP>\Atualizacoes." 'Error'; exit 1 }
    $instalada = [version](((Get-Item $exe).VersionInfo.ProductVersion -split '\+')[0])

    $pacotes = Get-ChildItem $pasta -Directory -Filter 'pacote-*' | ForEach-Object {
        $arquivo = Join-Path $_.FullName 'versao.txt'
        if (Test-Path $arquivo) { [pscustomobject]@{ Pasta = $_.FullName; Versao = [version]((Get-Content $arquivo -TotalCount 1).Trim()) } }
    } | Where-Object { $_.Versao -gt $instalada } | Sort-Object Versao -Descending
    if (-not $pacotes) { Aviso "O ERP já está atualizado (versão $instalada).`n`nNenhum pacote mais novo em:`n$pasta"; exit 0 }
    $novo = $pacotes[0]

    # Integridade: cada arquivo listado em pacote.sha256 precisa existir e ter o mesmo código.
    $lista = Join-Path $novo.Pasta 'pacote.sha256'
    if (-not (Test-Path $lista)) { Aviso "O pacote $($novo.Versao) não tem pacote.sha256 e não será instalado." 'Error'; exit 1 }
    $erros = @()
    foreach ($linha in Get-Content $lista) {
        if ($linha -notmatch '^([0-9A-F]{64})\s+(.+)$') { continue }
        $arquivo = Join-Path $novo.Pasta $Matches[2]
        if (-not (Test-Path -LiteralPath $arquivo)) { $erros += "falta: $($Matches[2])"; continue }
        if ((Get-FileHash -LiteralPath $arquivo -Algorithm SHA256).Hash -ne $Matches[1]) { $erros += "alterado: $($Matches[2])" }
        if ($erros.Count -ge 5) { break }
    }
    if ($erros.Count -gt 0) { Aviso "O pacote $($novo.Versao) não passou na conferência e NÃO será instalado:`n`n$($erros -join "`n")" 'Error'; exit 1 }

    if ($Conferir) { Write-Output "OK: instalaria a versão $($novo.Versao) (instalada: $instalada); pacote íntegro."; exit 0 }
    $resposta = [System.Windows.MessageBox]::Show("Atualizar o ERP da versão $instalada para $($novo.Versao)?`n`nAntes de confirmar:`n• feche o Caixa da Gráfica em TODOS os computadores;`n• garanta que ninguém está salvando lançamento ou pedido.`n`nO sistema faz backup do programa e do banco antes de trocar e volta a versão anterior sozinho se algo falhar. Leva de 2 a 5 minutos.", 'Atualizar ERP', 'YesNo', 'Question')
    if ($resposta -ne 'Yes') { exit 0 }

    $instalador = Join-Path $novo.Pasta 'Instalar_no_PC1.cmd'
    $resultado = Join-Path $novo.Pasta 'resultado.txt'
    if (Test-Path $resultado) { Remove-Item $resultado -Force }
    try { Start-Process -FilePath 'cmd.exe' -ArgumentList "/c `"`"$instalador`" /auto`"" -Verb RunAs -Wait }
    catch { Aviso "A atualização foi cancelada (a confirmação de administrador não foi dada). Nada foi alterado." 'Warning'; exit 1 }

    $texto = if (Test-Path $resultado) { (Get-Content $resultado -Raw).Trim() } else { 'ERRO o instalador não informou o resultado' }
    if ($texto.StartsWith('OK')) {
        Aviso "ERP atualizado para a versão $($novo.Versao).`n`n$texto`n`nConfira o roteiro do LEIA-ME e atualize os outros computadores."
        $leia = Join-Path $novo.Pasta 'LEIA-ME.txt'; if (Test-Path $leia) { Start-Process notepad.exe $leia }
    } else {
        Aviso "A atualização NÃO foi concluída.`n`n$texto" 'Error'
    }
}
catch { Aviso "Erro inesperado: $($_.Exception.Message)" 'Error'; exit 1 }
