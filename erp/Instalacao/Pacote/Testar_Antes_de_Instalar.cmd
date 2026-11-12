@echo off
setlocal EnableExtensions
chcp 65001 >nul
rem ============================================================================
rem  TESTE SEGURO da versao nova, SEM instalar e SEM tocar no ERP de verdade.
rem  Abre o programa novo com uma COPIA de um backup do banco, numa pasta temporaria:
rem    - nao usa o servico GraficaCaixaServer, nao lê nem grava em ERP\Dados;
rem    - o titulo da janela mostra [TESTE];
rem    - a conversao das fases roda na copia (da para conferir antes/depois).
rem  Pode rodar em qualquer PC (nao precisa ser administrador).
rem  Uso: duplo clique  -> usa o backup automatico mais recente em <ERP>\Dados\Backups
rem       ou:  Testar_Antes_de_Instalar.cmd "C:\caminho\caixa_xxx.db"
rem ============================================================================
set "PACOTE=%~dp0"
set "PACOTE=%PACOTE:~0,-1%"
for %%i in ("%PACOTE%\..\..") do set "ERP=%%~fi"
set "ORIGEM=%~1"
if "%ORIGEM%"=="" for /f "usebackq delims=" %%f in (`powershell -NoProfile -Command "Get-ChildItem -LiteralPath '%ERP%\Dados\Backups' -Filter 'caixa_*.db' -ErrorAction SilentlyContinue | Sort LastWriteTime -Descending | Select -First 1 -ExpandProperty FullName"`) do set "ORIGEM=%%f"
if "%ORIGEM%"=="" (echo Nao achei um backup do banco. Informe o caminho: Testar_Antes_de_Instalar.cmd "caminho\do\backup.db"& pause & exit /b 1)
if not exist "%ORIGEM%" (echo Arquivo nao encontrado: "%ORIGEM%"& pause & exit /b 1)
if not exist "%PACOTE%\Programa\Caixa da Grafica.exe" (echo Pacote incompleto: falta Programa\Caixa da Grafica.exe& pause & exit /b 1)

set /p VERSAO=<"%PACOTE%\versao.txt"
set "TESTE=%TEMP%\ERP_TESTE_%VERSAO%"
if exist "%TESTE%" rd /s /q "%TESTE%"
mkdir "%TESTE%\Dados" "%TESTE%\Perfil" "%TESTE%\Relatorios" >nul 2>&1
copy /Y "%ORIGEM%" "%TESTE%\Dados\caixa_dados.db" >nul
if errorlevel 1 (echo Nao consegui copiar o backup.& pause & exit /b 1)
> "%TESTE%\Perfil\modo.txt" <nul set /p "=0"
> "%TESTE%\Perfil\pasta.txt" <nul set /p "=%TESTE%\Dados"
> "%TESTE%\Perfil\pasta_relatorios.txt" <nul set /p "=%TESTE%\Relatorios"

echo Backup usado: %ORIGEM%
echo Pasta do teste: %TESTE%
echo.
echo O programa vai abrir em MODO TESTE. Entre com o SEU usuario e senha de sempre
echo (a copia tem os mesmos usuarios). Siga o roteiro de conferencia do LEIA-ME.
echo Tudo o que voce fizer ali vale so para a copia e some quando a pasta de teste for apagada.
set "GRAFICA_CAIXA_PERFIL=%TESTE%\Perfil"
start "" "%PACOTE%\Programa\Caixa da Grafica.exe"
echo.
echo Quando terminar, feche o programa. Para apagar o teste: exclua a pasta "%TESTE%".
pause
exit /b 0
