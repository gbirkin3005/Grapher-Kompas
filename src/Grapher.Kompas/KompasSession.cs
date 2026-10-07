using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Kompas6API5;
using Kompas6Constants;
using KompasAPI7;

namespace Grapher.Kompas;

/// <summary>Вид активного документа КОМПАС.</summary>
public enum ActiveDocumentKind
{
    None,
    Drawing,
    Fragment,
    Other
}

/// <summary>
/// Подключение к КОМПАС-3D через COM. Основной интерфейс — API версии 7;
/// API версии 5 используется только для указания точки курсором.
/// </summary>
public sealed class KompasSession
{
    private const string ProgId5 = "KOMPAS.Application.5";
    private const string ProgId7 = "KOMPAS.Application.7";

    // HRESULT-ы COM, означающие «сервер занят» и «сервер недоступен».
    private const int RpcCallRejected = unchecked((int)0x80010001);
    private const int RpcServerCallRetryLater = unchecked((int)0x8001010A);
    private const int RpcServerUnavailable = unchecked((int)0x800706BA);
    private const int RpcDisconnected = unchecked((int)0x80010108);
    private const int RpcCallFailed = unchecked((int)0x800706BE);

    private readonly KompasObject _api5;
    private readonly IApplication _api7;

    private KompasSession(KompasObject api5, IApplication api7, bool inProcess)
    {
        _api5 = api5;
        _api7 = api7;
        InProcess = inProcess;
    }

    /// <summary>Код выполняется внутри процесса КОМПАС (приложение-библиотека).</summary>
    public bool InProcess { get; }

    internal KompasObject Api5 => _api5;
    internal IApplication Api7 => _api7;

    /// <summary>КОМПАС-3D установлен: его COM-классы зарегистрированы в системе.</summary>
    public static bool IsInstalled() => Type.GetTypeFromProgID(ProgId5) != null;

    /// <summary>Подключается к запущенному КОМПАС. Возвращает null, если он не запущен.</summary>
    public static KompasSession TryAttach()
    {
        object instance;
        try
        {
            instance = Marshal.GetActiveObject(ProgId5);
        }
        catch (COMException)
        {
            // MK_E_UNAVAILABLE: в таблице запущенных объектов КОМПАС нет.
            return null;
        }

        return Create(instance, false);
    }

    /// <summary>
    /// Запускает КОМПАС-3D как обычную программу и сразу возвращает управление.
    /// После запуска подключайтесь через <see cref="TryAttach"/>: КОМПАС появляется
    /// в таблице запущенных объектов через несколько секунд.
    /// </summary>
    /// <remarks>
    /// КОМПАС намеренно не создаётся через COM (CreateInstance): такой экземпляр принадлежит
    /// вызывающей программе и закрывается вместе с ней, а пользователю нужен обычный КОМПАС,
    /// который остаётся открытым после закрытия этого приложения.
    /// </remarks>
    public static void Launch()
    {
        string executable = FindExecutable();
        if (executable == null)
        {
            throw new KompasException(KompasErrorKind.NotInstalled,
                "КОМПАС-3D не найден: COM-класс «" + ProgId5 + "» не зарегистрирован в системе.");
        }

        try
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
        {
            throw new KompasException(KompasErrorKind.StartFailed, "Не удалось запустить КОМПАС-3D: " + ex.Message, ex);
        }
    }

