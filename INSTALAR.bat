@echo off
chcp 65001 >nul
echo Instalando o Transcritor. Isso pode levar de 10 a 20 minutos (downloads grandes).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0instalar.ps1"
echo.
echo Pronto. Pode fechar esta janela.
pause
