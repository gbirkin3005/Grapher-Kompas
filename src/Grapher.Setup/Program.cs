using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Grapher.Setup;

/// <summary>
/// Мастер установки Grapher.
/// <code>
///   GrapherSetup.exe                       мастер установки
///   GrapherSetup.exe /uninstall            мастер удаления (так же — Uninstall.exe в папке программы)
///   Ключи без окна: /silent [/allusers] [/dir=ПАПКА] [/nostartmenu] [/nodesktop] [/nokompas] [/log=ФАЙЛ]
///                   /uninstall /silent [/removesettings] [/nokompas] [/log=ФАЙЛ]
/// </code>
/// Коды завершения: 0 — успешно, 1 — ошибка, 2 — файлы заняты (открыт КОМПАС), 3 — отменено.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var arguments = new Arguments(args);
        if (Path.GetFileName(Product.SelfPath).Equals(Product.UninstallerName, StringComparison.OrdinalIgnoreCase))
        {
            arguments.Uninstall = true;
        }

        try
        {
            if (arguments.Screens != null)
            {
                // Только для снимков окна: список версий КОМПАС вместо найденного в реестре.
                if (arguments.TestKompas != null) Kompas.VersionsOverride = new List<string>(arguments.TestKompas.Split(','));
                return WizardForm.SaveScreens(arguments.Screens);
            }

            if (arguments.Uninstall)
            {
                // Программа не может удалить файл, из которого запущена: удаление продолжает её копия во временной папке.
                string directory = arguments.Directory ?? FindInstallDirectory(arguments);
                if (directory != null && !arguments.Relocated && Product.IsInside(Product.SelfPath, directory))
                {
                    return Relocate(args, directory);
                }

                int code = arguments.Silent ? SilentUninstall(arguments, directory) : RunWizard(new WizardForm(arguments, directory));
                if (arguments.Relocated) DeleteSelfLater();
                return code;
            }

            return arguments.Silent ? SilentInstall(arguments) : RunWizard(new WizardForm(arguments, null));
        }
        catch (Exception ex)
        {
            if (!arguments.Silent) MessageBox.Show(ex.Message, "Установка Grapher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int RunWizard(WizardForm form)
    {
        Application.Run(form);
        return form.ExitCode;
    }

    /// <summary>Папка установленной программы для удаления: из записи в списке приложений или папка, из которой запущен мастер.</summary>
    private static string FindInstallDirectory(Arguments arguments)
    {
        string own = Path.GetDirectoryName(Product.SelfPath);
        if (File.Exists(Path.Combine(own, Product.ManifestName))) return own;
        ExistingInstall existing = arguments.AllUsers ? ExistingInstall.Find(true) : ExistingInstall.Find();
        return existing?.Directory;
    }

    private static int Relocate(string[] args, string directory)
    {
        string copy = Path.Combine(Path.GetTempPath(), "GrapherUninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
        File.Copy(Product.SelfPath, copy, true);

        var list = new List<string>(args) { "/uninstall", "/relocated", "/dir=" + directory };
        Process.Start(new ProcessStartInfo(copy, Arguments.Join(list)) { UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
        return 0;
    }

    /// <summary>Временная копия мастера удаляет себя после выхода.</summary>
    private static void DeleteSelfLater()
    {
        string self = Product.SelfPath;
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + self + "\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath()
        });
    }

    private static int SilentInstall(Arguments a)
    {
        var options = new InstallOptions
        {
            AllUsers = a.AllUsers,
            Directory = a.Directory ?? (a.AllUsers ? Product.AllUsersDirectory : Product.PerUserDirectory),
            StartMenuShortcut = !a.NoStartMenu,
            DesktopShortcut = !a.NoDesktop,
            AttachToKompas = !a.NoKompas
        };
        if (options.AllUsers && !Elevation.IsElevated)
        {
            return Fail(a, "Для установки для всех пользователей нужны права администратора: запустите мастер от имени администратора.", 3);
        }
        return Run(a, log => Installer.Install(options, log, _ => { }));
    }

    private static int SilentUninstall(Arguments a, string directory)
    {
        if (directory == null) return Fail(a, "Grapher не установлен: запись об установке не найдена.", 1);
        ExistingInstall entry = ExistingInstall.Find(true);
        bool allUsers = a.AllUsers || entry != null && Product.SamePath(entry.Directory, directory);
        if (allUsers && !Elevation.IsElevated)
        {
            return Fail(a, "Grapher установлен для всех пользователей: для удаления нужны права администратора.", 3);
        }
        var options = new UninstallOptions
        {
            Directory = directory,
            AllUsers = allUsers,
            DetachFromKompas = !a.NoKompas,
            RemoveSettings = a.RemoveSettings
        };
        return Run(a, log => Installer.Uninstall(options, log, _ => { }));
    }

    private static int Run(Arguments a, Action<Action<string>> work)
    {
        var lines = new List<string>();
        void Log(string line)
        {
            lines.Add(line);
            if (a.LogFile != null) File.AppendAllText(a.LogFile, line + Environment.NewLine, Encoding.UTF8);
        }

        try
        {
            work(Log);
            return 0;
        }
        catch (FilesInUseException ex)
        {
            Log("ОШИБКА: " + ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Log("ОШИБКА: " + ex.Message);
            return 1;
        }
    }

    private static int Fail(Arguments a, string message, int code)
    {
        if (a.LogFile != null) File.AppendAllText(a.LogFile, "ОШИБКА: " + message + Environment.NewLine, Encoding.UTF8);
        return code;
    }
}

/// <summary>Ключи командной строки мастера.</summary>
internal sealed class Arguments
{
    public bool Uninstall, Silent, AllUsers, NoStartMenu, NoDesktop, NoKompas, RemoveSettings, Relocated;

    /// <summary>Запущен окном мастера с правами администратора: часть работы для текущего пользователя делает окно.</summary>
    public bool Child;

    public string Directory, LogFile, Screens, TestKompas;

    public Arguments(string[] args)
    {
        foreach (string raw in args)
        {
            string arg = raw.Trim();
            if (arg.Length < 2 || arg[0] != '/' && arg[0] != '-') continue;
            string name = arg.Substring(1), value = null;
            int equals = name.IndexOf('=');
            if (equals >= 0)
            {
                value = name.Substring(equals + 1).Trim('"');
                name = name.Substring(0, equals);
            }

            switch (name.ToLowerInvariant())
            {
                case "uninstall": Uninstall = true; break;
                case "silent":
                case "quiet": Silent = true; break;
                case "allusers": AllUsers = true; break;
                case "nostartmenu": NoStartMenu = true; break;
                case "nodesktop": NoDesktop = true; break;
                case "nokompas": NoKompas = true; break;
                case "removesettings": RemoveSettings = true; break;
                case "relocated": Relocated = true; break;
                case "child": Child = true; break;
                case "dir": Directory = value; break;
                case "log": LogFile = value; break;
                case "screens": Screens = value; break;
                case "testkompas": TestKompas = value; break;
            }
        }
    }

    /// <summary>Склеивает аргументы в командную строку, беря в кавычки значения с пробелами.</summary>
    public static string Join(IEnumerable<string> args)
    {
        var builder = new StringBuilder();
        foreach (string arg in args)
        {
            if (builder.Length > 0) builder.Append(' ');
            int equals = arg.IndexOf('=');
            if (equals >= 0 && arg.IndexOf(' ') >= 0)
            {
                builder.Append(arg, 0, equals + 1).Append('"').Append(arg.Substring(equals + 1).Trim('"')).Append('"');
            }
            else
            {
                builder.Append(arg);
            }
        }
        return builder.ToString();
    }
}
