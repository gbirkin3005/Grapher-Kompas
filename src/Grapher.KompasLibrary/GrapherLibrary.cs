using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Grapher.UI;
using Microsoft.Win32;

// Наружу (в COM) виден только класс библиотеки.
[assembly: ComVisible(false)]

namespace Grapher.KompasLibrary;

/// <summary>
/// Приложение-библиотека КОМПАС-3D (тип ActiveX, как примеры Step* из SDK).
/// КОМПАС находит класс по разделу реестра Kompas_Library, показывает пункт меню
/// и вызывает <see cref="ExternalRunCommand"/>, передавая объект KompasObject.
/// Окно, расчёт графика и рендерер — те же, что и в самостоятельном приложении.
/// </summary>
[ComVisible(true)]
[Guid("25A52761-85E0-453D-BA06-C9AF2727E4D9")]
[ProgId("GrapherKompas.Library")]
[ClassInterface(ClassInterfaceType.AutoDual)]
public class GrapherLibrary
{
    private const string LibraryName = "Grapher";
    private const int CommandBuild = 1;

    // Значения параметра itemType функции ExternalMenuItem.
    private const short MenuItem = 1;
    private const short EndMenu = 3;

    static GrapherLibrary()
    {
        // КОМПАС загружает сборку из произвольной папки, поэтому зависимости
        // (Grapher.Core, Newtonsoft.Json и др.) ищем рядом с библиотекой.
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromLibraryFolder;
    }

    private static Assembly ResolveFromLibraryFolder(object sender, ResolveEventArgs args)
    {
        try
        {
            string folder = Path.GetDirectoryName(typeof(GrapherLibrary).Assembly.Location);
            string path = Path.Combine(folder ?? "", new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }
        catch (Exception ex) when (ex is IOException || ex is BadImageFormatException || ex is ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Имя библиотеки в списке приложений КОМПАС.</summary>
    [return: MarshalAs(UnmanagedType.BStr)]
    public string GetLibraryName() => LibraryName;

    /// <summary>Пункты меню библиотеки. КОМПАС вызывает функцию с номерами 1, 2, … до признака конца меню.</summary>
    [return: MarshalAs(UnmanagedType.BStr)]
    public string ExternalMenuItem(short number, ref short itemType, ref short command)
    {
        if (number == 1)
        {
            itemType = MenuItem;
            command = CommandBuild;
            return "Построить график…";
        }

        itemType = EndMenu;
        command = -1;
        return string.Empty;
    }

    /// <summary>Головная функция библиотеки: выполняет команду меню.</summary>
    public void ExternalRunCommand([In] short command, [In] short mode, [In, MarshalAs(UnmanagedType.IDispatch)] object kompas_)
    {
        if (kompas_ == null || command != CommandBuild) return;

        try
        {
            KompasLibraryHost.ShowDialog(kompas_);
        }
        catch (Exception ex)
        {
            // Исключение не должно уйти в КОМПАС: это привело бы к его аварийному завершению.
            MessageBox.Show("Не удалось открыть окно построения графика.\n\n" + ex.Message, LibraryName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    #region Регистрация COM

    /// <summary>
    /// Выполняется при регистрации сборки (RegAsm). Добавляет раздел Kompas_Library — по нему
    /// КОМПАС узнаёт, что класс является его приложением, — и полный путь к mscoree.dll.
    /// </summary>
    [ComRegisterFunction]
    public static void RegisterKompasLib(Type t)
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\CLSID\{" + t.GUID + "}", true))
            {
                if (key == null) return;
                key.CreateSubKey("Kompas_Library")?.Dispose();
                using (RegistryKey server = key.OpenSubKey("InprocServer32", true))
                {
                    server?.SetValue(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mscoree.dll"));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
        {
            MessageBox.Show("При регистрации библиотеки для КОМПАС произошла ошибка (нужны права администратора):\n" + ex.Message,
                LibraryName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    [ComUnregisterFunction]
    public static void UnregisterKompasLib(Type t)
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes\CLSID\{" + t.GUID + "}", true))
            {
                key?.DeleteSubKey("Kompas_Library", false);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
        {
            // Раздел удалит RegAsm вместе с остальной регистрацией класса.
        }
    }

    #endregion
}
