using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace Grapher.Setup;

/// <summary>Регистрация библиотеки КОМПАС как COM-класса .NET (те же записи, что делает RegAsm /codebase).</summary>
internal static class LibraryRegistration
{
    public static void Register(string dllPath, bool allUsers)
    {
        AssemblyName assembly = AssemblyName.GetAssemblyName(dllPath);
        string codeBase = CodeBase(dllPath);
        string mscoree = Path.Combine(Environment.SystemDirectory, "mscoree.dll");

        void ServerValues(RegistryKey key)
        {
            key.SetValue("Class", Product.ClassName);
            key.SetValue("Assembly", assembly.FullName);
            key.SetValue("RuntimeVersion", "v4.0.30319");
            key.SetValue("CodeBase", codeBase);
        }

        using (RegistryKey root = Product.BaseKey(allUsers))
        using (RegistryKey classes = root.CreateSubKey(@"Software\Classes"))
        {
            using (RegistryKey key = classes.CreateSubKey(Product.ProgId)) key.SetValue("", Product.ClassName);
            using (RegistryKey key = classes.CreateSubKey(Product.ProgId + @"\CLSID")) key.SetValue("", Product.Clsid);

            using (RegistryKey cls = classes.CreateSubKey(@"CLSID\" + Product.Clsid))
            {
                cls.SetValue("", Product.ClassName);
                using (RegistryKey server = cls.CreateSubKey("InprocServer32"))
                {
                    server.SetValue("", mscoree);
                    server.SetValue("ThreadingModel", "Both");
                    ServerValues(server);
                }
                using (RegistryKey version = cls.CreateSubKey(@"InprocServer32\" + assembly.Version)) ServerValues(version);
                using (RegistryKey progId = cls.CreateSubKey("ProgId")) progId.SetValue("", Product.ProgId);
                // Категория «компоненты .NET» и признак приложения КОМПАС.
                cls.CreateSubKey(@"Implemented Categories\{62C8FE65-4EBB-45E7-B440-6E39B2CDBF29}")?.Dispose();
                cls.CreateSubKey("Kompas_Library")?.Dispose();
            }
        }
    }

    /// <summary>
    /// Адрес библиотеки в том виде, в каком его записывает RegAsm /codebase: file:///C:/Program Files/…, без экранирования.
    /// Экранированный адрес (Program%20Files, кириллица в виде %D0%…) .NET при создании COM-объекта не находит:
    /// КОМПАС не может загрузить библиотеку и не показывает её в списке приложений.
    /// </summary>
    internal static string CodeBase(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) ? "file:" + path.Replace('\\', '/') : "file:///" + path.Replace('\\', '/');

    /// <summary>Путь к зарегистрированной библиотеке; null, если регистрации нет.</summary>
    public static string RegisteredPath(bool allUsers)
    {
        using (RegistryKey root = Product.BaseKey(allUsers))
        using (RegistryKey key = root.OpenSubKey(@"Software\Classes\CLSID\" + Product.Clsid + @"\InprocServer32"))
        {
            if (!(key?.GetValue("CodeBase") is string codeBase)) return null;
            return PathFromCodeBase(codeBase);
        }
    }

    /// <summary>Путь к файлу по адресу CodeBase; null, если адрес не распознан.</summary>
    internal static string PathFromCodeBase(string codeBase)
    {
        const string local = "file:///";
        // Адрес без экранирования читается как есть: в пути могут быть «#» и другие знаки, особые для Uri.
        if (codeBase.StartsWith(local, StringComparison.OrdinalIgnoreCase) && codeBase.IndexOf('%') < 0)
        {
            return codeBase.Substring(local.Length).Replace('/', '\\');
        }
        // Экранированный адрес записывали мастера версий 1.2.1 и раньше.
        try
        {
            return new Uri(codeBase).LocalPath;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    public static void Unregister(bool allUsers)
    {
        using (RegistryKey root = Product.BaseKey(allUsers))
        using (RegistryKey classes = root.OpenSubKey(@"Software\Classes", true))
        {
            if (classes == null) return;
            classes.DeleteSubKeyTree(@"CLSID\" + Product.Clsid, false);
            classes.DeleteSubKeyTree(Product.ProgId, false);
        }
    }
}

/// <summary>Ярлыки в меню «Пуск» и на рабочем столе.</summary>
internal static class Shortcuts
{
    public static string StartMenu(bool allUsers) =>
        Path.Combine(Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonPrograms : Environment.SpecialFolder.Programs),
            "Grapher.lnk");

    public static string Desktop(bool allUsers) =>
        Path.Combine(Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonDesktopDirectory : Environment.SpecialFolder.DesktopDirectory),
            "Grapher.lnk");

    public static void Create(string linkPath, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath));
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(shellType);
        try
        {
            dynamic link = shell.CreateShortcut(linkPath);
            link.TargetPath = target;
            link.WorkingDirectory = Path.GetDirectoryName(target);
            link.Description = "Grapher: графики по точкам для КОМПАС-3D";
            link.IconLocation = target + ",0";
            link.Save();
            Marshal.FinalReleaseComObject(link);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>Куда ведёт ярлык; null, если ярлыка нет.</summary>
    public static string TargetOf(string linkPath)
    {
        if (!File.Exists(linkPath)) return null;
        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(shellType);
        try
        {
            dynamic link = shell.CreateShortcut(linkPath);
            string target = link.TargetPath;
            Marshal.FinalReleaseComObject(link);
            return target;
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>Удаляет ярлык, только если он ведёт в указанную папку.</summary>
    public static bool RemoveIfInside(string linkPath, string directory)
    {
        string target = TargetOf(linkPath);
        if (string.IsNullOrEmpty(target) || !Product.IsInside(target, directory)) return false;
        File.Delete(linkPath);
        return true;
    }
}

/// <summary>Запись в списке установленных приложений Windows («Приложения и возможности»).</summary>
internal static class UninstallEntry
{
    public static void Write(string directory, bool allUsers, long sizeBytes)
    {
        string exe = Path.Combine(directory, Product.ExeName);
        string uninstaller = Path.Combine(directory, Product.UninstallerName);
        using (RegistryKey root = Product.BaseKey(allUsers))
        using (RegistryKey key = root.CreateSubKey(Product.UninstallKey))
        {
            key.SetValue("DisplayName", Product.Name);
            key.SetValue("DisplayVersion", Product.Version);
            key.SetValue("DisplayIcon", exe);
            key.SetValue("Comments", "Графики по точкам для КОМПАС-3D");
            key.SetValue("Publisher", Product.Publisher);
            // В «Программах и компонентах»: «Сведения об обновлении», «Ссылка справки», «Ссылка на службу поддержки».
            key.SetValue("URLUpdateInfo", Product.RepositoryUrl);
            key.SetValue("HelpLink", Product.RepositoryUrl);
            key.SetValue("URLInfoAbout", Product.RepositoryUrl);
            key.SetValue("InstallLocation", directory);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            key.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
            key.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /silent");
            key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, sizeBytes / 1024), RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
    }

    public static void Remove(bool allUsers)
    {
        using (RegistryKey root = Product.BaseKey(allUsers))
        {
            root.DeleteSubKeyTree(Product.UninstallKey, false);
        }
    }
}

/// <summary>КОМПАС-3D: установлен ли, запущен ли, подключение Grapher к списку приложений.</summary>
internal static class Kompas
{
    /// <summary>Исполняемые файлы разных редакций КОМПАС-3D.</summary>
    private static readonly string[] ProcessNames = { "kStudy", "KOMPAS", "kHome", "kLT", "kompasv" };

    private const string ApplicationId = "GrapherKompas.Library";

    public static bool IsInstalled()
    {
        try
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64))
            using (RegistryKey key = root.OpenSubKey("KOMPAS.Application.7"))
            {
                return key != null;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
        {
            return false;
        }
    }

    public static bool IsRunning()
    {
        foreach (string name in ProcessNames)
        {
            Process[] processes = Process.GetProcessesByName(name);
            bool running = processes.Length > 0;
            foreach (Process process in processes) process.Dispose();
            if (running) return true;
        }
        return false;
    }

    /// <summary>
    /// Файлы со списком подключённых приложений КОМПАС текущего пользователя:
    /// %AppData%\ASCON\KOMPAS-3D\&lt;версия&gt;\&lt;редакция&gt;.kit.config. Файл появляется после первого запуска КОМПАС.
    /// </summary>
    /// <param name="installedOnly">
    /// Только для установленных версий КОМПАС: после удаления старой версии её папка настроек остаётся.
    /// </param>
    public static List<string> ConfigFiles(bool installedOnly = true)
    {
        var files = new List<string>();
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ASCON", "KOMPAS-3D");
        if (!Directory.Exists(root)) return files;

        var folders = new List<string>(Directory.GetDirectories(root));
        if (installedOnly)
        {
            List<string> installed = InstalledVersions();
            if (installed.Count > 0)
            {
                folders.RemoveAll(folder => !installed.Contains(Path.GetFileName(folder)));
            }
            else
            {
                // Сведений о версиях в реестре нет: берём самую новую папку настроек.
                folders.Sort((a, b) => VersionNumber(b).CompareTo(VersionNumber(a)));
                if (folders.Count > 1) folders.RemoveRange(1, folders.Count - 1);
            }
        }

        foreach (string folder in folders)
        {
            files.AddRange(Directory.GetFiles(folder, "*.kit.config"));
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    /// <summary>Номера установленных версий КОМПАС-3D (разделы HKLM\SOFTWARE\ASCON\KOMPAS-3D\&lt;версия&gt;).</summary>
    private static List<string> InstalledVersions()
    {
        var versions = new List<string>();
        try
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey kompas = root.OpenSubKey(@"SOFTWARE\ASCON\KOMPAS-3D"))
            {
                if (kompas == null) return versions;
                foreach (string name in kompas.GetSubKeyNames())
                {
                    if (VersionNumber(name) > 0) versions.Add(name);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
        {
        }
        return versions;
    }

    private static int VersionNumber(string folder) => int.TryParse(Path.GetFileName(folder), out int number) ? number : 0;

    /// <summary>Версии КОМПАС для проверки окна мастера (ключ /testkompas вместе с /screens); null — по реестру.</summary>
    internal static List<string> VersionsOverride;

    /// <summary>
    /// Установленные версии и редакции КОМПАС-3D, к которым можно подключить Grapher: по одной на файл настроек
    /// текущего пользователя. Версия, которая ещё не запускалась от его имени, — без файла настроек.
    /// </summary>
    public static List<KompasProfile> Profiles()
    {
        var profiles = new List<KompasProfile>();
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ASCON", "KOMPAS-3D");

        List<string> versions = VersionsOverride ?? InstalledVersions();
        if (versions.Count == 0)
        {
            // Сведений о версиях в реестре нет: файлы настроек самой новой версии.
            foreach (string file in ConfigFiles()) profiles.Add(KompasProfile.FromFile(file));
            return profiles;
        }

        versions.Sort((a, b) => VersionNumber(b).CompareTo(VersionNumber(a)));
        foreach (string version in versions)
        {
            string folder = Path.Combine(root, version);
            string[] files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.kit.config") : new string[0];
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            if (files.Length == 0) profiles.Add(new KompasProfile { Version = version, Title = "КОМПАС-3D v" + version });
            foreach (string file in files) profiles.Add(KompasProfile.FromFile(file));
        }
        return profiles;
    }

    /// <summary>Подключён ли Grapher к КОМПАС по этому файлу настроек.</summary>
    public static bool IsAttached(string file)
    {
        try
        {
            return Find(Parse(Read(file, out _, out _))) != null;
        }
        catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Добавляет Grapher в список приложений КОМПАС (как «Приложения» → «Добавить приложения…» → «ActiveX»).
    /// КОМПАС должен быть закрыт: при выходе он перезаписывает этот файл.
    /// </summary>
    /// <param name="files">Файлы настроек выбранных версий; null — все установленные версии.</param>
    /// <returns>Сколько файлов изменено.</returns>
    public static int Attach(Action<string> log, IEnumerable<string> files = null)
    {
        int changed = 0;
        foreach (string file in files ?? ConfigFiles())
        {
            string text = Read(file, out Encoding encoding, out string newLine);
            XmlDocument document = Parse(text);
            if (Find(document) != null)
            {
                log("Grapher уже подключён к " + Describe(file));
                continue;
            }
            if (document.DocumentElement == null || document.DocumentElement.Name != "Kit_Config")
            {
                log("Файл настроек КОМПАС не распознан, Grapher не подключён: " + file);
                continue;
            }

            // Строка вставляется в начало первой группы приложений, как это делает сам КОМПАС;
            // остальной текст файла не меняется.
            string updated;
            Match group = GroupStart.Match(text);
            if (group.Success)
            {
                // Отступ — как у строки с тегом группы, плюс одна табуляция.
                int tag = group.Index + group.Length - "<Group>".Length;
                int lineStart = text.LastIndexOf('\n', tag) + 1;
                string indent = text.Substring(lineStart, tag - lineStart);
                if (indent.Trim().Length > 0) indent = "";
                updated = text.Insert(group.Index + group.Length, newLine + indent + "\t" + ApplicationLine);
            }
            else
            {
                // Групп ещё нет: файл перестраивается целиком.
                XmlElement rootElement = document.DocumentElement;
                XmlElement groups = rootElement["Groups"] ?? AppendWithIndent(rootElement, document.CreateElement("Groups"), 1);
                XmlElement first = groups["Group"] ?? AppendWithIndent(groups, document.CreateElement("Group"), 2);
                AppendWithIndent(first, (XmlElement)document.ImportNode(Parse(ApplicationLine).DocumentElement, false), 3);
                updated = Serialize(document, encoding, newLine);
            }

            Write(file, updated, encoding, log);
            log("Grapher подключён к " + Describe(file));
            changed++;
        }
        return changed;
    }

    /// <summary>Убирает Grapher из списка приложений КОМПАС.</summary>
    public static int Detach(Action<string> log)
    {
        int changed = 0;
        foreach (string file in ConfigFiles(false))
        {
            string text = Read(file, out Encoding encoding, out _);
            if (Find(Parse(text)) == null) continue;

            string updated = ApplicationElement.Replace(text, "");
            if (Find(Parse(updated)) != null)
            {
                log("Строку Grapher в файле настроек КОМПАС не удалось убрать: " + file);
                continue;
            }

            Write(file, updated, encoding, log);
            log("Grapher убран из списка приложений " + Describe(file));
            changed++;
        }
        return changed;
    }

    /// <summary>Строка приложения — та же, что записывает КОМПАС при подключении через «Добавить приложения…».</summary>
    private const string ApplicationLine =
        "<Application libType=\"lbt_ocx\" ocxClassId=\"" + ApplicationId + "\" id=\"" + ApplicationId +
        "\" libName=\"Grapher\" displayName=\"Grapher\" autostart=\"true\"/>";

    /// <summary>Открывающий тег первой группы внутри &lt;Groups&gt;.</summary>
    private static readonly Regex GroupStart = new Regex(@"<Groups>\s*<Group>", RegexOptions.CultureInvariant);

    /// <summary>Элемент Grapher вместе с переводом строки и отступом перед ним.</summary>
    private static readonly Regex ApplicationElement = new Regex(
        @"\r?\n[ \t]*<Application\b[^>]*\b(?:id|ocxClassId)=""" + Regex.Escape(ApplicationId) + @"""[^>]*/>",
        RegexOptions.CultureInvariant);

    /// <summary>Записывает файл, если новый текст — правильный XML; перед первым изменением сохраняет копию.</summary>
    private static void Write(string file, string text, Encoding encoding, Action<string> log)
    {
        Parse(text);
        Backup(file);
        File.WriteAllText(file, text, encoding);
    }

    public static bool IsAttached()
    {
        foreach (string file in ConfigFiles())
        {
            try
            {
                if (Find(Parse(Read(file, out _, out _))) != null) return true;
            }
            catch (XmlException)
            {
            }
        }
        return false;
    }

    private static string Describe(string file) => KompasProfile.FromFile(file).Title;

    private static XmlElement Find(XmlDocument document)
    {
        XmlNode groups = document.DocumentElement?["Groups"];
        if (groups == null) return null;
        foreach (XmlNode node in groups.SelectNodes(".//Application"))
        {
            if (node is XmlElement element &&
                (element.GetAttribute("id") == ApplicationId || element.GetAttribute("ocxClassId") == ApplicationId))
            {
                return element;
            }
        }
        return null;
    }

    private static string Read(string file, out Encoding encoding, out string newLine)
    {
        byte[] bytes = File.ReadAllBytes(file);
        using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true))
        {
            string text = reader.ReadToEnd();
            // Кодировка (в КОМПАС-3D v25 — UTF-16 с меткой порядка байтов) и переводы строк сохраняются как были.
            encoding = reader.CurrentEncoding;
            newLine = text.Contains("\r\n") ? "\r\n" : "\n";
            return text;
        }
    }

    private static XmlDocument Parse(string text)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(text);
        return document;
    }

    private static string Serialize(XmlDocument document, Encoding encoding, string newLine)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = encoding,
            Indent = false,
            NewLineChars = newLine,
            NewLineHandling = NewLineHandling.Replace
        };
        using (var stream = new MemoryStream())
        {
            using (XmlWriter writer = XmlWriter.Create(stream, settings))
            {
                document.Save(writer);
            }
            stream.Position = 0;
            using (var reader = new StreamReader(stream, encoding, true))
            {
                return reader.ReadToEnd();
            }
        }
    }

    /// <summary>Копия файла настроек КОМПАС до первого изменения мастером.</summary>
    private static void Backup(string file)
    {
        string backup = file + ".grapher-backup";
        if (!File.Exists(backup)) File.Copy(file, backup);
    }

    /// <summary>Добавляет узел последним в родителя, с отступом табуляциями, как в файлах КОМПАС.</summary>
    private static XmlElement AppendWithIndent(XmlElement parent, XmlElement child, int depth)
    {
        XmlDocument document = parent.OwnerDocument;
        string indent = "\n" + new string('\t', depth);
        XmlNode last = parent.LastChild;
        if (last is XmlWhitespace closing)
        {
            // Пробельный узел перед закрывающим тегом родителя остаётся последним.
            parent.InsertBefore(document.CreateWhitespace(indent), closing);
            parent.InsertBefore(child, closing);
        }
        else
        {
            parent.AppendChild(document.CreateWhitespace(indent));
            parent.AppendChild(child);
            parent.AppendChild(document.CreateWhitespace("\n" + new string('\t', depth - 1)));
        }
        return child;
    }
}

/// <summary>Установленная версия (и редакция) КОМПАС-3D с файлом настроек текущего пользователя.</summary>
internal sealed class KompasProfile
{
    public string Version;
    public string Title;

    /// <summary>Файл со списком приложений; null — версия ещё не запускалась от имени пользователя.</summary>
    public string ConfigFile;

    public bool Attached => ConfigFile != null && Kompas.IsAttached(ConfigFile);

    /// <summary>Редакция — по имени исполняемого файла КОМПАС, от которого назван файл настроек.</summary>
    public static KompasProfile FromFile(string file)
    {
        string version = Path.GetFileName(Path.GetDirectoryName(file));
        string name = Path.GetFileName(file);
        string stem = name.Substring(0, name.Length - ".kit.config".Length);
        string edition;
        switch (stem.ToLowerInvariant())
        {
            case "kstudy": edition = " Учебная версия"; break;
            case "khome": edition = " Home"; break;
            case "klt": edition = " LT"; break;
            case "kompas": edition = ""; break;
            default: edition = " (" + stem + ")"; break;
        }
        return new KompasProfile { Version = version, Title = "КОМПАС-3D v" + version + edition, ConfigFile = file };
    }
}

/// <summary>Права администратора и повторный запуск мастера с ними.</summary>
internal static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
    }

    /// <summary>Запускает этот же файл с правами администратора (окно UAC).</summary>
    /// <returns>Процесс; null, если пользователь отказался в окне UAC.</returns>
    public static Process Start(string arguments)
    {
        var info = new ProcessStartInfo(Product.SelfPath, arguments)
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        try
        {
            return Process.Start(info);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return null;
        }
    }

    private const int BcmSetShield = 0x160C;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Значок щита на кнопке: действие запросит права администратора.</summary>
    public static void SetShield(Button button, bool shield)
    {
        button.FlatStyle = FlatStyle.System;
        SendMessage(button.Handle, BcmSetShield, IntPtr.Zero, shield ? (IntPtr)1 : IntPtr.Zero);
    }
}
