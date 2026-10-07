@echo off
chcp 65001 >nul
rem Удаление Grapher: регистрация библиотеки КОМПАС, ярлыки и папка программы.

if not exist "%~dp0setup\setup.ps1" goto nosetup

set "PS=powershell"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "PS=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"

rem Скрипт удаляет и этот файл, поэтому всё, что идёт после него, записано в одной строке:
rem командный файл больше не читается с диска. (goto) завершает его без сообщения об ошибке.
cd /d "%TEMP%"
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup\setup.ps1" -Uninstall %* & echo. & pause & (goto) 2>nul

:nosetup
echo Не найден файл setup\setup.ps1 рядом с uninstall.cmd.
echo.
pause
exit /b 1
