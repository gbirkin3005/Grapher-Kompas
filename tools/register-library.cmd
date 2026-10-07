@echo off
rem Регистрация библиотеки КОМПАС для текущего пользователя. Права администратора не нужны.
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0register-library-user.ps1"
echo.
pause
