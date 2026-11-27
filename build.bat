@echo off
setlocal
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo [ERRO] .NET 8 SDK nao encontrado.
  echo Instale o SDK .NET 8 e execute este arquivo novamente.
  echo https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1"
if errorlevel 1 (
  echo [ERRO] Falha ao gerar o executavel. Veja as mensagens acima.
  pause
  exit /b 1
)
echo.
echo [OK] Executavel gerado em: %~dp0dist\ynoez.exe
echo Para instalar em C:\ynoez execute scripts\install.ps1 como administrador.
pause
