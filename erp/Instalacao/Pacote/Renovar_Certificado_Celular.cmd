@echo off
rem Renova na mao o certificado do acesso pelo celular e do link de acompanhamento.
rem So e preciso se o aviso do Inicio continuar: o servidor ja tenta renovar sozinho nos ultimos 30 dias.
setlocal
if not defined PASTA set "PASTA=D:\Grafica CA CP\ERP\Configuracao\BrowserTls"
set "NOME=pc1.exemplo.ts.net"
set "TS=%ProgramFiles%\Tailscale\tailscale.exe"
if not exist "%TS%" (
  echo O Tailscale nao foi encontrado neste computador.
  pause
  exit /b 1
)
"%TS%" cert --min-validity 720h --cert-file "%PASTA%\browser.crt.novo" --key-file "%PASTA%\browser.key.novo" %NOME%
if errorlevel 1 goto falhou
if not exist "%PASTA%\browser.crt.novo" goto falhou
if not exist "%PASTA%\browser.key.novo" goto falhou
move /y "%PASTA%\browser.key.novo" "%PASTA%\browser.key" >nul
if errorlevel 1 goto falhou
move /y "%PASTA%\browser.crt.novo" "%PASTA%\browser.crt" >nul
if errorlevel 1 goto falhou
echo.
echo Certificado renovado. O celular volta em ate 5 minutos.
echo Se ja estava vencido, execute tambem Reiniciar_Servidor_ERP.cmd.
pause
exit /b 0
:falhou
del /q "%PASTA%\browser.crt.novo" "%PASTA%\browser.key.novo" 2>nul
echo.
echo Nao foi possivel renovar. O certificado atual nao foi alterado.
pause
exit /b 1
