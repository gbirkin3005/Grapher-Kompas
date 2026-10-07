@echo off
rem Отмена регистрации библиотеки КОМПАС для текущего пользователя.
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0register-library-user.ps1" -Unregister
echo.
pause
