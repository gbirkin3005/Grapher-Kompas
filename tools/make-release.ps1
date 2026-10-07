<#
.SYNOPSIS
  Собирает релизный пакет Grapher: папку dist\release\Grapher-<версия> и архив Grapher-<версия>.zip.

.DESCRIPTION
  В пакет попадают программа и библиотека КОМПАС (в одной папке, общие файлы не дублируются),
  установщик (install.cmd, uninstall.cmd, setup\), примеры данных и документация в виде текстовых файлов.
  Отладочные файлы (.pdb) в пакет не включаются.

  Папки dist\Grapher и dist\GrapherKompas.Library скрипт не трогает, поэтому его можно запускать,
  даже когда КОМПАС открыт и держит библиотеку из dist загруженной.

.PARAMETER SkipTests
  Не запускать тесты ядра перед сборкой.
#>
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Encoding UTF8
$versionNode = $props.SelectSingleNode('/Project/PropertyGroup/Version')
if (-not $versionNode) { throw 'В Directory.Build.props не найдена версия (Version).' }
$version = $versionNode.InnerText.Trim()

$name = "Grapher-$version"
$releaseDir = Join-Path $root 'dist\release'
$stage = Join-Path $releaseDir $name
$libraryStage = Join-Path $releaseDir '_library'
$zip = Join-Path $releaseDir "$name.zip"

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "Команда dotnet $($args[0]) завершилась с ошибкой." }
}

function Get-Sha256([string]$path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($path)
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}

# Документация в пакете — обычные текстовые файлы: разметка Markdown убирается.
function Convert-MarkdownToText([string]$source, [string]$destination) {
    $result = New-Object System.Collections.Generic.List[string]
    foreach ($line in Get-Content -LiteralPath $source -Encoding UTF8) {
        if ($line -match '^!\[') { continue }
        $text = $line -replace '\*\*', '' -replace '`', ''
        $text = $text -replace '\[([^\]]+)\]\([^)]+\)', '$1'
        if ($text -match '^(#+)\s+(.*)$') {
            $title = $Matches[2]
            $underline = '-'
            if ($Matches[1].Length -eq 1) { $underline = '=' }
            $result.Add($title)
            $result.Add($underline * $title.Length)
        }
        else {
            $result.Add($text)
        }
    }
    [IO.File]::WriteAllLines($destination, $result, (New-Object Text.UTF8Encoding $true))
}

if (-not (Test-Path -LiteralPath (Join-Path $root 'lib\kompas\KompasAPI7.dll'))) {
    Write-Host '=== Interop-сборки КОМПАС ==='
    & (Join-Path $PSScriptRoot 'get-kompas-interop.ps1')
}

if (-not $SkipTests) {
    Write-Host '=== Тесты ядра ==='
    Invoke-Dotnet test (Join-Path $root 'tests\Grapher.Core.Tests\Grapher.Core.Tests.csproj') -c Release --nologo
}

foreach ($folder in $stage, $libraryStage) {
    if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null

$options = @('-c', 'Release', '--nologo')

Write-Host ''
Write-Host '=== Приложение ==='
Invoke-Dotnet publish (Join-Path $root 'src\Grapher.App\Grapher.App.csproj') -o $stage @options

Write-Host ''
Write-Host '=== Библиотека КОМПАС ==='
Invoke-Dotnet publish (Join-Path $root 'src\Grapher.KompasLibrary\Grapher.KompasLibrary.csproj') -o $libraryStage @options

# Библиотека кладётся в ту же папку, что и приложение. Общие файлы должны совпадать байт в байт.
foreach ($file in Get-ChildItem -LiteralPath $libraryStage -File) {
    $existing = Join-Path $stage $file.Name
    if (Test-Path -LiteralPath $existing) {
        if ((Get-Sha256 $existing) -ne (Get-Sha256 $file.FullName)) {
            throw "Файл $($file.Name) у приложения и у библиотеки различается."
        }
    }
    else {
        Copy-Item -LiteralPath $file.FullName -Destination $existing
    }
}
Remove-Item -LiteralPath $libraryStage -Recurse -Force

Write-Host ''
Write-Host '=== Пакет ==='
Copy-Item -LiteralPath (Join-Path $root 'installer\install.cmd'), (Join-Path $root 'installer\uninstall.cmd') -Destination $stage
New-Item -ItemType Directory -Path (Join-Path $stage 'setup') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'installer\setup.ps1'), (Join-Path $PSScriptRoot 'register-library-user.ps1') -Destination (Join-Path $stage 'setup')
New-Item -ItemType Directory -Path (Join-Path $stage 'samples') | Out-Null
Copy-Item -Path (Join-Path $root 'samples\*') -Destination (Join-Path $stage 'samples')
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.txt') -Destination $stage
Convert-MarkdownToText (Join-Path $root 'docs\INSTALL.md') (Join-Path $stage 'Установка.txt')
Convert-MarkdownToText (Join-Path $root 'docs\USAGE.md') (Join-Path $stage 'Руководство.txt')

# Архив собирается по одному файлу: ZipFile.CreateFromDirectory в Windows PowerShell 5.1
# записывает имена с обратной косой чертой, а по стандарту ZIP разделитель — «/».
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$files = @(Get-ChildItem -LiteralPath $stage -Recurse -File)
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $entryName = $name + '/' + $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $archive.Dispose()
}

Write-Host ("Папка:  {0} ({1} файлов)" -f $stage, $files.Count)
Write-Host ("Архив:  {0} ({1:N0} КБ)" -f $zip, ((Get-Item -LiteralPath $zip).Length / 1KB))
Write-Host ("SHA256: {0}" -f (Get-Sha256 $zip))
