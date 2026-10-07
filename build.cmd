@echo off
rem Сборка: тесты, затем готовые к запуску приложение и библиотека КОМПАС в папке dist.
chcp 65001 >nul
setlocal
cd /d "%~dp0"

rem Interop-сборки КОМПАС в репозитории не хранятся: при первой сборке они берутся из SDK КОМПАС.
if exist lib\kompas\KompasAPI7.dll goto build
echo === Interop-сборки КОМПАС ===
powershell -NoProfile -ExecutionPolicy Bypass -File tools\get-kompas-interop.ps1
if errorlevel 1 goto error
echo.

:build
echo === Тесты ядра ===
dotnet test tests\Grapher.Core.Tests\Grapher.Core.Tests.csproj -c Release --nologo
if errorlevel 1 goto error

echo.
echo === Приложение ===
dotnet publish src\Grapher.App\Grapher.App.csproj -c Release -o dist\Grapher --nologo
if errorlevel 1 goto error

echo.
echo === Библиотека КОМПАС ===
dotnet publish src\Grapher.KompasLibrary\Grapher.KompasLibrary.csproj -c Release -o dist\GrapherKompas.Library --nologo
if errorlevel 1 goto error

echo.
echo Готово.
echo   Приложение:        dist\Grapher\Grapher.exe
echo   Библиотека КОМПАС: dist\GrapherKompas.Library\GrapherKompas.Library.dll
exit /b 0

:error
echo.
echo Сборка завершилась с ошибкой.
exit /b 1
