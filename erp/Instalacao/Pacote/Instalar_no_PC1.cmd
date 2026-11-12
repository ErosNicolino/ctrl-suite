@echo off
setlocal EnableExtensions
chcp 65001 >nul
rem ============================================================================
rem  Instala a versao do ERP que esta neste pacote (versao.txt). Executar NO PC1, como administrador.
rem  O pacote fica em <ERP>\Atualizacoes\pacote-<versao>\ (ou em <ERP>\Backups\) junto com:
rem    Programa\  Servidor\  Codigo-fonte\  versao.txt
rem  Uso manual: botao direito > Executar como administrador (faz perguntas).
rem  Uso pelo botao "Atualizar ERP": Instalar_no_PC1.cmd /auto  (sem perguntas; resultado em resultado.txt).
rem  Nao mexe em Configuracao, Backups, Recibos, Fechamentos, Impressoes, Orcamentos, Produtos
rem  nem na pasta .git do codigo-fonte. Faz backup do programa, do codigo e do banco antes de trocar.
rem ============================================================================
set "PACOTE=%~dp0"
set "PACOTE=%PACOTE:~0,-1%"
set "AUTO="
if /I "%~1"=="/auto" set "AUTO=1"
set "PAUSA=pause"
if defined AUTO set "PAUSA=type nul"
if not exist "%PACOTE%\versao.txt" (echo Pacote sem versao.txt.& %PAUSA% & exit /b 1)
set /p VERSAO=<"%PACOTE%\versao.txt"
for %%i in ("%PACOTE%\..\..") do set "ERP=%%~fi"
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd-HHmmss"') do set "STAMP=%%i"
set "BACKUP=%ERP%\Backups\antes-da-%VERSAO%-%STAMP%"
set "LOG=%PACOTE%\instalacao-%STAMP%.log"
set "RESULTADO=%PACOTE%\resultado.txt"
if exist "%RESULTADO%" del /q "%RESULTADO%"

net session >nul 2>&1
if errorlevel 1 (echo Clique com o botao direito neste arquivo e escolha "Executar como administrador".& call :falha "sem permissao de administrador" & exit /b 1)
sc query GraficaCaixaServer >nul 2>&1
if errorlevel 1 (echo Este computador nao tem o servico GraficaCaixaServer. Rode este arquivo no PC1.& call :falha "servico nao encontrado" & exit /b 1)
if not exist "%ERP%\Programa\Caixa da Grafica.exe" (echo Nao encontrei o ERP em "%ERP%". Coloque a pasta do pacote em "ERP\Atualizacoes\" e rode de la.& call :falha "ERP nao encontrado" & exit /b 1)
sc qc GraficaCaixaServer | find /I "%ERP%\Programa\Servidor\GraficaCaixaServer.exe" >nul
if errorlevel 1 (echo Esta pasta NAO esta dentro do ERP que o servico usa ^("%ERP%"^). Nada foi alterado.& call :falha "pacote fora da pasta do ERP" & exit /b 1)
if not exist "%PACOTE%\Programa\Caixa da Grafica.exe" (echo Pacote incompleto em "%PACOTE%".& call :falha "pacote incompleto" & exit /b 1)
if not exist "%PACOTE%\Servidor\GraficaCaixaServer.exe" (echo Pacote incompleto em "%PACOTE%".& call :falha "pacote incompleto" & exit /b 1)

echo ERP que sera atualizado: %ERP%  ^>  versao %VERSAO%
if defined AUTO goto confirmado
echo.
echo Antes de continuar:
echo   1) Feche o "Caixa da Grafica" em TODOS os computadores.
echo   2) Garanta que nao ha lancamento ou pedido sendo salvo neste momento.
choice /M "Tudo fechado, posso atualizar"
if errorlevel 2 (echo Cancelado. Nada foi alterado.& call :falha "cancelado" & exit /b 1)
:confirmado
taskkill /IM "Caixa da Grafica.exe" /F >nul 2>&1

echo [1/6] Backup do programa e do codigo em "%BACKUP%" ...
robocopy "%ERP%\Programa" "%BACKUP%\Programa" /E /R:1 /W:1 /LOG+:"%LOG%" /NP >nul
if errorlevel 8 goto erro_backup
robocopy "%ERP%\Codigo-fonte" "%BACKUP%\Codigo-fonte" /E /XD bin obj /R:1 /W:1 /LOG+:"%LOG%" /NP >nul
if errorlevel 8 goto erro_backup

