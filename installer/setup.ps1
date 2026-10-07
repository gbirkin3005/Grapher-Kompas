<#
.SYNOPSIS
  Устанавливает или удаляет Grapher для текущего пользователя. Права администратора не нужны.

.DESCRIPTION
  Установка:
    - копирует программу в папку %LOCALAPPDATA%\Programs\Grapher;
    - регистрирует библиотеку КОМПАС (ветка реестра HKCU\Software\Classes);
    - создаёт ярлыки в меню «Пуск» и на рабочем столе;
    - добавляет Grapher в список установленных приложений Windows.

  Удаление (-Uninstall) убирает всё перечисленное. Удаляются только те файлы, которые были
  установлены; настройки пользователя (%AppData%\Grapher) остаются.

  Скрипт запускают файлы install.cmd и uninstall.cmd из папки программы.

.PARAMETER Uninstall
  Удалить программу.

.PARAMETER InstallDir
  Папка установки. По умолчанию %LOCALAPPDATA%\Programs\Grapher.

.PARAMETER NoDesktopShortcut
  Не создавать ярлык на рабочем столе.
#>
param(
    [switch]$Uninstall,
    [string]$InstallDir,
    [switch]$NoDesktopShortcut
)

$ErrorActionPreference = 'Stop'

$exeName = 'Grapher.exe'
$libraryName = 'GrapherKompas.Library.dll'
$clsid = '{25A52761-85E0-453D-BA06-C9AF2727E4D9}'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Grapher'
$manifestName = 'setup\installed-files.txt'
$registerScript = Join-Path $PSScriptRoot 'register-library-user.ps1'
$packageDir = Split-Path $PSScriptRoot -Parent
$defaultDir = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\Grapher'
$startMenuLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'Grapher.lnk'
$desktopLink = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Grapher.lnk'

function Get-FullPath([string]$path) {
    return [IO.Path]::GetFullPath($path).TrimEnd('\')
}

function Test-SamePath([string]$a, [string]$b) {
    return [string]::Equals((Get-FullPath $a), (Get-FullPath $b), [StringComparison]::OrdinalIgnoreCase)
}

function Test-Inside([string]$path, [string]$dir) {
    return (Get-FullPath $path).StartsWith((Get-FullPath $dir) + '\', [StringComparison]::OrdinalIgnoreCase)
}

# Пока КОМПАС держит библиотеку загруженной (или открыт сам Grapher), файлы нельзя ни заменить, ни удалить.
function Assert-NotInUse([string]$dir) {
    if (-not (Test-Path -LiteralPath $dir)) { return }
    $binaries = Get-ChildItem -LiteralPath $dir -File | Where-Object { $_.Extension -eq '.dll' -or $_.Extension -eq '.exe' }
    foreach ($file in $binaries) {
        try {
            $stream = [IO.File]::Open($file.FullName, 'Open', 'ReadWrite', 'None')
            $stream.Close()
        }
        catch {
            throw "Файл $($file.Name) занят. Закройте КОМПАС-3D и Grapher и повторите."
        }
    }
}

function Assert-DotNet48 {
    $full = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue
    if (-not $full -or $full.Release -lt 528040) {
        throw ('Нужен .NET Framework 4.8. В Windows 10 (с версии 1903) и Windows 11 он уже есть; ' +
               'для более старых систем скачайте его: https://dotnet.microsoft.com/download/dotnet-framework/net48')
    }
}

function Read-Manifest([string]$dir) {
    $path = Join-Path $dir $manifestName
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    return @(Get-Content -LiteralPath $path -Encoding UTF8 | Where-Object { $_ })
}

# Путь к зарегистрированной библиотеке КОМПАС; $null, если она не зарегистрирована.
function Get-RegisteredLibrary {
    $server = Get-ItemProperty -LiteralPath "HKCU:\Software\Classes\CLSID\$clsid\InprocServer32" -ErrorAction SilentlyContinue
    if (-not $server -or -not $server.CodeBase) { return $null }
    return ([Uri]$server.CodeBase).LocalPath
}

function New-Shortcut([string]$path, [string]$exe) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $exe
    $link.WorkingDirectory = Split-Path $exe -Parent
    $link.Description = 'Grapher: графики по точкам для КОМПАС-3D'
    $link.IconLocation = "$exe,0"
    $link.Save()
}

# Ярлык удаляется, только если он ведёт в папку установки.
function Remove-Shortcut([string]$path, [string]$dir) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    $shell = New-Object -ComObject WScript.Shell
    $linkTarget = $shell.CreateShortcut($path).TargetPath
    if ($linkTarget -and (Test-Inside $linkTarget $dir)) {
        Remove-Item -LiteralPath $path -Force
    }
}

