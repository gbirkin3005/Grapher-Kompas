<#
.SYNOPSIS
  Регистрирует библиотеку КОМПАС для текущего пользователя. Права администратора не нужны.

.DESCRIPTION
  Записывает в ветку реестра HKCU\Software\Classes те же сведения, которые RegAsm /codebase
  записывает в HKLM, и раздел Kompas_Library, по которому КОМПАС узнаёт свои приложения.
  Регистрация действует только для текущей учётной записи.

  После регистрации в КОМПАС-3D: Приложения → Добавить приложения… → вкладка ActiveX →
  выберите GrapherKompas.Library и нажмите «Открыть».

.PARAMETER Unregister
  Удалить регистрацию.

.PARAMETER Path
  Путь к GrapherKompas.Library.dll. По умолчанию — dist\GrapherKompas.Library.

.PARAMETER Quiet
  Не выводить сообщения: так скрипт вызывает установщик (installer\setup.ps1).
#>
param(
    [switch]$Unregister,
    [string]$Path,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

$clsid = '{25A52761-85E0-453D-BA06-C9AF2727E4D9}'
$progId = 'GrapherKompas.Library'
$className = 'Grapher.KompasLibrary.GrapherLibrary'
$classes = 'HKCU:\Software\Classes'

if ($Unregister) {
    foreach ($key in "$classes\CLSID\$clsid", "$classes\$progId") {
        if (Test-Path $key) {
            Remove-Item $key -Recurse
            if (-not $Quiet) { Write-Host "Удалён раздел $key" }
        }
    }
    if (-not $Quiet) {
        Write-Host 'Регистрация отменена. Если библиотека была подключена, отключите её в КОМПАС: Приложения → Конфигуратор.'
    }
    return
}

if (-not $Path) {
    $Path = Join-Path (Split-Path $PSScriptRoot -Parent) 'dist\GrapherKompas.Library\GrapherKompas.Library.dll'
}
if (-not (Test-Path $Path)) {
    throw "Не найден файл $Path. Сначала соберите проект: build.cmd."
}

$dll = (Resolve-Path $Path).Path
$assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($dll)
$codeBase = ([System.Uri]$dll).AbsoluteUri
$mscoree = Join-Path ([Environment]::GetFolderPath('System')) 'mscoree.dll'

# New-Item -Force пересоздаёт существующий раздел и стирает его значения,
# поэтому раздел создаётся, только если его ещё нет.
function Ensure-Key($key) {
    if (-not (Test-Path -LiteralPath $key)) { New-Item -Path $key -Force | Out-Null }
}

function Set-Default($key, $value) {
    Ensure-Key $key
    Set-Item -LiteralPath $key -Value $value
}

function Set-ServerValues($key) {
    Ensure-Key $key
    Set-ItemProperty -Path $key -Name 'Class' -Value $className
    Set-ItemProperty -Path $key -Name 'Assembly' -Value $assemblyName.FullName
    Set-ItemProperty -Path $key -Name 'RuntimeVersion' -Value 'v4.0.30319'
    Set-ItemProperty -Path $key -Name 'CodeBase' -Value $codeBase
}

$classKey = "$classes\CLSID\$clsid"
Set-Default "$classes\$progId" $className
Set-Default "$classes\$progId\CLSID" $clsid

Set-Default $classKey $className
Set-Default "$classKey\InprocServer32" $mscoree
Set-ItemProperty -Path "$classKey\InprocServer32" -Name 'ThreadingModel' -Value 'Both'
Set-ServerValues "$classKey\InprocServer32"
Set-ServerValues "$classKey\InprocServer32\$($assemblyName.Version)"
Set-Default "$classKey\ProgId" $progId

# Категория «компоненты .NET» и признак приложения КОМПАС.
Ensure-Key "$classKey\Implemented Categories\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}"
Ensure-Key "$classKey\Kompas_Library"

if ($Quiet) { return }

Write-Host "Библиотека зарегистрирована для пользователя $env:USERNAME."
Write-Host "Файл: $dll"
Write-Host 'Теперь в КОМПАС-3D: Приложения → Добавить приложения… → вкладка ActiveX → GrapherKompas.Library → «Открыть».'
Write-Host 'После этого в меню «Приложения» появится пункт «Grapher».'
