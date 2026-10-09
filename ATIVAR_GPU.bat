@echo off
chcp 65001 >nul
echo Ativando a placa NVIDIA no Transcritor. Baixa ~2,5 GB, pode levar uns 15 minutos.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ativar_gpu.ps1"
echo.
pause
