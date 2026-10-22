using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace Grapher.Setup;

/// <summary>Постоянные сведения об устанавливаемой программе.</summary>
internal static class Product
{
    public const string Name = "Grapher";
    public const string ExeName = "Grapher.exe";
    public const string LibraryName = "GrapherKompas.Library.dll";
    public const string UninstallerName = "Uninstall.exe";
    public const string GuideName = "Руководство.txt";

    /// <summary>Страница программы: исходный код, выпуски и инструкции.</summary>
    public const string RepositoryUrl = "https://github.com/gbirkin3005/Grapher-Kompas";

    /// <summary>Автор (владелец репозитория) — в списке программ Windows это «Издатель».</summary>
    public const string Publisher = "gbirkin3005";

    /// <summary>Список установленных файлов (тот же, что вёл прежний установщик setup.ps1).</summary>
    public const string ManifestName = @"setup\installed-files.txt";

    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Grapher";

    // Регистрация библиотеки КОМПАС (как RegAsm /codebase), см. tools\register-library-user.ps1.
    public const string Clsid = "{25A52761-85E0-453D-BA06-C9AF2727E4D9}";
    public const string ProgId = "GrapherKompas.Library";
    public const string ClassName = "Grapher.KompasLibrary.GrapherLibrary";

    public static string Version
    {
        get
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            return version.ToString(3);
        }
    }

    /// <summary>Файл самого мастера (из него делается копия для удаления программы).</summary>
    public static string SelfPath => Assembly.GetExecutingAssembly().Location;

    public static string PerUserDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name);

    public static string AllUsersDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Name);

    public static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);

    /// <summary>Раздел реестра: HKCU для установки «только для меня», HKLM — для всех пользователей (64-разрядная ветка).</summary>
    public static RegistryKey BaseKey(bool allUsers) =>
        RegistryKey.OpenBaseKey(allUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);

    public static string FullPath(string path) => Path.GetFullPath(path).TrimEnd('\\');

    public static bool SamePath(string a, string b) =>
        string.Equals(FullPath(a), FullPath(b), StringComparison.OrdinalIgnoreCase);

    public static bool IsInside(string path, string directory) =>
        FullPath(path).StartsWith(FullPath(directory) + "\\", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Архив с файлами программы, встроенный в мастер установки.</summary>
internal static class Payload
{
    private const string ResourceName = "payload.zip";

    public static Stream Open() => Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);

    public static bool Available
    {
        get
        {
            using (Stream stream = Open()) return stream != null;
        }
    }

    /// <summary>Размер файлов программы после распаковки, байт.</summary>
    public static long InstalledSize
    {
        get
        {
            using (Stream stream = Open())
            {
                if (stream == null) return 0;
                long total = 0;
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in zip.Entries) total += entry.Length;
                }
                return total;
            }
        }
    }
}

/// <summary>Установленная программа, найденная по записи в списке приложений Windows.</summary>
internal sealed class ExistingInstall
{
    public string Directory;
    public string Version;
    public bool AllUsers;

    /// <summary>Установка текущего пользователя, иначе — для всех пользователей; null — Grapher не установлен.</summary>
    public static ExistingInstall Find()
    {
        foreach (bool allUsers in new[] { false, true })
        {
            ExistingInstall found = Find(allUsers);
            if (found != null) return found;
        }
        return null;
    }

    public static ExistingInstall Find(bool allUsers)
    {
        try
        {
            using (RegistryKey root = Product.BaseKey(allUsers))
            using (RegistryKey key = root.OpenSubKey(Product.UninstallKey))
            {
                string directory = key?.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(directory)) return null;
                return new ExistingInstall
                {
                    Directory = directory,
                    Version = key.GetValue("DisplayVersion") as string ?? "",
                    AllUsers = allUsers
                };
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
        {
            return null;
        }
    }
}