function Set-UninstallEntry([string]$dir) {
    $exe = Join-Path $dir $exeName
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    $size = (Get-ChildItem -LiteralPath $dir -Recurse -File | Measure-Object Length -Sum).Sum

    if (-not (Test-Path -LiteralPath $uninstallKey)) { New-Item -Path $uninstallKey -Force | Out-Null }
    Set-ItemProperty -LiteralPath $uninstallKey -Name 'DisplayName' -Value 'Grapher'
    Set-ItemProperty -LiteralPath $uninstallKey -Name 'DisplayVersion' -Value "$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart)"
    Set-ItemProperty -LiteralPath $uninstallKey -Name 'DisplayIcon' -Value $exe
    Set-ItemProperty -LiteralPath $uninstallKey -Name 'InstallLocation' -Value $dir
    Set-ItemProperty -LiteralPath $uninstallKey -Name 'UninstallString' -Value ('"' + (Join-Path $dir 'uninstall.cmd') + '"')
    Set-ItemProperty -LiteralPath $uninstallKey -Name 'EstimatedSize' -Value ([int]($size / 1KB)) -Type DWord
    Set-ItemProperty -LiteralPath $uninstallKey -Name 'NoModify' -Value 1 -Type DWord
    Set-ItemProperty -LiteralPath $uninstallKey -Name 'NoRepair' -Value 1 -Type DWord
}

function Install-Grapher {
    $source = Get-FullPath $packageDir
    $target = $defaultDir
    if ($InstallDir) { $target = $InstallDir }
    $target = Get-FullPath $target

    foreach ($required in $exeName, $libraryName, 'Grapher.Core.dll', 'uninstall.cmd') {
        if (-not (Test-Path -LiteralPath (Join-Path $source $required))) {
            throw "В папке $source нет файла $required. Распакуйте архив целиком и запустите install.cmd из распакованной папки."
        }
    }
    Assert-DotNet48

    $files = @(Get-ChildItem -LiteralPath $source -Recurse -File |
        ForEach-Object { $_.FullName.Substring($source.Length + 1) } |
        Where-Object { $_ -ne $manifestName })

    if (-not (Test-SamePath $source $target)) {
        if (Test-Inside $target $source) { throw 'Папка установки не может находиться внутри распакованной папки.' }
        Assert-NotInUse $target
        $previous = Read-Manifest $target

        Write-Host "Копирование в $target ..."
        foreach ($relative in $files) {
            $destination = Join-Path $target $relative
            $folder = Split-Path $destination -Parent
            if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
            Copy-Item -LiteralPath (Join-Path $source $relative) -Destination $destination -Force
        }

        # Файлы прошлой версии, которых в новой уже нет.
        foreach ($relative in $previous) {
            $stale = Join-Path $target $relative
            if (($files -notcontains $relative) -and (Test-Inside $stale $target) -and (Test-Path -LiteralPath $stale)) {
                Remove-Item -LiteralPath $stale -Force
            }
        }
    }

    Set-Content -LiteralPath (Join-Path $target $manifestName) -Value $files -Encoding UTF8

    # У файлов из скачанного архива стоит отметка «получено из Интернета». Она снимается:
    # с ней .NET Framework может отказаться загружать библиотеку в чужой процесс (КОМПАС).
    foreach ($relative in $files) {
        Unblock-File -LiteralPath (Join-Path $target $relative) -ErrorAction SilentlyContinue
    }

    & $registerScript -Path (Join-Path $target $libraryName) -Quiet

    $exe = Join-Path $target $exeName
    New-Shortcut $startMenuLink $exe
    if (-not $NoDesktopShortcut) { New-Shortcut $desktopLink $exe }
    Set-UninstallEntry $target

    Write-Host ''
    Write-Host "Grapher установлен: $target" -ForegroundColor Green
    Write-Host 'Ярлык «Grapher» добавлен в меню «Пуск» и на рабочий стол.'
    Write-Host ''
    if (Test-Path 'Registry::HKEY_CLASSES_ROOT\KOMPAS.Application.7') {
        Write-Host 'Осталось один раз подключить Grapher в КОМПАС-3D:'
        Write-Host '  1. Запустите КОМПАС-3D (если он открыт, перезапустите его).'
        Write-Host '  2. Меню «Приложения» → «Добавить приложения…».'
        Write-Host '  3. В окне «Подключить КОМПАС-Приложения» откройте вкладку «ActiveX»,'
        Write-Host '     щёлкните по строке «GrapherKompas.Library» и нажмите «Открыть».'
        Write-Host '  4. В меню «Приложения» появится пункт «Grapher» → «Построить график…».'
    }
    else {
        Write-Host 'КОМПАС-3D на этом компьютере не найден.' -ForegroundColor Yellow
        Write-Host 'Grapher установлен, но строить графики он сможет только после установки КОМПАС-3D.'
        Write-Host 'После установки КОМПАС подключите Grapher: «Приложения» → «Добавить приложения…» →'
        Write-Host 'вкладка «ActiveX» → «GrapherKompas.Library» → «Открыть».'
    }
    if (-not (Test-SamePath $source $target)) {
        Write-Host ''
        Write-Host 'Распакованную папку и архив теперь можно удалить.'
    }
}

