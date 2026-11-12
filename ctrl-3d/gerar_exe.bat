@echo off
REM Gera o "CTRL 3D.exe" na pasta principal. Detalhes em gerar_exe.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0gerar_exe.ps1"
pause
