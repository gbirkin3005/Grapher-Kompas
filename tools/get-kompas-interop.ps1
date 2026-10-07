<#
.SYNOPSIS
  Копирует interop-сборки КОМПАС из SDK в папку lib\kompas.

.DESCRIPTION
  В SDK КОМПАС-3D готовые interop-сборки для .NET лежат внутри архива
  SDK\Samples\CSharp.zip, в папке Common. Скрипт достаёт оттуда сборки,
  на которые ссылается проект Grapher.Kompas.

  Запускать нужно после обновления КОМПАС до новой версии или если папка lib\kompas пуста.

.PARAMETER KompasDir
  Каталог установки КОМПАС-3D. По умолчанию берётся из реестра (самая новая установленная версия).
#>
param(
    [string]$KompasDir
)

$ErrorActionPreference = 'Stop'

if (-not $KompasDir) {
    $root = 'HKLM:\SOFTWARE\ASCON\KOMPAS-3D'
    $versions = Get-ChildItem $root -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -match '^\d+$' } |
        Sort-Object { [int]$_.PSChildName } -Descending
    foreach ($version in $versions) {
        $path = (Get-ItemProperty $version.PSPath).InstallPath
        if ($path -and (Test-Path $path)) { $KompasDir = $path; break }
    }
}

if (-not $KompasDir -or -not (Test-Path $KompasDir)) {
    throw 'Каталог установки КОМПАС-3D не найден. Укажите его параметром -KompasDir.'
}

$zip = Join-Path $KompasDir 'SDK\Samples\CSharp.zip'
if (-not (Test-Path $zip)) {
    throw "Не найден архив примеров SDK: $zip. Установите SDK вместе с КОМПАС-3D."
}

$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'lib\kompas'
New-Item -ItemType Directory -Force $target | Out-Null

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    foreach ($name in 'KompasAPI7.dll', 'Kompas6API5.dll', 'Kompas6Constants.dll', 'KAPITypes.dll') {
        $entry = $archive.GetEntry("Common/$name")
        if (-not $entry) { throw "В архиве SDK нет файла Common/$name" }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $target $name), $true)
        Write-Host "Скопировано: $name"
    }
}
finally {
    $archive.Dispose()
}

Write-Host "Interop-сборки КОМПАС обновлены в $target (источник: $KompasDir)"