function Uninstall-Grapher {
    $target = $null
    $entry = Get-ItemProperty -LiteralPath $uninstallKey -ErrorAction SilentlyContinue
    if ($InstallDir) { $target = $InstallDir }
    elseif ($entry -and $entry.InstallLocation) { $target = $entry.InstallLocation }
    elseif (Test-Path -LiteralPath (Join-Path $defaultDir $exeName)) { $target = $defaultDir }

    if (-not $target) {
        Write-Host 'Grapher не установлен: запись об установке не найдена.'
        return
    }
    $target = Get-FullPath $target

    # Папку нельзя удалить, пока она текущая.
    Set-Location -LiteralPath ([IO.Path]::GetTempPath())
    Assert-NotInUse $target

    $registered = Get-RegisteredLibrary
    if ($registered) {
        if (Test-Inside $registered $target) {
            & $registerScript -Unregister -Quiet
            Write-Host 'Регистрация библиотеки КОМПАС удалена.'
        }
        else {
            Write-Host "Библиотека КОМПАС зарегистрирована из другой папки ($registered) - её регистрация не изменена."
        }
    }

    Remove-Shortcut $startMenuLink $target
    Remove-Shortcut $desktopLink $target
    if (Test-Path -LiteralPath $uninstallKey) { Remove-Item -LiteralPath $uninstallKey -Recurse -Force }

    $installed = Read-Manifest $target
    if ($installed.Count -eq 0) {
        Write-Host "Список установленных файлов не найден. Папку $target удалите вручную." -ForegroundColor Yellow
    }
    else {
        foreach ($relative in $installed) {
            $file = Join-Path $target $relative
            if ((Test-Inside $file $target) -and (Test-Path -LiteralPath $file)) { Remove-Item -LiteralPath $file -Force }
        }
        Remove-Item -LiteralPath (Join-Path $target $manifestName) -Force

        # Опустевшие папки, начиная с самых глубоких.
        $folders = @(Get-ChildItem -LiteralPath $target -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending)
        foreach ($folder in $folders) {
            if (-not (Get-ChildItem -LiteralPath $folder.FullName -Force)) { Remove-Item -LiteralPath $folder.FullName -Force }
        }
        if (Get-ChildItem -LiteralPath $target -Force) {
            Write-Host "В папке $target остались посторонние файлы - она не удалена." -ForegroundColor Yellow
        }
        else {
            Remove-Item -LiteralPath $target -Force
        }
    }

    Write-Host ''
    Write-Host 'Grapher удалён.' -ForegroundColor Green
    Write-Host "Настройки программы оставлены в папке $(Join-Path $env:APPDATA 'Grapher')."
    Write-Host 'Если Grapher остался в списке приложений КОМПАС-3D, уберите его оттуда:'
    Write-Host '«Приложения» → «Конфигуратор…» → группа «Приложения» → Grapher → «Исключить из конфигурации».'
}

try {
    if ($Uninstall) { Uninstall-Grapher } else { Install-Grapher }
    exit 0
}
catch {
    Write-Host ''
    Write-Host "Ошибка: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
