# Roda toda a bateria de testes do ERP e mostra um resumo. Saída 0 = tudo passou.
# Uso:  powershell -ExecutionPolicy Bypass -File Rodar_Testes.ps1 [-Filtro customer]
param([string]$Filtro = '', [int]$LimiteSegundos = 240)
$ErrorActionPreference = 'Continue'
$fonte = $PSScriptRoot
$temp = Join-Path $env:TEMP ("erp-testes-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $temp | Out-Null

function Rodar([string]$nome, [string[]]$argumentos) {
    if ($Filtro -and $nome -notlike "*$Filtro*") { return }
    $saida = Join-Path $temp ('saida-' + ($nome -replace '[^a-zA-Z0-9]', '_') + '.txt')
    $inicio = Get-Date
    $processo = Start-Process dotnet -ArgumentList $argumentos -WorkingDirectory $fonte -NoNewWindow -PassThru -RedirectStandardOutput $saida -RedirectStandardError "$saida.err"
    $handle = $processo.Handle
    if (-not $processo.WaitForExit($LimiteSegundos * 1000)) { $processo.Kill(); $codigo = 'tempo esgotado' } else { $processo.WaitForExit(); $codigo = $processo.ExitCode }
    $texto = @(Get-Content $saida -ErrorAction SilentlyContinue) + @(Get-Content "$saida.err" -ErrorAction SilentlyContinue)
    $falha = $texto | Where-Object { $_ -cmatch 'FALHOU|Unhandled exception|^FAIL' } | Select-Object -First 1
    $passou = ($codigo -eq 0) -and -not $falha
    $script:resultados += [pscustomobject]@{ Teste = $nome; Resultado = $(if ($passou) { 'ok' } else { 'FALHOU' }); Segundos = [int]((Get-Date) - $inicio).TotalSeconds; Detalhe = $(if ($passou) { '' } else { "$codigo $falha" }) }
    Write-Host ("{0,-28} {1}" -f $nome, $(if ($passou) { 'ok' } else { "FALHOU  $codigo $falha" }))
}

$resultados = @()
foreach ($projeto in 'ServerHost\GraficaCaixa.ServerHost.csproj', 'Wpf\GraficaCaixa.Wpf.csproj', 'IntegrityTests\IntegrityTests.csproj', 'WpfAsyncTests\WpfAsyncTests.csproj', 'WebApiTests\WebApiTests.csproj', 'HotfixTests\HotfixTests.csproj', 'ConsolidatedHotfixTests\ConsolidatedHotfixTests.csproj', 'WpfPerformanceTests\WpfPerformanceTests.csproj') {
    dotnet build (Join-Path $fonte $projeto) -v q --nologo | Out-Null
    if ($LASTEXITCODE) { Write-Host "Falha no build de $projeto"; exit 1 }
}

$integridade = 'IntegrityTests\IntegrityTests.csproj'
Rodar 'integridade' @('run', '--no-build', '--project', $integridade)
foreach ($suite in 'abc', 'audit-fast', 'cash-sale-link', 'backup-files', 'boot', 'budget-cost', 'catalog', 'cash-adjust', 'indicators', 'cert-renewal', 'customer-purge', 'data-directory', 'debtors', 'discount-excess', 'documents', 'expenses', 'fiscal', 'forecast', 'gateway-writes', 'hotfix-2026-9-27-2', 'lock-queue', 'network', 'order-tabs', 'owner-pin', 'payment-date', 'phase1-close', 'phase1-focus', 'price-calc', 'product-photos', 'products-costs', 'prompt10', 'prompt6', 'prompt7', 'quantity-pricing', 'read-stress', 'reconciliation', 'recovery', 'refund', 'reminder-search', 'report-totals', 'roles', 'seasonal', 'small-round', 'stages', 'suppliers', 'today-queue', 'tracking', 'ux-point-adjustments', 'visual-refresh', 'whatsapp') {
    Rodar $suite @('run', '--no-build', '--project', $integridade, '--', "--$suite")
}
$qr = Join-Path $temp 'qr.json'
Rodar 'qr' @('run', '--no-build', '--project', $integridade, '--', '--qr', $qr)
Rodar 'wpf-async' @('run', '--no-build', '--project', 'WpfAsyncTests\WpfAsyncTests.csproj')
$api = Join-Path $temp 'api'; New-Item -ItemType Directory -Path $api | Out-Null
Rodar 'web-api' @('run', '--no-build', '--project', 'WebApiTests\WebApiTests.csproj', '--', $api, 'ServerHost\bin\Debug\net10.0-windows\win-x64\GraficaCaixaServer.exe')
$hotfix = Join-Path $temp 'hotfix'; New-Item -ItemType Directory -Path $hotfix | Out-Null
Rodar 'hotfix' @('run', '--no-build', '--project', 'HotfixTests\HotfixTests.csproj', '--', $hotfix)
Rodar 'hotfix-consolidado' @('run', '--no-build', '--project', 'ConsolidatedHotfixTests\ConsolidatedHotfixTests.csproj', '--', (Join-Path $temp 'consolidado'))
Rodar 'desempenho-wpf' @('run', '--no-build', '--project', 'WpfPerformanceTests\WpfPerformanceTests.csproj', '--', (Join-Path $temp 'desempenho'))

$falhas = @($resultados | Where-Object { $_.Resultado -ne 'ok' })
Write-Host ''
Write-Host ("{0} de {1} testes passaram." -f ($resultados.Count - $falhas.Count), $resultados.Count)
if ($falhas.Count) { $falhas | Format-Table Teste, Detalhe -AutoSize -Wrap | Out-String | Write-Host; Write-Host "Saídas em $temp" } else { Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue }
exit $falhas.Count
