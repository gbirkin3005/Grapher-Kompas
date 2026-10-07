@echo off
rem Отмена регистрации библиотеки КОМПАС. Запускать от имени администратора.
chcp 65001 >nul
setlocal

set "DLL=%~dp0..\dist\GrapherKompas.Library\GrapherKompas.Library.dll"
set "REGASM=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"

if not exist "%DLL%" goto nodll

net session >nul 2>&1
if errorlevel 1 goto noadmin

"%REGASM%" /unregister /nologo "%DLL%"
echo.
echo Регистрация библиотеки отменена. Удалите её из списка приложений в конфигураторе КОМПАС.
pause
exit /b 0

:nodll
echo Не найден файл %DLL%
pause
exit /b 1

:noadmin
echo Нужны права администратора: щёлкните по файлу правой кнопкой и выберите
echo "Запуск от имени администратора".
pause
exit /b 1