    /// <summary>Путь к исполняемому файлу КОМПАС из регистрации COM-сервера; null, если КОМПАС не установлен.</summary>
    public static string FindExecutable()
    {
        Type type = Type.GetTypeFromProgID(ProgId5);
        if (type == null) return null;

        using (RegistryKey key = Registry.ClassesRoot.OpenSubKey(@"CLSID\{" + type.GUID + @"}\LocalServer32"))
        {
            string command = key?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(command)) return null;

            // Значение может быть в кавычках и с параметрами после пути.
            command = command.Trim();
            string path;
            if (command.StartsWith("\"", StringComparison.Ordinal))
            {
                int close = command.IndexOf('"', 1);
                path = close > 1 ? command.Substring(1, close - 1) : command.Trim('"');
            }
            else
            {
                int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                path = exe > 0 ? command.Substring(0, exe + 4) : command;
            }
            return File.Exists(path) ? path : null;
        }
    }

    /// <summary>Создаёт сеанс из объекта KompasObject, который КОМПАС передаёт библиотеке.</summary>
    public static KompasSession FromLibrary(object kompasObject)
    {
        if (kompasObject == null) throw new ArgumentNullException(nameof(kompasObject));
        return Create(kompasObject, true);
    }

    private static KompasSession Create(object instance, bool inProcess)
    {
        try
        {
            var api5 = (KompasObject)instance;
            var api7 = (IApplication)api5.ksGetApplication7();
            if (api7 == null)
            {
                throw new KompasException(KompasErrorKind.ApiError, "КОМПАС не предоставил интерфейс API версии 7.");
            }
            return new KompasSession(api5, api7, inProcess);
        }
        catch (InvalidCastException ex)
        {
            throw new KompasException(KompasErrorKind.ApiError,
                "Не удалось получить интерфейсы API КОМПАС. Возможно, установлена несовместимая версия.", ex);
        }
        catch (COMException ex)
        {
            throw Translate(ex);
        }
    }

    /// <summary>Строка с версией, например «КОМПАС-3D v25.0».</summary>
    public string VersionText => Guard(() =>
    {
        _api7.GetSystemVersion(out int major, out int minor, out int _, out int _);
        return "КОМПАС-3D v" + major + "." + minor;
    });

    /// <summary>Проверяет, что КОМПАС ещё запущен и отвечает.</summary>
    public bool IsAlive()
    {
        try
        {
            _api7.GetSystemVersion(out int _, out int _, out int _, out int _);
            return true;
        }
        catch (Exception ex) when (ex is COMException || ex is InvalidComObjectException)
        {
            return false;
        }
    }

    public ActiveDocumentKind GetActiveDocumentKind() => Guard(() =>
    {
        IKompasDocument document = _api7.ActiveDocument;
        if (document == null) return ActiveDocumentKind.None;
        switch (document.DocumentType)
        {
            case DocumentTypeEnum.ksDocumentDrawing:
                return ActiveDocumentKind.Drawing;
            case DocumentTypeEnum.ksDocumentFragment:
                return ActiveDocumentKind.Fragment;
            default:
                return ActiveDocumentKind.Other;
        }
    });

    /// <summary>Имя активного документа для строки состояния; пустая строка, если документа нет.</summary>
    public string GetActiveDocumentName() => Guard(() =>
    {
        IKompasDocument document = _api7.ActiveDocument;
        return document == null ? "" : DisplayName(document);
    });

    /// <summary>Имя документа; у нового несохранённого документа имени ещё нет.</summary>
    internal static string DisplayName(IKompasDocument document)
    {
        string name = document.Name;
        return string.IsNullOrEmpty(name) ? "без имени" : name;
    }

    /// <summary>Создаёт новый чертёж (или фрагмент) и делает его активным.</summary>
    public void CreateDocument(bool fragment) => Guard(() =>
    {
        DocumentTypeEnum type = fragment ? DocumentTypeEnum.ksDocumentFragment : DocumentTypeEnum.ksDocumentDrawing;
        IKompasDocument document = _api7.Documents.Add(type, true);
        if (document == null)
        {
            throw new KompasException(KompasErrorKind.ApiError, "КОМПАС не смог создать новый документ.");
        }
    });

    /// <summary>
    /// Создаёт в активном чертеже новый вид с заданным масштабом и делает его текущим.
    /// Нужен для проверки построения в видах с масштабом, отличным от 1:1.
    /// </summary>
    /// <param name="scale">Масштаб вида: 0,5 для 1:2, 2 для 2:1.</param>
    public void CreateView(string name, double scale, double x, double y) => Guard(() =>
    {
        IKompasDocument2D document = GetActiveDocument2D();
        IView view = document.ViewsAndLayersManager.Views.Add(LtViewType.vt_Normal);
        if (view == null)
        {
            throw new KompasException(KompasErrorKind.ApiError, "КОМПАС не смог создать вид.");
        }

        view.Name = name;
        view.Scale = scale;
        view.X = x;
        view.Y = y;
        view.Current = true;
        if (!view.Update())
        {
            throw new KompasException(KompasErrorKind.ApiError, "КОМПАС не смог создать вид с масштабом " + scale + ".");
        }
    });

    /// <summary>Активный 2D-документ (чертёж или фрагмент).</summary>
    internal IKompasDocument2D GetActiveDocument2D()
    {
        IKompasDocument document = _api7.ActiveDocument;
        if (document == null)
        {
            throw new KompasException(KompasErrorKind.NoActiveDocument,
                "В КОМПАС нет открытого документа. Откройте или создайте чертёж либо фрагмент.");
        }

        DocumentTypeEnum type = document.DocumentType;
        if (type != DocumentTypeEnum.ksDocumentDrawing && type != DocumentTypeEnum.ksDocumentFragment)
        {
            throw new KompasException(KompasErrorKind.Not2DDocument,
                "Активный документ КОМПАС — не чертёж и не фрагмент. График можно построить только в 2D-документе.");
        }

        return (IKompasDocument2D)document;
    }

    /// <summary>Выводит окно КОМПАС на передний план.</summary>
    public void Activate() => Guard(() =>
    {
        IntPtr handle = new IntPtr(_api5.ksGetHWindow());
        if (handle == IntPtr.Zero) return;
        if (NativeMethods.IsIconic(handle)) NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
        NativeMethods.SetForegroundWindow(handle);
    });

    /// <summary>Дескриптор главного окна КОМПАС (владелец диалогов библиотеки).</summary>
    public IntPtr MainWindowHandle => Guard(() => new IntPtr(_api5.ksGetHWindow()));

    /// <summary>
    /// Запрашивает у пользователя точку в окне активного документа.
    /// Координаты возвращаются в системе координат текущего вида.
    /// </summary>
    /// <returns>false, если пользователь отказался от указания (Esc).</returns>
    public bool PickPoint(string prompt, out double x, out double y)
    {
        double px = 0, py = 0;
        bool picked = Guard(() =>
        {
            GetActiveDocument2D();
            var document5 = (ksDocument2D)_api5.ActiveDocument2D();
            if (document5 == null)
            {
                throw new KompasException(KompasErrorKind.NoActiveDocument, "В КОМПАС нет активного 2D-документа.");
            }

            var info = (ksRequestInfo)_api5.GetParamStruct((short)StructType2DEnum.ko_RequestInfo);
            info.Init();
            info.prompt = prompt;

            int answer = document5.ksCursor(info, ref px, ref py, null);
            return answer != 0;
        });

        x = px;
        y = py;
        return picked;
    }

    /// <summary>В активном документе выполняется интерактивная команда (например, запрос точки).</summary>
    public bool IsProcessRunning() => Guard(() =>
    {
        IKompasDocument document = _api7.ActiveDocument;
        return document != null && ((IKompasDocument1)document).IsActiveProcessRunning;
    });

    /// <summary>Прерывает текущую интерактивную команду КОМПАС (то же, что клавиша Esc).</summary>
    public void StopCurrentProcess() => Guard(() =>
    {
        _api7.StopCurrentProcess(false, _api7.ActiveDocument);
    });

    /// <summary>
    /// Сохраняет активный документ в растровый файл PNG. Нужен для проверки результата построения
    /// без ручного просмотра чертежа (ключ командной строки --png).
    /// </summary>
    public bool ExportActiveDocumentToPng(string path, int resolutionDpi = 150, bool color = false) => Guard(() =>
    {
        IKompasDocument document = _api7.ActiveDocument;
        if (document == null)
        {
            throw new KompasException(KompasErrorKind.NoActiveDocument, "В КОМПАС нет открытого документа.");
        }

        var document1 = (IKompasDocument1)document;
        var parameters = (IRasterConvertParameters)document1.GetInterface(KompasAPIObjectTypeEnum.ksObjectRasterConvertParameters);
        parameters.RasterFormat = ksRasterFormatEnum.ksRasterFormatPNG;
        parameters.ColorBPP = ksColorBPPEnum.ksColorBPP_24;
        parameters.ColorType = color ? ksObjectColorTypeEnum.ksColorObject : ksObjectColorTypeEnum.ksColorBw;
        parameters.Resolution = resolutionDpi;
        return document1.SaveAsToRasterFormat(path, (RasterConvertParameters)parameters);
    });

    // ------------------------------------------------------------------ обработка ошибок COM

    internal void Guard(Action action) => Guard(() =>
    {
        action();
        return true;
    });

    /// <summary>Выполняет обращение к КОМПАС и переводит ошибки COM в понятные сообщения.</summary>
    internal T Guard<T>(Func<T> func)
    {
        try
        {
            return func();
        }
        catch (COMException ex)
        {
            throw Translate(ex);
        }
        catch (InvalidComObjectException ex)
        {
            throw new KompasException(KompasErrorKind.ConnectionLost,
                "Связь с КОМПАС-3D потеряна. Запустите КОМПАС и повторите построение.", ex);
        }
    }

    private static KompasException Translate(COMException ex)
    {
        switch (ex.ErrorCode)
        {
            case RpcCallRejected:
            case RpcServerCallRetryLater:
                return new KompasException(KompasErrorKind.Busy,
                    "КОМПАС занят: в нём открыт диалог или выполняется команда. Завершите её и повторите.", ex);
            case RpcServerUnavailable:
            case RpcDisconnected:
            case RpcCallFailed:
                return new KompasException(KompasErrorKind.ConnectionLost,
                    "Связь с КОМПАС-3D потеряна (программа была закрыта). Запустите КОМПАС и повторите построение.", ex);
            default:
                return new KompasException(KompasErrorKind.ApiError,
                    "Ошибка при обращении к КОМПАС: " + ex.Message + " (код 0x" + ex.ErrorCode.ToString("X8") + ")", ex);
        }
    }

    private static class NativeMethods
    {
        public const int SwRestore = 9;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