echo [2/6] Parando o servidor ...
net stop GraficaCaixaServer >nul 2>&1
timeout /t 4 /nobreak >nul

echo [3/6] Backup do banco de dados (com o servidor parado) ...
robocopy "%ERP%\Dados" "%BACKUP%\Dados" caixa_dados.db caixa_dados.db-wal caixa_dados.db-shm /R:2 /W:2 /LOG+:"%LOG%" /NP >nul
if errorlevel 8 goto erro_backup_banco

echo [4/6] Atualizando programa e servidor ...
robocopy "%PACOTE%\Programa" "%ERP%\Programa" /MIR /XD Servidor /R:5 /W:2 /LOG+:"%LOG%" /NP >nul
if errorlevel 8 goto voltar
robocopy "%PACOTE%\Servidor" "%ERP%\Programa\Servidor" /MIR /R:5 /W:2 /LOG+:"%LOG%" /NP >nul
if errorlevel 8 goto voltar

echo [5/6] Ligando o servidor ...
set /a TENTATIVA=0
:ligar
net start GraficaCaixaServer >nul 2>&1
timeout /t 6 /nobreak >nul
sc query GraficaCaixaServer | find "RUNNING" >nul
if not errorlevel 1 goto ligou
set /a TENTATIVA+=1
if %TENTATIVA% GEQ 5 goto voltar
goto ligar
:ligou

echo [6/6] Atualizando o codigo-fonte ...
robocopy "%PACOTE%\Codigo-fonte" "%ERP%\Codigo-fonte" /MIR /XD bin obj .git /R:1 /W:1 /LOG+:"%LOG%" /NP >nul
if errorlevel 8 echo   Aviso: codigo-fonte nao foi copiado por completo (o programa ja esta atualizado). Backup em "%BACKUP%\Codigo-fonte".
rem Limpeza: guarda so os 2 backups "antes-da" mais novos e, alem deste pacote, so o anterior (cada um ~340 MB).
powershell -NoProfile -Command "Get-ChildItem -LiteralPath '%ERP%\Backups' -Directory -Filter 'antes-da-*' | Sort-Object CreationTime -Descending | Select-Object -Skip 2 | Remove-Item -Recurse -Force; Get-ChildItem -LiteralPath '%ERP%\Atualizacoes' -Directory -Filter 'pacote-*' | Where-Object { $_.FullName -ne '%PACOTE%' } | Sort-Object CreationTime -Descending | Select-Object -Skip 1 | Remove-Item -Recurse -Force" >>"%LOG%" 2>&1

> "%RESULTADO%" echo OK %VERSAO% instalada. Backup em: %BACKUP%
echo.
echo ===============================================================
echo  ERP %VERSAO% instalado e servidor ligado.
echo  Backup do que existia (programa, codigo e banco): %BACKUP%
echo  Agora siga o roteiro de conferencia do LEIA-ME antes de liberar para a equipe.
echo ===============================================================
%PAUSA%
exit /b 0

:erro_backup
echo ERRO ao fazer o backup. Nada foi alterado. Detalhes: "%LOG%"
call :falha "erro no backup; nada foi alterado"
exit /b 1
:erro_backup_banco
echo ERRO ao copiar o banco. Religando o servidor sem alterar nada.
net start GraficaCaixaServer >nul 2>&1
call :falha "erro ao copiar o banco; nada foi alterado"
exit /b 1
:voltar
echo ERRO durante a atualizacao. Voltando para a versao anterior do programa...
net stop GraficaCaixaServer >nul 2>&1
timeout /t 3 /nobreak >nul
robocopy "%BACKUP%\Programa" "%ERP%\Programa" /MIR /R:5 /W:2 /LOG+:"%LOG%" /NP >nul
net start GraficaCaixaServer
echo Programa anterior restaurado. Detalhes do erro: "%LOG%" e "%ERP%\Servidor\Logs\servidor.log".
call :falha "erro na troca; versao anterior restaurada (log: %LOG%)"
exit /b 1

:falha
> "%RESULTADO%" echo ERRO %~1
%PAUSA%
exit /b 0
