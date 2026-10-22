using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;

namespace Grapher.Setup;

/// <summary>Параметры установки.</summary>
internal sealed class InstallOptions
{
    public string Directory = Product.PerUserDirectory;
    public bool AllUsers;
    public bool StartMenuShortcut = true;
    public bool DesktopShortcut = true;
    public bool RegisterLibrary = true;

    /// <summary>Подключить Grapher к КОМПАС текущего пользователя (список приложений КОМПАС).</summary>
    public bool AttachToKompas = true;

    /// <summary>Файлы настроек выбранных версий КОМПАС; null — все установленные версии.</summary>
    public List<string> KompasConfigs;
}

/// <summary>Параметры удаления.</summary>
internal sealed class UninstallOptions
{
    public string Directory;
    public bool AllUsers;
    public bool DetachFromKompas = true;
    public bool RemoveSettings;
}

/// <summary>Файлы программы заняты (открыт КОМПАС с подключённым Grapher или сам Grapher).</summary>
internal sealed class FilesInUseException : Exception
{
    public FilesInUseException(string file)
        : base("Файл " + file + " занят. Закройте КОМПАС-3D и Grapher и повторите.")
    {
    }
}

/// <summary>Установка и удаление: копирование файлов, регистрация библиотеки, ярлыки, запись в списке приложений.</summary>
internal static class Installer
{
    /// <param name="log">Сообщения о ходе работы.</param>
    /// <param name="progress">Доля выполненной работы, 0…1.</param>
    public static void Install(InstallOptions options, Action<string> log, Action<double> progress)
    {
        if (!Payload.Available) throw new InvalidOperationException("В мастере установки нет файлов программы: файл установщика повреждён или собран без них.");

        string target = CheckDirectory(options.Directory);
        progress(0);

        // Прежняя установка в другой папке (или для другой группы пользователей) заменяется новой.
        ExistingInstall previous = ExistingInstall.Find(options.AllUsers) ?? ExistingInstall.Find(!options.AllUsers);
        bool movePrevious = previous != null && (!Product.SamePath(previous.Directory, target) || previous.AllUsers != options.AllUsers)
                            && (!previous.AllUsers || Elevation.IsElevated);

        log("Проверка, не заняты ли файлы программы…");
        EnsureNotInUse(target);
        if (movePrevious) EnsureNotInUse(previous.Directory);

        List<string> oldFiles = ReadManifest(target);
        var files = new List<string>();

        log("Копирование файлов в " + target + "…");
        Directory.CreateDirectory(target);
        using (Stream stream = Payload.Open())
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            int done = 0, total = zip.Entries.Count;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                done++;
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                string relative = entry.FullName.Replace('/', '\\');
                string destination = Path.Combine(target, relative);
                if (!Product.IsInside(destination, target)) throw new InvalidDataException("Неверное имя файла в пакете: " + entry.FullName);

                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                entry.ExtractToFile(destination, true);
                files.Add(relative);
                progress(0.7 * done / total);
            }
        }

        // Мастер остаётся в папке программы: из неё запускается удаление.
        string uninstaller = Path.Combine(target, Product.UninstallerName);
        if (!Product.SamePath(Product.SelfPath, uninstaller))
        {
            File.Copy(Product.SelfPath, uninstaller, true);
            // Копия скачанного мастера несёт отметку «получено из Интернета»; файлы из архива её не получают.
            Unblock(uninstaller);
        }
        files.Add(Product.UninstallerName);

        // Файлы прежней версии, которых в новой нет (в том числе install.cmd и setup.ps1 прежнего установщика).
        var current = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        foreach (string relative in oldFiles)
        {
            string stale = Path.Combine(target, relative);
            if (!current.Contains(relative) && Product.IsInside(stale, target) && File.Exists(stale))
            {
                File.Delete(stale);
            }
        }
        RemoveEmptyFolders(target, false);
        WriteManifest(target, files);
        progress(0.75);

        if (movePrevious)
        {
            log("Удаление прежней установки из " + previous.Directory + "…");
            RemoveFiles(previous.Directory, log);
            RemoveIntegration(previous.Directory, previous.AllUsers, log, previous.AllUsers != options.AllUsers);
        }

        string dll = Path.Combine(target, Product.LibraryName);
        if (options.RegisterLibrary)
        {
            log("Регистрация библиотеки для КОМПАС-3D (" + (options.AllUsers ? "для всех пользователей" : "для текущего пользователя") + ")…");
            LibraryRegistration.Register(dll, options.AllUsers);
        }
        progress(0.85);

        string exe = Path.Combine(target, Product.ExeName);
        if (options.StartMenuShortcut)
        {
            log("Ярлык в меню «Пуск».");
            Shortcuts.Create(Shortcuts.StartMenu(options.AllUsers), exe);
        }
        else
        {
            Shortcuts.RemoveIfInside(Shortcuts.StartMenu(options.AllUsers), target);
        }
        if (options.DesktopShortcut)
        {
            log("Ярлык на рабочем столе.");
            Shortcuts.Create(Shortcuts.Desktop(options.AllUsers), exe);
        }
        else
        {
            Shortcuts.RemoveIfInside(Shortcuts.Desktop(options.AllUsers), target);
        }

        UninstallEntry.Write(target, options.AllUsers, DirectorySize(target));
        log("Grapher добавлен в список установленных приложений Windows.");
        progress(0.95);

        if (options.AttachToKompas) AttachToKompas(log, options.KompasConfigs);
        progress(1);
        log("Установка завершена.");
    }

    /// <summary>Подключение к КОМПАС текущего пользователя; КОМПАС должен быть закрыт.</summary>
    /// <param name="files">Файлы настроек выбранных версий КОМПАС; null — все установленные версии.</param>
    public static void AttachToKompas(Action<string> log, List<string> files = null)
    {
        List<string> targets = files ?? Kompas.ConfigFiles();
        if (targets.Count == 0)
        {
            if (files == null) log("КОМПАС-3D ещё не запускался от имени этого пользователя — подключите Grapher в КОМПАС вручную.");
            return;
        }
        if (Kompas.IsRunning())
        {
            log("КОМПАС-3D запущен — Grapher не подключён к нему. Закройте КОМПАС и подключите Grapher вручную или повторите установку.");
            return;
        }
        Kompas.Attach(log, targets);
    }

    public static void Uninstall(UninstallOptions options, Action<string> log, Action<double> progress)
    {
        string target = Product.FullPath(options.Directory);
        progress(0);
        log("Проверка, не заняты ли файлы программы…");
        EnsureNotInUse(target);

        RemoveIntegration(target, options.AllUsers, log, true);
        progress(0.3);

        if (options.DetachFromKompas)
        {
            if (Kompas.IsRunning()) log("КОМПАС-3D запущен — Grapher остался в списке его приложений. Уберите его в КОМПАС: «Приложения» → «Конфигуратор…».");
            else Kompas.Detach(log);
        }
        progress(0.4);

        log("Удаление файлов из " + target + "…");
        RemoveFiles(target, log);
        progress(0.9);

        if (options.RemoveSettings && Directory.Exists(Product.SettingsDirectory))
        {
            Directory.Delete(Product.SettingsDirectory, true);
            log("Настройки программы удалены.");
        }
        progress(1);
        log("Grapher удалён.");
    }

    /// <summary>Регистрация библиотеки, ярлыки и запись в списке приложений, относящиеся к этой папке.</summary>
    private static void RemoveIntegration(string directory, bool allUsers, Action<string> log, bool removeEntry)
    {
        string registered = LibraryRegistration.RegisteredPath(allUsers);
        if (registered != null)
        {
            if (Product.IsInside(registered, directory))
            {
                LibraryRegistration.Unregister(allUsers);
                log("Регистрация библиотеки КОМПАС удалена.");
            }
            else
            {
                log("Библиотека КОМПАС зарегистрирована из другой папки (" + registered + ") — её регистрация не изменена.");
            }
        }

        if (Shortcuts.RemoveIfInside(Shortcuts.StartMenu(allUsers), directory)) log("Ярлык в меню «Пуск» удалён.");
        if (Shortcuts.RemoveIfInside(Shortcuts.Desktop(allUsers), directory)) log("Ярлык на рабочем столе удалён.");

        if (removeEntry)
        {
            ExistingInstall entry = ExistingInstall.Find(allUsers);
            if (entry != null && Product.SamePath(entry.Directory, directory)) UninstallEntry.Remove(allUsers);
        }
    }

    /// <summary>Удаляет установленные файлы по списку; посторонние файлы и папка с ними остаются.</summary>
    private static void RemoveFiles(string directory, Action<string> log)
    {
        if (!Directory.Exists(directory)) return;

        List<string> installed = ReadManifest(directory);
        if (installed.Count == 0)
        {
            log("Список установленных файлов не найден. Папку " + directory + " удалите вручную.");
            return;
        }

        foreach (string relative in installed)
        {
            string file = Path.Combine(directory, relative);
            if (!Product.IsInside(file, directory) || !File.Exists(file)) continue;
            if (Product.SamePath(file, Product.SelfPath)) continue;
            File.Delete(file);
        }
        string manifest = Path.Combine(directory, Product.ManifestName);
        if (File.Exists(manifest)) File.Delete(manifest);

        RemoveEmptyFolders(directory, true);
        if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length > 0)
        {
            log("В папке " + directory + " остались посторонние файлы — она не удалена.");
        }
    }

    private static void RemoveEmptyFolders(string directory, bool includingRoot)
    {
        if (!Directory.Exists(directory)) return;
        var folders = new List<string>(Directory.GetDirectories(directory, "*", SearchOption.AllDirectories));
        folders.Sort((a, b) => b.Length.CompareTo(a.Length));
        foreach (string folder in folders)
        {
            if (Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder);
        }
        if (includingRoot && Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
    }

    /// <summary>Проверяет папку установки и возвращает её полный путь.</summary>
    public static string CheckDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Не задана папка установки.");
        string full;
        try
        {
            full = Product.FullPath(directory.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            throw new ArgumentException("Неверная папка установки: " + directory);
        }
        if (!Path.IsPathRooted(full) || Path.GetPathRoot(full).TrimEnd('\\') == full)
        {
            throw new ArgumentException("Папкой установки не может быть корень диска: выберите или создайте отдельную папку.");
        }
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (Product.SamePath(full, windows) || Product.IsInside(full, windows))
        {
            throw new ArgumentException("Устанавливать программу в папку Windows нельзя.");
        }
        return full;
    }

    /// <summary>
    /// Пока КОМПАС держит библиотеку загруженной (или открыт сам Grapher), её файлы нельзя ни заменить, ни удалить.
    /// </summary>
    public static void EnsureNotInUse(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string file in Directory.GetFiles(directory))
        {
            string extension = Path.GetExtension(file);
            if (!extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (Product.SamePath(file, Product.SelfPath)) continue;
            try
            {
                using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
            }
            catch (IOException)
            {
                throw new FilesInUseException(Path.GetFileName(file));
            }
            catch (UnauthorizedAccessException)
            {
                // Файл только для чтения или нет прав: это выяснится при копировании.
            }
        }
    }

    private static List<string> ReadManifest(string directory)
    {
        var files = new List<string>();
        string path = Path.Combine(directory, Product.ManifestName);
        if (!File.Exists(path)) return files;
        foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
        {
            string relative = line.Trim().TrimStart('﻿');
            if (relative.Length > 0) files.Add(relative);
        }
        return files;
    }

    private static void WriteManifest(string directory, List<string> files)
    {
        string path = Path.Combine(directory, Product.ManifestName);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllLines(path, files, new UTF8Encoding(true));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool DeleteFile(string path);

    /// <summary>Снимает с файла отметку «получено из Интернета» (поток Zone.Identifier).</summary>
    private static void Unblock(string path) => DeleteFile(path + ":Zone.Identifier");

    private static long DirectorySize(string directory)
    {
        long total = 0;
        foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)) total += new FileInfo(file).Length;
        return total;
    }
}
