@echo off
chcp 65001 >nul
rem Установка Grapher для текущего пользователя. Права администратора не нужны.
setlocal

if not exist "%~dp0Grapher.exe" goto notunpacked
if not exist "%~dp0setup\setup.ps1" goto notunpacked

rem Из 32-разрядной программы (например, файлового менеджера) нужен 64-разрядный PowerShell:
rem иначе библиотека зарегистрируется в ветке реестра, которую КОМПАС не читает.
set "PS=powershell"
if exist "%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe" set "PS=%SystemRoot%\Sysnative\WindowsPowerShell\v1.0\powershell.exe"

"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup\setup.ps1" %*
set "RESULT=%errorlevel%"
echo.
pause
exit /b %RESULT%

:notunpacked
echo Рядом с этим файлом нет программы Grapher.
echo Похоже, архив не распакован. Щёлкните по архиву правой кнопкой мыши,
echo выберите "Извлечь всё...", а затем запустите install.cmd из распакованной папки.
echo.
pause
exit /b 1
