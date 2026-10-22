using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Grapher.Core.Build;
using Grapher.Core.Geometry;
using Grapher.Core.Import;
using Grapher.Core.Model;
using Grapher.Core.Numerics;
using Grapher.Core.Parsing;
using Grapher.Core.Scene;
using Grapher.Core.Serialization;
using Grapher.Kompas;
using Grapher.UI.Controls;

namespace Grapher.UI;

/// <summary>
/// Главное окно: таблица данных, настройки графика, предпросмотр и построение в КОМПАС.
/// Используется и самостоятельным приложением, и библиотекой КОМПАС.
/// </summary>
public sealed partial class MainForm : Form
{
    private const string AppTitle = "Grapher";
    private const string MacroName = "График";

    private readonly AppSettings _settings;
    private readonly KompasSession _hostSession;
    private readonly Timer _previewTimer = new Timer { Interval = 120 };
    private readonly Timer _kompasTimer = new Timer { Interval = 2500 };

    private GraphProject _project;
    private BuildResult _lastResult;
    private KompasSession _session;
    private string _filePath;
    private bool _dirty;
    private bool _loading;
    private bool _busy;
    private int _selectedCurve;

    /// <param name="hostSession">
    /// Подключение, если окно открыто из библиотеки внутри КОМПАС; null — самостоятельное приложение.
    /// </param>
    public MainForm(KompasSession hostSession = null)
    {
        _hostSession = hostSession;
        _session = hostSession;
        _settings = AppSettings.Load();

        BuildLayout();
        WireEvents();

        _project = CreateEmptyProject(_settings.Project);
        ShowProject();

        _previewTimer.Tick += (s, e) =>
        {
            _previewTimer.Stop();
            UpdatePreview();
        };
        _kompasTimer.Tick += (s, e) => RefreshKompasStatus();
    }

    /// <summary>
    /// В режиме библиотеки: пользователь нажал «Построить» с указанием точки курсором.
    /// Окно закрывается с результатом Retry, вызывающий код выполняет <see cref="CompletePendingBuild"/> и открывает окно снова.
    /// </summary>
    public bool HasPendingBuild { get; private set; }

    public GraphProject Project => _project;

    /// <summary>Запоминать настройки и положение окна при закрытии (отключается для служебных запусков).</summary>
    public bool SaveSettingsOnClose { get; set; } = true;

