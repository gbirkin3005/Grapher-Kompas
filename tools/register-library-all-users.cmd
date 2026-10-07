@echo off
rem Регистрация библиотеки КОМПАС для всех пользователей компьютера (RegAsm).
rem Запускать от имени администратора. Обычно достаточно register-library.cmd (без прав администратора).
chcp 65001 >nul
setlocal

set "DLL=%~dp0..\dist\GrapherKompas.Library\GrapherKompas.Library.dll"
set "REGASM=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

if not exist "%DLL%" goto nodll
if not exist "%REGASM%" goto noregasm

net session >nul 2>&1
if errorlevel 1 goto noadmin

"%REGASM%" /codebase /nologo "%DLL%"
if errorlevel 1 goto failed

echo.
echo Библиотека зарегистрирована.
echo Теперь в КОМПАС-3D: Приложения - Добавить приложения... - вкладка ActiveX -
echo GrapherKompas.Library - Открыть.
echo После этого в меню "Приложения" появится пункт "Grapher".
pause
exit /b 0

:nodll
echo Не найден файл %DLL%
echo Сначала соберите проект: запустите build.cmd в корне репозитория.
pause
exit /b 1

:noregasm
echo Не найден %REGASM% - установите .NET Framework 4.8.
pause
exit /b 1

:noadmin
echo Нужны права администратора: щёлкните по файлу правой кнопкой и выберите
echo "Запуск от имени администратора".
pause
exit /b 1

:failed
echo Регистрация не удалась.
pause
exit /b 1
