@echo off
rem Релизный пакет: тесты, сборка и архив dist\release\Grapher-<версия>.zip.
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\make-release.ps1" %*
if errorlevel 1 goto error
exit /b 0

:error
echo.
echo Сборка релиза завершилась с ошибкой.
exit /b 1