    /// <summary>Показывает в окне заданный проект как новый, без имени файла.</summary>
    public void LoadProject(GraphProject project)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        SetProject(project, null);
    }

    /// <summary>Заменяет таблицу текстом в том виде, в каком он приходит из буфера обмена.</summary>
    public void PasteTableText(string text) => _grid.PasteText(text, true);

    /// <summary>Заменяет таблицу импортированными данными.</summary>
    public void LoadTable(ImportedTable table) => _grid.LoadTable(table);

    /// <summary>Немедленно пересчитывает график (без задержки предпросмотра) и возвращает результат.</summary>
    public BuildResult Rebuild()
    {
        _previewTimer.Stop();
        UpdatePreview();
        return _lastResult;
    }

    /// <summary>Выполняет то же, что кнопка «Построить в КОМПАС».</summary>
    public void BuildNow() => BuildInKompas();

    /// <summary>Текст строки состояния (результат последнего действия).</summary>
    public string StatusText => _statusMessage.Text;

    // ------------------------------------------------------------------ жизненный цикл

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RestoreWindowBounds();
        // Высота строк зависит от масштаба экрана; строки пересоздаются, чтобы новая высота применилась.
        _grid.RowTemplate.Height = Font.Height + 6;
        _grid.RowHeadersWidth = TextRenderer.MeasureText("000000", Font).Width + 8;
        _grid.RebuildColumns();
        ScaleSettingsLayout();
        FitSettings(true);
        UpdatePreview();
        RefreshKompasStatus();
        _kompasTimer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Закрытие ради указания точки в КОМПАС — не выход из программы.
        if (!HasPendingBuild)
        {
            if (!ConfirmDiscardChanges())
            {
                e.Cancel = true;
                return;
            }
            if (SaveSettingsOnClose) SaveSettings();
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (!HasPendingBuild)
        {
            _previewTimer.Dispose();
            _kompasTimer.Dispose();
        }
        base.OnFormClosed(e);
    }

    private void SaveSettings()
    {
        ReadUi();
        _settings.Project = _project;
        _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
        _settings.WindowBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.Save();
    }

    private void RestoreWindowBounds()
    {
        Rectangle bounds = _settings.WindowBounds;
        if (bounds.Width >= MinimumSize.Width && bounds.Height >= MinimumSize.Height)
        {
            // Окно восстанавливаем, только если оно попадает на подключённый экран.
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.IntersectsWith(bounds))
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = bounds;
                    break;
                }
            }
        }
        if (_settings.WindowMaximized) WindowState = FormWindowState.Maximized;
    }


    // ------------------------------------------------------------------ проект

    /// <summary>Новый проект с пустой таблицей; оформление берётся из последних настроек.</summary>
    private static GraphProject CreateEmptyProject(GraphProject settingsSource)
    {
        GraphProject project = settingsSource != null ? ProjectSerializer.CloneSettingsOnly(settingsSource) : new GraphProject();

        // То, что относится к конкретным данным, в новый график не переносится.
        project.XAxis.AutoLimits = true;
        project.YAxis.AutoLimits = true;
        project.XAxis.Title = "";
        project.YAxis.Title = "";
        project.XAxis.Unit = "";
        project.YAxis.Unit = "";
        project.XAxis.Numbers = NumberStyle.Decimal;
        project.ExtraAxes.Clear();
        project.Ternary.TitleC = "";
        project.Data.XName = "X";
        project.Curves.Add(new CurveSettings { Name = "Y" });
        return project;
    }

    private void SetProject(GraphProject project, string filePath)
    {
        _project = project;
        if (_project.Curves.Count == 0) _project.Curves.Add(new CurveSettings { Name = "Y" });
        _filePath = filePath;
        _dirty = false;
        _selectedCurve = 0;
        _axisIndex = -1;
        ShowProject();
        UpdatePreview();
    }

    private void ShowProject()
    {
        _grid.Bind(_project);
        WriteUi();
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        string name = string.IsNullOrEmpty(_filePath) ? "новый график" : Path.GetFileName(_filePath);
        Text = name + (_dirty ? "*" : "") + " — " + AppTitle;
    }

    private void MarkDirty()
    {
        if (_dirty) return;
        _dirty = true;
        UpdateTitle();
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_dirty) return true;

        DialogResult answer = MessageBox.Show(this, "Сохранить изменения в проекте графика?", AppTitle,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false;
        return answer == DialogResult.No || SaveProject(false);
    }

    private void NewProject()
    {
        if (!ConfirmDiscardChanges()) return;
        ReadUi();
        SetProject(CreateEmptyProject(_project), null);
        SetStatus("Создан новый график. Вставьте данные из буфера обмена (Ctrl+V) или импортируйте файл.");
    }

    /// <summary>Открывает один из встроенных примеров как новый график.</summary>
    private void LoadSample(string name)
    {
        if (!ConfirmDiscardChanges()) return;
        GraphProject sample = SampleData.ByName(name);
        if (sample == null) return;

        // Способ указания точки вставки остаётся таким, какой выбран в окне.
        sample.Placement = _project.Placement.Clone();
        sample.Placement.AngleDeg = 0;
        SetProject(sample, null);
        SetStatus("Открыт пример. Замените данные в таблице своими; настройки оформления — на вкладках.");
    }

    private void OpenProject()
    {
        if (!ConfirmDiscardChanges()) return;
        using (var dialog = new OpenFileDialog { Filter = ProjectSerializer.FileFilter, Title = "Открыть проект графика", InitialDirectory = InitialDirectory() })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            OpenProjectFile(dialog.FileName);
        }
    }

    /// <summary>Открывает проект из файла (также используется при запуске с именем файла).</summary>
    public bool OpenProjectFile(string path)
    {
        try
        {
            GraphProject project = ProjectSerializer.Load(path);
            _settings.LastDirectory = Path.GetDirectoryName(path);
            SetProject(project, path);
            SetStatus("Открыт проект «" + Path.GetFileName(path) + "».");
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Не удалось открыть проект «" + Path.GetFileName(path) + "».\n\n" + ex.Message, AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
    }

    private bool SaveProject(bool saveAs)
    {
        CommitEdits();
        ReadUi();

        string path = _filePath;
        if (saveAs || string.IsNullOrEmpty(path))
        {
            using (var dialog = new SaveFileDialog
            {
                Filter = ProjectSerializer.FileFilter,
                Title = "Сохранить проект графика",
                InitialDirectory = InitialDirectory(),
                FileName = string.IsNullOrEmpty(_filePath) ? "График" + ProjectSerializer.FileExtension : Path.GetFileName(_filePath),
                AddExtension = false,
                OverwritePrompt = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                path = dialog.FileName;
                if (string.IsNullOrEmpty(Path.GetExtension(path))) path += ProjectSerializer.FileExtension;
            }
        }

        try
        {
            ProjectSerializer.Save(_project, path);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Не удалось сохранить проект.\n\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        _filePath = path;
        _settings.LastDirectory = Path.GetDirectoryName(path);
        _dirty = false;
        UpdateTitle();
        SetStatus("Проект сохранён: " + path);
        return true;
    }

    private string InitialDirectory()
    {
        string directory = _settings.LastDirectory;
        return !string.IsNullOrEmpty(directory) && Directory.Exists(directory)
            ? directory
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void ClearTable()
    {
        if (_grid.IsDataEmpty()) return;
        DialogResult answer = MessageBox.Show(this, "Удалить все данные из таблицы?", AppTitle,
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.OK) _grid.ClearAll();
    }

    private void ShowAbout()
    {
        using (var dialog = new AboutDialog()) dialog.ShowDialog(this);
    }

    private void ImportFile()
    {
        using (var dialog = new OpenFileDialog { Filter = SpreadsheetImporter.FileFilter, Title = "Импорт данных", InitialDirectory = InitialDirectory() })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            ImportFromFile(dialog.FileName);
        }
    }

    /// <summary>Открывает диалог импорта для файла Excel или CSV.</summary>
    public void ImportFromFile(string path)
    {
        ImportedTable table = ImportDialog.Show(this, path);
        if (table == null) return;

        _settings.LastDirectory = Path.GetDirectoryName(path);
        _grid.LoadTable(table);
        SetStatus(string.Format(CultureInfo.CurrentCulture, "Импортировано из «{0}»: строк — {1}, кривых — {2}.",
            Path.GetFileName(path), table.Rows.Count, table.YNames.Count));
    }

    // ------------------------------------------------------------------ предпросмотр

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private void UpdatePreview()
    {
        BuildResult result;
        try
        {
            result = GraphBuilder.Build(_project);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is ArithmeticException)
        {
            _preview.Scene = null;
            _preview.Message = "Не удалось построить график: " + ex.Message;
            return;
        }

        _lastResult = result;

        // В предпросмотре график показан с тем же поворотом, с каким попадёт на чертёж.
        _preview.Scene = result.Scene?.Transform(new Transform2D(0, 0, _project.Placement.AngleDeg));
        _preview.Message = result.SourcePointCount == 0 ? "Нет данных. Вставьте таблицу из буфера обмена (Ctrl+V) или импортируйте файл." : null;

        ShowComputedValues(result);
        ShowIssues(result);
    }

    private void SetStatus(string text)
    {
        _statusMessage.Text = text;
    }

    // ------------------------------------------------------------------ КОМПАС

    private void RefreshKompasStatus()
    {
        if (_busy) return;
        try
        {
            if (_hostSession == null && (_session == null || !_session.IsAlive()))
            {
                _session = KompasSession.TryAttach();
            }

            if (_session == null)
            {
                _statusKompas.Text = KompasSession.IsInstalled() ? "КОМПАС-3D не запущен" : "КОМПАС-3D не установлен";
                _statusKompas.ForeColor = Color.Firebrick;
                return;
            }

            string document;
            switch (_session.GetActiveDocumentKind())
            {
                case ActiveDocumentKind.Drawing:
                    document = "чертёж «" + _session.GetActiveDocumentName() + "»";
                    break;
                case ActiveDocumentKind.Fragment:
                    document = "фрагмент «" + _session.GetActiveDocumentName() + "»";
                    break;
                case ActiveDocumentKind.Other:
                    document = "активный документ — не чертёж";
                    break;
                default:
                    document = "нет открытого документа";
                    break;
            }
            _statusKompas.Text = _session.VersionText + ": " + document;
            _statusKompas.ForeColor = SystemColors.ControlText;
        }
        catch (KompasException ex)
        {
            if (ex.Kind == KompasErrorKind.ConnectionLost && _hostSession == null) _session = null;
            _statusKompas.Text = ex.Kind == KompasErrorKind.Busy ? "КОМПАС-3D занят" : "КОМПАС-3D: нет связи";
            _statusKompas.ForeColor = Color.Firebrick;
        }
    }

    /// <summary>Строит график в активном документе КОМПАС.</summary>
    private void BuildInKompas()
    {
        if (_busy) return;
        CommitEdits();
        ReadUi();
        UpdatePreview();

        BuildResult result = _lastResult;
        if (result?.Scene == null) return;

        if (result.HasErrors)
        {
            var errors = new List<string>();
            foreach (DataIssue issue in result.Issues)
            {
                if (issue.Severity == IssueSeverity.Error && errors.Count < 8) errors.Add("• " + issue.Message);
            }
            MessageBox.Show(this, "График не построен: сначала исправьте ошибки.\n\n" + string.Join("\n", errors), AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _busy = true;
        try
        {
            KompasSession session = EnsureSession();
            if (session == null || !EnsureDocument(session)) return;

            if (_project.Placement.PickPoint)
            {
                if (_hostSession != null)
                {
                    // Внутри КОМПАС модальное окно мешает указанию точки: закрываем его,
                    // точку и построение выполняет вызывающий код, затем окно открывается снова.
                    HasPendingBuild = true;
                    DialogResult = DialogResult.Retry;
                    return;
                }
                if (!PickPointFromWindow(session)) return;
            }

            Render(session, result);
        }
        catch (KompasException ex)
        {
            ReportKompasError(ex);
        }
        finally
        {
            _busy = false;
        }
        RefreshKompasStatus();
    }

    /// <summary>
    /// Режим библиотеки: указывает точку в окне КОМПАС и строит график.
    /// Вызывается, когда окно закрыто (см. <see cref="HasPendingBuild"/>).
    /// </summary>
    public void CompletePendingBuild()
    {
        if (!HasPendingBuild) return;
        HasPendingBuild = false;
        DialogResult = DialogResult.None;

        BuildResult result = _lastResult;
        if (result?.Scene == null || _session == null) return;

        try
        {
            if (!_session.PickPoint(PickPrompt(), out double x, out double y))
            {
                SetStatus("Указание точки отменено, график не построен.");
                return;
            }
            StorePickedPoint(x, y);
            Render(_session, result);
        }
        catch (KompasException ex)
        {
            ReportKompasError(ex);
        }
    }

    private bool PickPointFromWindow(KompasSession session)
    {
        FormWindowState previous = WindowState;
        bool picked;
        double x, y;
        try
        {
            // Убираем своё окно с экрана, чтобы оно не закрывало чертёж.
            WindowState = FormWindowState.Minimized;
            session.Activate();
            picked = session.PickPoint(PickPrompt(), out x, out y);
        }
        finally
        {
            NativeMethods.ShowWindow(Handle, previous == FormWindowState.Maximized ? NativeMethods.SwShowMaximized : NativeMethods.SwShowNoActivate);
        }

        if (!picked)
        {
            Activate();
            SetStatus("Указание точки отменено, график не построен.");
            return false;
        }

        StorePickedPoint(x, y);
        return true;
    }

    private void StorePickedPoint(double x, double y)
    {
        _project.Placement.X = x;
        _project.Placement.Y = y;
        _placeX.SetValue(x);
        _placeY.SetValue(y);
    }

    private void Render(KompasSession session, BuildResult result)
    {
        Cursor previous = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;
        RenderResult rendered;
        try
        {
            rendered = KompasRenderer.Render(session, result.Scene, _project.Placement.X, _project.Placement.Y,
                _project.Placement.AngleDeg, MacroName);
        }
        finally
        {
            Cursor.Current = previous;
        }

        string status = string.Format(CultureInfo.CurrentCulture, "График построен в документе «{0}»: объектов — {1}{2}.",
            rendered.DocumentName, rendered.ObjectCount, rendered.Grouped ? ", объединены в макроэлемент" : "");
        if (Math.Abs(rendered.ViewScale - 1) > 1e-9)
        {
            status += string.Format(CultureInfo.CurrentCulture, " Учтён масштаб вида {0}.", FormatViewScale(rendered.ViewScale));
        }
        SetStatus(status);

        if (rendered.Warnings.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", rendered.Warnings), AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        if (_hostSession == null && _project.Placement.PickPoint)
        {
            // После указания точки оставляем на переднем плане КОМПАС, чтобы был виден результат.
            session.Activate();
        }
    }

    private static string FormatViewScale(double scale) =>
        scale >= 1
            ? NumberFormatter.ForInput(scale, 3) + ":1"
            : "1:" + NumberFormatter.ForInput(1 / scale, 3);

    /// <summary>Возвращает подключение к КОМПАС; при необходимости предлагает запустить его.</summary>
    private KompasSession EnsureSession()
    {
        if (_hostSession != null) return _hostSession;

        if (_session != null && _session.IsAlive()) return _session;
        _session = KompasSession.TryAttach();
        if (_session != null) return _session;

        if (!KompasSession.IsInstalled())
        {
            MessageBox.Show(this, "КОМПАС-3D не найден на этом компьютере: его COM-компоненты не зарегистрированы.\n\n" +
                                  "Установите КОМПАС-3D или запустите его хотя бы один раз от имени текущего пользователя.",
                AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        DialogResult answer = MessageBox.Show(this, "КОМПАС-3D не запущен. Запустить его сейчас?", AppTitle,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return null;

        KompasSession.Launch();
        using (var wait = new WaitForKompasDialog())
        {
            if (wait.ShowDialog(this) != DialogResult.OK) return null;
            _session = wait.Session;
        }
        Activate();
        return _session;
    }

    /// <summary>Проверяет, что в КОМПАС открыт чертёж или фрагмент; иначе предлагает создать документ.</summary>
    private bool EnsureDocument(KompasSession session)
    {
        ActiveDocumentKind kind = session.GetActiveDocumentKind();
        if (kind == ActiveDocumentKind.Drawing || kind == ActiveDocumentKind.Fragment) return true;

        string reason = kind == ActiveDocumentKind.None
            ? "В КОМПАС нет открытого документа."
            : "Активный документ КОМПАС — не чертёж и не фрагмент (график строится только в 2D-документе).";
        DialogResult answer = MessageBox.Show(this,
            reason + "\n\nСоздать новый документ?\n\n«Да» — чертёж,  «Нет» — фрагмент,  «Отмена» — не строить.",
            AppTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false;

        session.CreateDocument(answer == DialogResult.No);
        return true;
    }

    private void ReportKompasError(KompasException ex)
    {
        if (ex.Kind == KompasErrorKind.ConnectionLost && _hostSession == null) _session = null;
        SetStatus("График не построен: " + ex.Message);
        MessageBox.Show(this, ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static class NativeMethods
    {
        public const int SwShowMaximized = 3;
        public const int SwShowNoActivate = 4;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
