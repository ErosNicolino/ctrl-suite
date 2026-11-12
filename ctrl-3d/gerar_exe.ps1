# Gera o programa "CTRL 3D.exe" na pasta principal (um nível acima de Codigo-fonte).
# Uso: clique duas vezes em gerar_exe.bat (ou rode este arquivo no PowerShell).
# Requisitos (uma vez): pip install pywebview openpyxl pillow pyinstaller

$ErrorActionPreference = "Stop"
$fonte = $PSScriptRoot
$raiz  = Split-Path $fonte -Parent
$tmp   = Join-Path $env:TEMP "ctrl3d_build"

Write-Host "Fechando o programa, se estiver aberto..."
Get-Process -Name "CTRL 3D" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "Gerando o programa (leva 1 a 2 minutos)..."
if (Test-Path $tmp) { [IO.Directory]::Delete($tmp, $true) }   # .NET: aceita o caminho curto do TEMP
python -m PyInstaller --noconfirm --onedir --windowed --name "CTRL 3D" `
    --icon "$fonte\assets\icon.ico" --add-data "$fonte\ui;ui" `
    --collect-all webview --exclude-module customtkinter --exclude-module tkinterdnd2 `
    --distpath "$tmp\dist" --workpath "$tmp\build" --specpath "$tmp" "$fonte\app.py"
if ($LASTEXITCODE -ne 0) { throw "O PyInstaller falhou." }

Write-Host "Copiando para $raiz ..."
$interno = Join-Path $raiz "_internal"
if (Test-Path $interno) { (Get-Item $interno -Force).Attributes = "Directory"; Remove-Item -Recurse -Force $interno }
Copy-Item "$tmp\dist\CTRL 3D\*" $raiz -Recurse -Force
(Get-Item $interno -Force).Attributes = "Directory, Hidden"     # arquivos internos ficam escondidos
[IO.Directory]::Delete($tmp, $true)

Write-Host ""
Write-Host "Pronto! Abra: $raiz\CTRL 3D.exe" -ForegroundColor Green
