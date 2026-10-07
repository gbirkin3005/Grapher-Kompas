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

    /// <summary>Открывает вкладку настроек с заданным номером.</summary>
    public void SelectSettingsTab(int index)
    {
        if (index >= 0 && index < _tabs.TabCount) _tabs.SelectedIndex = index;
    }

    // ------------------------------------------------------------------ жизненный цикл

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RestoreWindowBounds();
        // Высота строк зависит от масштаба экрана; строки пересоздаются, чтобы новая высота применилась.
        _grid.RowTemplate.Height = Font.Height + 6;
        _grid.RowHeadersWidth = TextRenderer.MeasureText("000000", Font).Width + 8;
        _grid.RebuildColumns();
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
        Version version = typeof(MainForm).Assembly.GetName().Version;
        MessageBox.Show(this,
            "Grapher " + version.ToString(3) + "\n\n" +
            "Построение графиков по точкам в чертежах и фрагментах КОМПАС-3D.\n" +
            "Данные вводятся в таблицу, вставляются из буфера обмена (Mathcad, Excel)\n" +
            "или импортируются из файлов Excel и CSV.\n\n" +
            "График рисуется объектами КОМПАС и объединяется в макроэлемент.",
            "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    // ------------------------------------------------------------------ события элементов управления

    private void WireEvents()
    {
        _buildButton.Click += (s, e) => BuildInKompas();

        _grid.DataChanged += (s, e) =>
        {
            MarkDirty();
            SchedulePreview();
        };
        _grid.StructureChanged += (s, e) => OnCurvesChanged();
        _grid.CurveColumnSelected += index => SelectCurve(index);
        _issuesList.DoubleClick += (s, e) =>
        {
            if (_issuesList.SelectedItem is IssueItem item && item.Issue.Row > 0)
            {
                _grid.GoToCell(item.Issue.Row, item.Issue.Column);
            }
        };

        // Общие настройки: любое изменение переносится в проект и обновляет предпросмотр.
        EventHandler changed = (s, e) => OnSettingChanged();
        foreach (CheckBox box in new[] { _xAuto, _yAuto, _gridOn, _axesOn, _arrowsOn, _frameOn, _labelsOn, _italic, _legendOn, _decimateOn })
        {
            box.CheckedChanged += changed;
        }
        foreach (NumberBox box in new[] { _xMin, _xMax, _yMin, _yMax, _angle, _stepX, _stepY, _axisExtension, _arrowLength, _tickLength,
                     _labelHeight, _titleHeight, _placeX, _placeY, _decimateThreshold, _decimateTolerance })
        {
            box.ValueChanged += changed;
        }
        foreach (ComboBox box in new[] { _crossing, _titlePlacement, _decimalsX, _decimalsY, _decimalSeparator })
        {
            box.SelectedIndexChanged += changed;
        }
        _fontName.TextChanged += changed;
        _titleX.TextChanged += changed;
        _titleY.TextChanged += changed;
        _everyX.ValueChanged += changed;
        _everyY.ValueChanged += changed;
        _pickPoint.CheckedChanged += changed;
        _useCoords.CheckedChanged += changed;
        _gridMode.SelectedIndexChanged += (s, e) => OnGridModeChanged();

        // Габарит и масштабный коэффициент связаны в обе стороны.
        _width.ValueChanged += (s, e) => OnSizeEdited(SizeDriver.Width, _width);
        _height.ValueChanged += (s, e) => OnSizeEdited(SizeDriver.Height, _height);
        _scaleX.ValueChanged += (s, e) => OnSizeEdited(SizeDriver.ScaleX, _scaleX);
        _scaleY.ValueChanged += (s, e) => OnSizeEdited(SizeDriver.ScaleY, _scaleY);
        _equalScale.CheckedChanged += changed;

        // Блок «Тип и цвет линии графика».
        _curveBox.SelectedIndexChanged += (s, e) =>
        {
            if (!_loading) SelectCurve(_curveBox.SelectedIndex);
        };
        _curveAdd.Click += (s, e) => AddCurve();
        _curveRemove.Click += (s, e) => RemoveCurve();
        EventHandler curveChanged = (s, e) => OnCurveSettingChanged();
        _curveName.TextChanged += curveChanged;
        _curveStyle.SelectedIndexChanged += curveChanged;
        _curveMode.SelectedIndexChanged += curveChanged;
        _curveMarker.SelectedIndexChanged += curveChanged;
        _curveMarkerSize.ValueChanged += curveChanged;
        _curveVisible.CheckedChanged += curveChanged;
        _curveColor.Click += (s, e) => ChooseCurveColor();
        _curveColorReset.Click += (s, e) => SetCurveColor(null);
    }

    private void OnSettingChanged()
    {
        if (_loading) return;
        ReadUi();
        UpdateEnabledState();
        MarkDirty();
        SchedulePreview();
    }

    /// <summary>Завершает редактирование ячейки таблицы, чтобы введённое значение попало в данные.</summary>
    private void CommitEdits()
    {
        if (_grid.IsCurrentCellInEditMode) _grid.EndEdit();
    }

    // ------------------------------------------------------------------ проект → окно

    private void WriteUi()
    {
        _loading = true;
        try
        {
            GraphSettings g = _project.Graph;

            _xAuto.Checked = _project.XAxis.AutoLimits;
            _yAuto.Checked = _project.YAxis.AutoLimits;
            _xMin.SetValue(_project.XAxis.Min);
            _xMax.SetValue(_project.XAxis.Max);
            _yMin.SetValue(_project.YAxis.Min);
            _yMax.SetValue(_project.YAxis.Max);
            _angle.SetValue(_project.Placement.AngleDeg);

            _gridOn.Checked = g.ShowGrid;
            _width.SetValue(g.WidthMm);
            _height.SetValue(g.HeightMm);
            _equalScale.Checked = g.EqualScale;
            _gridMode.SelectedIndex = (int)g.GridMode;
            _stepX.SetValue(_project.XAxis.GridStep);
            _stepY.SetValue(_project.YAxis.GridStep);
            _everyX.Value = Math.Max(1, Math.Min(100, _project.XAxis.LabelEvery));
            _everyY.Value = Math.Max(1, Math.Min(100, _project.YAxis.LabelEvery));

            _axesOn.Checked = g.ShowAxes;
            _arrowsOn.Checked = g.ShowArrows;
            _frameOn.Checked = g.ShowFrame;
            _axisExtension.SetValue(g.AxisExtensionMm);
            _arrowLength.SetValue(g.ArrowLengthMm);
            _tickLength.SetValue(g.TickLengthMm);
            _crossing.SelectedIndex = (int)g.Crossing;
            _titleX.Text = _project.XAxis.Title;
            _titleY.Text = _project.YAxis.Title;
            _titlePlacement.SelectedIndex = (int)g.TitlePlacement;
            _labelsOn.Checked = g.ShowTickLabels;
            _decimalsX.SelectedIndex = Math.Max(0, Math.Min(7, _project.XAxis.Decimals + 1));
            _decimalsY.SelectedIndex = Math.Max(0, Math.Min(7, _project.YAxis.Decimals + 1));
            _decimalSeparator.SelectedIndex = g.DecimalComma ? 0 : 1;
            _fontName.Text = g.FontName;
            _italic.Checked = g.Italic;
            _labelHeight.SetValue(g.LabelHeightMm);
            _titleHeight.SetValue(g.TitleHeightMm);

            _pickPoint.Checked = _project.Placement.PickPoint;
            _useCoords.Checked = !_project.Placement.PickPoint;
            _placeX.SetValue(_project.Placement.X);
            _placeY.SetValue(_project.Placement.Y);
            _legendOn.Checked = g.ShowLegend;
            _decimateOn.Checked = g.Decimate;
            _decimateThreshold.SetValue(g.DecimateThreshold);
            _decimateTolerance.SetValue(g.DecimateToleranceMm);

            FillCurveList();
        }
        finally
        {
            _loading = false;
        }
        ShowCurve();
        UpdateEnabledState();
    }

    // ------------------------------------------------------------------ окно → проект

    private void ReadUi()
    {
        GraphSettings g = _project.Graph;

        _project.XAxis.AutoLimits = _xAuto.Checked;
        _project.YAxis.AutoLimits = _yAuto.Checked;
        if (!_xAuto.Checked)
        {
            _project.XAxis.Min = _xMin.Value;
            _project.XAxis.Max = _xMax.Value;
        }
        if (!_yAuto.Checked)
        {
            _project.YAxis.Min = _yMin.Value;
            _project.YAxis.Max = _yMax.Value;
        }
        _project.Placement.AngleDeg = _angle.Value;

        g.ShowGrid = _gridOn.Checked;
        g.EqualScale = _equalScale.Checked;
        g.GridMode = (GridStepMode)Math.Max(0, _gridMode.SelectedIndex);
        if (g.GridMode != GridStepMode.Auto)
        {
            _project.XAxis.GridStep = _stepX.Value;
            _project.YAxis.GridStep = _stepY.Value;
        }
        _project.XAxis.LabelEvery = (int)_everyX.Value;
        _project.YAxis.LabelEvery = (int)_everyY.Value;

        g.ShowAxes = _axesOn.Checked;
        g.ShowArrows = _arrowsOn.Checked;
        g.ShowFrame = _frameOn.Checked;
        g.AxisExtensionMm = _axisExtension.Value;
        g.ArrowLengthMm = _arrowLength.Value;
        g.TickLengthMm = _tickLength.Value;
        g.Crossing = (AxesCrossing)Math.Max(0, _crossing.SelectedIndex);
        _project.XAxis.Title = _titleX.Text;
        _project.YAxis.Title = _titleY.Text;
        g.TitlePlacement = (AxisTitlePlacement)Math.Max(0, _titlePlacement.SelectedIndex);
        g.ShowTickLabels = _labelsOn.Checked;
        _project.XAxis.Decimals = _decimalsX.SelectedIndex - 1;
        _project.YAxis.Decimals = _decimalsY.SelectedIndex - 1;
        g.DecimalComma = _decimalSeparator.SelectedIndex != 1;
        if (!string.IsNullOrWhiteSpace(_fontName.Text)) g.FontName = _fontName.Text.Trim();
        g.Italic = _italic.Checked;
        g.LabelHeightMm = _labelHeight.Value;
        g.TitleHeightMm = _titleHeight.Value;

        _project.Placement.PickPoint = _pickPoint.Checked;
        _project.Placement.X = _placeX.Value;
        _project.Placement.Y = _placeY.Value;
        g.ShowLegend = _legendOn.Checked;
        g.Decimate = _decimateOn.Checked;
        g.DecimateThreshold = (int)Math.Max(2, Math.Min(int.MaxValue, _decimateThreshold.Value));
        g.DecimateToleranceMm = _decimateTolerance.Value;
    }

    private void UpdateEnabledState()
    {
        _xMin.Enabled = _xMax.Enabled = !_xAuto.Checked;
        _yMin.Enabled = _yMax.Enabled = !_yAuto.Checked;

        bool manualStep = _gridMode.SelectedIndex > 0;
        _stepX.Enabled = _stepY.Enabled = manualStep;

        _arrowsOn.Enabled = _axesOn.Checked;
        _axisExtension.Enabled = _arrowLength.Enabled = _axesOn.Checked && _arrowsOn.Checked;
        _decimalsX.Enabled = _decimalsY.Enabled = _decimalSeparator.Enabled = _labelsOn.Checked;
        _placeX.Enabled = _placeY.Enabled = _useCoords.Checked;
        _decimateThreshold.Enabled = _decimateTolerance.Enabled = _decimateOn.Checked;
        _curveRemove.Enabled = _project.Curves.Count > 1;
        _removeCurveMenuItem.Enabled = _project.Curves.Count > 1;
        _curveMarkerSize.Enabled = _curveMarker.SelectedIndex > 0;
        _curveColorReset.Enabled = _curveColor.Value.HasValue;
    }

    // ------------------------------------------------------------------ габариты и масштаб

    private void OnSizeEdited(SizeDriver driver, NumberBox source)
    {
        if (_loading || _lastResult == null) return;

        double rangeX = _lastResult.XMax - _lastResult.XMin, rangeY = _lastResult.YMax - _lastResult.YMin;
        double width = _project.Graph.WidthMm, height = _project.Graph.HeightMm;
        GraphSizeSolver.Solve(driver, source.Value, rangeX, rangeY, _equalScale.Checked, ref width, ref height);

        _project.Graph.WidthMm = GraphSizeSolver.Clamp(width);
        _project.Graph.HeightMm = GraphSizeSolver.Clamp(height);
        if (source != _width) _width.SetValue(_project.Graph.WidthMm);
        if (source != _height) _height.SetValue(_project.Graph.HeightMm);

        MarkDirty();
        SchedulePreview();
    }

    /// <summary>При смене способа задания шага пересчитывает текущий шаг в новые единицы, чтобы сетка не «прыгала».</summary>
    private void OnGridModeChanged()
    {
        if (_loading) return;

        var mode = (GridStepMode)Math.Max(0, _gridMode.SelectedIndex);
        if (_lastResult?.TicksX != null && _lastResult.TicksY != null && mode != GridStepMode.Auto)
        {
            double stepX = _lastResult.TicksX.Step, stepY = _lastResult.TicksY.Step;
            double rangeX = _lastResult.XMax - _lastResult.XMin, rangeY = _lastResult.YMax - _lastResult.YMin;
            if (stepX > 0 && stepY > 0)
            {
                switch (mode)
                {
                    case GridStepMode.Units:
                        _stepX.SetValue(stepX);
                        _stepY.SetValue(stepY);
                        break;
                    case GridStepMode.Millimeters:
                        _stepX.SetValue(Math.Round(stepX * _lastResult.ScaleX, 6));
                        _stepY.SetValue(Math.Round(stepY * _lastResult.ScaleY, 6));
                        break;
                    case GridStepMode.Divisions:
                        _stepX.SetValue(Math.Max(1, Math.Round(rangeX / stepX)));
                        _stepY.SetValue(Math.Max(1, Math.Round(rangeY / stepY)));
                        break;
                }
            }
        }
        OnSettingChanged();
    }

    // ------------------------------------------------------------------ кривые

    private void FillCurveList()
    {
        _curveBox.Items.Clear();
        for (int i = 0; i < _project.Curves.Count; i++)
        {
            _curveBox.Items.Add(CurveTitle(i));
        }
        if (_selectedCurve >= _project.Curves.Count) _selectedCurve = _project.Curves.Count - 1;
        if (_selectedCurve < 0) _selectedCurve = 0;
        if (_curveBox.Items.Count > 0) _curveBox.SelectedIndex = _selectedCurve;
    }

    private string CurveTitle(int index)
    {
        string name = _project.Curves[index].Name;
        return (index + 1).ToString(CultureInfo.CurrentCulture) + ". " + (string.IsNullOrWhiteSpace(name) ? "(без названия)" : name);
    }

    private void SelectCurve(int index)
    {
        if (index < 0 || index >= _project.Curves.Count || index == _selectedCurve && _curveBox.SelectedIndex == index) return;
        _selectedCurve = index;
        ShowCurve();
    }

    /// <summary>Показывает настройки выбранной кривой в блоке «Тип и цвет линии графика».</summary>
    private void ShowCurve()
    {
        if (_project.Curves.Count == 0) return;
        if (_selectedCurve >= _project.Curves.Count) _selectedCurve = 0;

        CurveSettings curve = _project.Curves[_selectedCurve];
        _loading = true;
        try
        {
            if (_curveBox.SelectedIndex != _selectedCurve && _selectedCurve < _curveBox.Items.Count)
            {
                _curveBox.SelectedIndex = _selectedCurve;
            }
            _curveName.Text = curve.Name;
            _curveStyle.SelectedStyle = curve.LineStyle;
            _curveMode.SelectedIndex = curve.Mode == CurveMode.Smooth ? 0 : 1;
            _curveMarker.SelectedIndex = MarkerToIndex(curve.Marker);
            _curveMarkerSize.SetValue(curve.MarkerSizeMm);
            _curveVisible.Checked = curve.Visible;
            ShowCurveColor(curve);
        }
        finally
        {
            _loading = false;
        }
        UpdateEnabledState();
    }

    // ------------------------------------------------------------------ цвет линии кривой

    /// <summary>Пользовательские цвета диалога выбора цвета — общие на время работы программы.</summary>
    private static int[] _customColors;

    private static Color ToColor(int rgb) =>
        Color.FromArgb(ColorValue.Red(rgb), ColorValue.Green(rgb), ColorValue.Blue(rgb));

    private void ShowCurveColor(CurveSettings curve)
    {
        int? rgb = ColorValue.Parse(curve.Color);
        Color? color = rgb.HasValue ? ToColor(rgb.Value) : (Color?)null;
        _curveColor.Value = color;
        _curveStyle.SampleColor = color;
    }

    private void ChooseCurveColor()
    {
        if (_selectedCurve < 0 || _selectedCurve >= _project.Curves.Count) return;

        int? current = ColorValue.Parse(_project.Curves[_selectedCurve].Color);
        using (var dialog = new ColorDialog { FullOpen = true, AnyColor = true })
        {
            // Если цвет ещё не задан, диалог открывается на синем цвете основной линии КОМПАС.
            dialog.Color = current.HasValue ? ToColor(current.Value) : Color.FromArgb(0, 128, 255);
            if (_customColors != null) dialog.CustomColors = _customColors;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            _customColors = dialog.CustomColors;
            SetCurveColor(ColorValue.Format(ColorValue.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B)));
        }
    }

    /// <param name="color">Цвет «#RRGGBB» либо null — вернуть цвет стиля КОМПАС.</param>
    private void SetCurveColor(string color)
    {
        if (_selectedCurve < 0 || _selectedCurve >= _project.Curves.Count) return;

        CurveSettings curve = _project.Curves[_selectedCurve];
        if (curve.Color == color) return;

        curve.Color = color;
        ShowCurveColor(curve);
        UpdateEnabledState();
        MarkDirty();
        SchedulePreview();
    }

    private static readonly MarkerShape[] MarkerOrder =
    {
        MarkerShape.None, MarkerShape.Circle, MarkerShape.Square, MarkerShape.Triangle, MarkerShape.Diamond, MarkerShape.Cross
    };

    private static int MarkerToIndex(MarkerShape shape) => Math.Max(0, Array.IndexOf(MarkerOrder, shape));

    private void OnCurveSettingChanged()
    {
        if (_loading || _selectedCurve < 0 || _selectedCurve >= _project.Curves.Count) return;

        CurveSettings curve = _project.Curves[_selectedCurve];
        bool renamed = curve.Name != _curveName.Text;
        curve.Name = _curveName.Text;
        curve.LineStyle = _curveStyle.SelectedStyle;
        curve.Mode = _curveMode.SelectedIndex == 1 ? CurveMode.Polyline : CurveMode.Smooth;
        curve.Marker = MarkerOrder[Math.Max(0, Math.Min(MarkerOrder.Length - 1, _curveMarker.SelectedIndex))];
        curve.MarkerSizeMm = _curveMarkerSize.Value;
        curve.Visible = _curveVisible.Checked;

        if (renamed)
        {
            _loading = true;
            _curveBox.Items[_selectedCurve] = CurveTitle(_selectedCurve);
            _loading = false;
            _grid.RefreshHeaders();
        }

        UpdateEnabledState();
        MarkDirty();
        SchedulePreview();
    }

    private void AddCurve()
    {
        CommitEdits();
        _selectedCurve = _grid.AddCurve();
        _loading = true;
        FillCurveList();
        _loading = false;
        ShowCurve();
        MarkDirty();
        SchedulePreview();
        SetStatus("Добавлен столбец «" + _project.Curves[_selectedCurve].Name + "». Введите или вставьте значения Y.");
    }

    private void RemoveCurve()
    {
        if (_project.Curves.Count <= 1) return;

        string name = _project.Curves[_selectedCurve].Name;
        DialogResult answer = MessageBox.Show(this, "Удалить кривую «" + name + "» вместе со столбцом данных?", AppTitle,
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;

        _grid.RemoveCurve(_selectedCurve);
        _selectedCurve = Math.Min(_selectedCurve, _project.Curves.Count - 1);
        _loading = true;
        FillCurveList();
        _loading = false;
        ShowCurve();
        MarkDirty();
        SchedulePreview();
    }

    /// <summary>Набор кривых изменился после вставки или импорта.</summary>
    private void OnCurvesChanged()
    {
        _loading = true;
        FillCurveList();
        _loading = false;
        ShowCurve();
        UseHeadersAsTitles();
    }

    /// <summary>
    /// Если подписи осей ещё не заданы, а таблица пришла с заголовками, берём заголовки:
    /// столбцы «x, мм» и «F, Н» из Mathcad или Excel сразу становятся подписями осей.
    /// </summary>
    private void UseHeadersAsTitles()
    {
        bool changed = false;
        string xName = _project.Data.XName;
        if (string.IsNullOrWhiteSpace(_project.XAxis.Title) && !string.IsNullOrWhiteSpace(xName) && xName != "X")
        {
            _project.XAxis.Title = xName;
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(_project.YAxis.Title) && _project.Curves.Count == 1)
        {
            string yName = _project.Curves[0].Name;
            if (!string.IsNullOrWhiteSpace(yName) && yName != "Y" && yName != "Y1")
            {
                _project.YAxis.Title = yName;
                changed = true;
            }
        }
        if (!changed) return;

        _loading = true;
        _titleX.Text = _project.XAxis.Title;
        _titleY.Text = _project.YAxis.Title;
        _loading = false;
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

    /// <summary>Показывает вычисленные величины: пределы в режиме «авто», размах, масштабные коэффициенты, шаг сетки.</summary>
    private void ShowComputedValues(BuildResult result)
    {
        _loading = true;
        try
        {
            if (_xAuto.Checked)
            {
                _xMin.SetValue(result.XMin);
                _xMax.SetValue(result.XMax);
            }
            if (_yAuto.Checked)
            {
                _yMin.SetValue(result.YMin);
                _yMax.SetValue(result.YMax);
            }

            _rangeX.Text = NumberFormatter.ForInput(result.XMax - result.XMin);
            _rangeY.Text = NumberFormatter.ForInput(result.YMax - result.YMin);
            _scaleX.SetValue(result.ScaleX);
            _scaleY.SetValue(result.ScaleY);

            // При одинаковом масштабе высота следует за шириной.
            if (_project.Graph.EqualScale && Math.Abs(_project.Graph.HeightMm - result.HeightMm) > 1e-9)
            {
                _project.Graph.HeightMm = result.HeightMm;
                _height.SetValue(result.HeightMm);
            }

            if (_gridMode.SelectedIndex <= 0 && result.TicksX != null && result.TicksY != null)
            {
                _stepX.SetValue(result.TicksX.Step);
                _stepY.SetValue(result.TicksY.Step);
            }
        }
        finally
        {
            _loading = false;
        }

        string info = string.Format(CultureInfo.CurrentCulture,
            "X: {0} … {1};  Y: {2} … {3};  поле {4}×{5} мм;  точек: {6}",
            NumberFormatter.ForInput(result.XMin), NumberFormatter.ForInput(result.XMax),
            NumberFormatter.ForInput(result.YMin), NumberFormatter.ForInput(result.YMax),
            NumberFormatter.ForInput(result.WidthMm, 2), NumberFormatter.ForInput(result.HeightMm, 2),
            result.SourcePointCount);
        if (result.Decimated)
        {
            info += string.Format(CultureInfo.CurrentCulture, ", после прореживания вершин: {0}", result.CurveVertexCount);
        }
        _infoLabel.Text = info;
    }

    private sealed class IssueItem
    {
        public DataIssue Issue;
        public override string ToString() =>
            (Issue.Severity == IssueSeverity.Error ? "Ошибка: " : "Внимание: ") + Issue.Message;
    }

    private void ShowIssues(BuildResult result)
    {
        _issuesList.BeginUpdate();
        _issuesList.Items.Clear();
        foreach (DataIssue issue in result.Issues)
        {
            // Пустая таблица — не ошибка для показа: об этом говорит надпись в предпросмотре.
            if (result.SourcePointCount == 0 && issue.Row == 0 && issue.Column < 0 && issue.Message.Contains("пуста")) continue;
            _issuesList.Items.Add(new IssueItem { Issue = issue });
        }
        _issuesList.EndUpdate();
        _issuesList.Visible = _issuesList.Items.Count > 0;
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
            if (!_session.PickPoint("Укажите точку вставки графика (левый нижний угол поля)", out double x, out double y))
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
            picked = session.PickPoint("Укажите точку вставки графика (левый нижний угол поля)", out x, out y);
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
