@echo off
rem Botao "Atualizar ERP": crie um atalho deste arquivo na area de trabalho do PC1. Nada e agendado.
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%~dp0Atualizar_ERP.ps1"
