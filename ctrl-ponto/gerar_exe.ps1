# Gera o programa "CTRL Ponto.exe" na pasta principal (um nível acima de Codigo-fonte).
# Uso: clique duas vezes em gerar_exe.bat (ou rode este arquivo no PowerShell).
# Requisitos (uma vez): pip install pywebview pillow pystray pyinstaller

$ErrorActionPreference = "Stop"
$fonte = $PSScriptRoot
$raiz  = Split-Path $fonte -Parent
$tmp   = Join-Path $env:TEMP "ctrlponto_build"

Write-Host "Fechando o programa, se estiver aberto..."
Get-Process -Name "CTRL Ponto" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "Gerando o programa (leva 1 a 2 minutos)..."
if (Test-Path $tmp) { [IO.Directory]::Delete($tmp, $true) }   # .NET: aceita o caminho curto do TEMP
python -m PyInstaller --noconfirm --onedir --windowed --name "CTRL Ponto" `
    --icon "$fonte\assets\icon.ico" --add-data "$fonte\ui;ui" --add-data "$fonte\assets;assets" `
    --collect-all webview --hidden-import pystray._win32 --exclude-module customtkinter --exclude-module tkinterdnd2 --exclude-module openpyxl `
    --distpath "$tmp\dist" --workpath "$tmp\build" --specpath "$tmp" "$fonte\app.py"
if ($LASTEXITCODE -ne 0) { throw "O PyInstaller falhou." }

Write-Host "Copiando para $raiz ..."
$interno = Join-Path $raiz "_internal"
if (Test-Path $interno) { (Get-Item $interno -Force).Attributes = "Directory"; Remove-Item -Recurse -Force $interno }
Copy-Item "$tmp\dist\CTRL Ponto\*" $raiz -Recurse -Force
(Get-Item $interno -Force).Attributes = "Directory, Hidden"     # arquivos internos ficam escondidos
[IO.Directory]::Delete($tmp, $true)

Write-Host ""
Write-Host "Pronto! Abra: $raiz\CTRL Ponto.exe" -ForegroundColor Green
