using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Grapher.UI.Controls;
using Grapher.UI.Rendering;

namespace Grapher.UI;

// Раскладка главного окна. Слева — таблица данных, вид диаграммы и настройки: дерево элементов диаграммы
// (как «Дерево построения» в КОМПАС) и параметры выбранного элемента. Справа — предпросмотр.
public sealed partial class MainForm
{
    private const int LeftPanelWidth = 800;

    /// <summary>Начальная высота блока настроек; после загрузки окна подгоняется по самой высокой странице.</summary>
    private const int SettingsHeight = 470;

    private const int TreeWidth = 190;

    /// <summary>Сколько места по высоте остаётся таблице данных, когда окно мало (при 100 % масштабе экрана).</summary>
    private const int MinGridHeight = 110;

    // Строка меню и строка состояния
    private MenuStrip _menu;
    private ToolStripMenuItem _removeCurveMenuItem;
    private StatusStrip _statusStrip;
    private ToolStripStatusLabel _statusKompas;
    private ToolStripStatusLabel _statusMessage;

    // Данные, вид диаграммы, предпросмотр
    private Panel _leftPanel;
    private DataTableView _grid;
    private Control _kindRow;
    private ComboBox _kind;
    private Label _kindHint;
    private PreviewControl _preview;
    private Label _infoLabel;
    private ListBox _issuesList;
    private Button _buildButton;

    // Настройки: дерево и страницы параметров
    private Control _settingsArea;
    private TreeView _tree;
    private Label _pageTitle;
    private Panel _pageHost;
    private int _settingsNeeded;
    private readonly List<Label> _captionLabels = new List<Label>();
    private readonly List<Label> _wrapLabels = new List<Label>();

    /// <summary>Строка страницы параметров: подпись слева, элементы справа.</summary>
    private sealed class Field
    {
        public Label Caption;
        public Control Content;

        public bool Visible
        {
            set
            {
                Caption.Visible = value;
                Content.Visible = value;
            }
        }

        public string Text
        {
            set => Caption.Text = value;
        }
    }

    /// <summary>Таблица «подпись — элементы» внутри раздела страницы.</summary>
    private sealed class FormBuilder
    {
        public readonly TableLayoutPanel Table = Ui.Table(2);
        public readonly List<Label> Captions = new List<Label>();
        public readonly List<Label> Hints = new List<Label>();
        private int _row;

        public Field Add(string caption, params Control[] controls)
        {
            Label label = Ui.Label(caption);
            Captions.Add(label);
            Control content = controls.Length == 1 ? controls[0] : Ui.Row(controls);
            content.Anchor = AnchorStyles.Left;
            Table.Controls.Add(label, 0, _row);
            Table.Controls.Add(content, 1, _row);
            _row++;
            return new Field { Caption = label, Content = content };
        }

        public Control Wide(Control control)
        {
            // Длинные пояснения переносятся по ширине страницы.
            if (control is Label label && label.ForeColor == SystemColors.GrayText) Hints.Add(label);
            Table.Controls.Add(control, 0, _row);
            Table.SetColumnSpan(control, 2);
            _row++;
            return control;
        }
    }

    private void BuildLayout()
    {
        SuspendLayout();

        Text = AppTitle;
        Icon = AppIcon.Get();
        Font = SystemFonts.MessageBoxFont;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1340, 810);
        MinimumSize = new Size(1000, 640);
        KeyPreview = true;

        BuildMenu();
        BuildStatusStrip();

        // Слева — таблица, вид диаграммы и настройки, справа — предпросмотр.
        var left = new Panel { Dock = DockStyle.Left, Width = LeftPanelWidth, Padding = new Padding(6, 4, 3, 4) };
        _leftPanel = left;
        _grid = new DataTableView { Dock = DockStyle.Fill };
        _settingsArea = BuildSettingsArea();
        _kindRow = BuildKindRow();
        left.Controls.Add(_grid);
        left.Controls.Add(_kindRow);
        left.Controls.Add(_settingsArea);
        left.Resize += (s, e) => FitSettings(false);

        Panel right = BuildPreviewPanel();

        Controls.Add(right);
        Controls.Add(left);
        Controls.Add(_menu);
        Controls.Add(_statusStrip);
        MainMenuStrip = _menu;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }

    private void BuildMenu()
    {
        _menu = new MenuStrip();

        ToolStripMenuItem Item(ToolStripMenuItem parent, string text, Action onClick, Keys shortcut = Keys.None, string shortcutText = null)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) => onClick();
            if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
            if (shortcutText != null) item.ShortcutKeyDisplayString = shortcutText;
            parent.DropDownItems.Add(item);
            return item;
        }

        var file = new ToolStripMenuItem("&Файл");
        Item(file, "&Новый", NewProject, Keys.Control | Keys.N);
        Item(file, "&Открыть…", OpenProject, Keys.Control | Keys.O);
        Item(file, "&Сохранить", () => SaveProject(false), Keys.Control | Keys.S);
        Item(file, "Сохранить &как…", () => SaveProject(true), Keys.Control | Keys.Shift | Keys.S);
        file.DropDownItems.Add(new ToolStripSeparator());
        Item(file, "&Импорт из Excel/CSV…", ImportFile, Keys.Control | Keys.I);
        file.DropDownItems.Add(new ToolStripSeparator());
        var samples = new ToolStripMenuItem("&Примеры");
        Item(samples, "График с &двумя шкалами (оформление ТММ)", () => LoadSample("sample:tmm"));
        Item(samples, "&Полярная диаграмма", () => LoadSample("sample:polar"));
        Item(samples, "&Треугольная диаграмма", () => LoadSample("sample:ternary"));
        Item(samples, "Простой &график y = √x", () => LoadSample("sample"));
        file.DropDownItems.Add(samples);
        file.DropDownItems.Add(new ToolStripSeparator());
        Item(file, "В&ыход", Close, Keys.None, "Alt+F4");

        var data = new ToolStripMenuItem("&Данные");
        // Ctrl+V обрабатывает сама таблица (и поля ввода), поэтому здесь сочетание только показано.
        Item(data, "&Вставить из буфера обмена", () => _grid.PasteFromClipboard(false), Keys.None, "Ctrl+V");
        Item(data, "Вставить с &заменой всей таблицы", () => _grid.PasteFromClipboard(true), Keys.Control | Keys.Shift | Keys.V);
        data.DropDownItems.Add(new ToolStripSeparator());
        Item(data, "&Добавить кривую", AddCurve);
        _removeCurveMenuItem = Item(data, "&Удалить кривую", RemoveCurve);
        data.DropDownItems.Add(new ToolStripSeparator());
        Item(data, "&Очистить таблицу", ClearTable);

        var graph = new ToolStripMenuItem("&График");
        Item(graph, "&Построить в КОМПАС", BuildInKompas, Keys.F5);

        var help = new ToolStripMenuItem("&Справка");
        Item(help, "&О программе", ShowAbout);

        _menu.Items.AddRange(new ToolStripItem[] { file, data, graph, help });
    }

    private void BuildStatusStrip()
    {
        _statusStrip = new StatusStrip { ShowItemToolTips = true };
        _statusKompas = new ToolStripStatusLabel("КОМПАС-3D: проверка…")
        {
            BorderSides = ToolStripStatusLabelBorderSides.Right,
            Padding = new Padding(2, 0, 8, 0)
        };
        _statusMessage = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _statusStrip.Items.Add(_statusKompas);
        _statusStrip.Items.Add(_statusMessage);
    }

    private Panel BuildPreviewPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(3, 4, 6, 4) };

        _preview = new PreviewControl { Dock = DockStyle.Fill };
        _infoLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Text = "Предпросмотр"
        };

        _issuesList = new ListBox
        {
            Dock = DockStyle.Bottom,
            Height = 82,
            IntegralHeight = false,
            HorizontalScrollbar = true,
            Visible = false
        };

        _buildButton = Ui.Button("Построить в КОМПАС");
        _buildButton.Padding = new Padding(14, 4, 14, 4);
        _buildButton.Font = new Font(Font, FontStyle.Bold);
        var buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 4, 0, 0)
        };
        buttonRow.Controls.Add(_buildButton);

        panel.Controls.Add(_preview);
        panel.Controls.Add(_infoLabel);
        panel.Controls.Add(_issuesList);
        panel.Controls.Add(buttonRow);
        return panel;
    }

    /// <summary>Строка над настройками: вид диаграммы и то, как при нём читаются столбцы таблицы.</summary>
    private Control BuildKindRow()
    {
        _kind = Ui.Combo(250, "в прямоугольных координатах", "в полярных координатах", "треугольная (три составляющие)");
        _kindHint = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(8, 0, 0, 0)
        };

        var row = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Bottom,
            Padding = new Padding(0, 4, 0, 2)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(Ui.Label("Вид диаграммы:"), 0, 0);
        row.Controls.Add(_kind, 1, 0);
        row.Controls.Add(_kindHint, 2, 0);
        return row;
    }

    // ------------------------------------------------------------------ дерево и страницы

    private Control BuildSettingsArea()
    {
        var area = new Panel { Dock = DockStyle.Bottom, Height = SettingsHeight, Padding = new Padding(0, 2, 0, 0) };

        _tree = new TreeView
        {
            Dock = DockStyle.Left,
            Width = TreeWidth,
            HideSelection = false,
            FullRowSelect = true,
            ShowLines = true,
            ShowRootLines = false
        };

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
        _pageTitle = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 26,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, SystemFonts.MessageBoxFont.Size + 1.5f, FontStyle.Bold)
        };
        _pageHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        right.Controls.Add(_pageHost);
        right.Controls.Add(_pageTitle);

        BuildDiagramPage();
        BuildScalesPage();
        BuildScalePage();
        BuildPolarAnglePage();
        BuildPolarRadiusPage();
        BuildTernaryPage();
        BuildCurvesPage();
        BuildCurvePage();
        BuildDesignPage();
        BuildPlacementPage();

        area.Controls.Add(right);
        area.Controls.Add(_tree);
        return area;
    }

    private TableLayoutPanel NewPage(PageKind kind)
    {
        TableLayoutPanel page = Ui.Table(1);
        page.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100);
        page.Dock = DockStyle.Top;
        page.Visible = false;
        _pages[kind] = page;
        _pageHost.Controls.Add(page);
        return page;
    }

    private GroupBox AddSection(TableLayoutPanel page, string title, FormBuilder form)
    {
        GroupBox group = Ui.Group(title, form.Table);
        page.Controls.Add(group, 0, page.Controls.Count);
        _captionLabels.AddRange(form.Captions);
        _wrapLabels.AddRange(form.Hints);
        return group;
    }

    private Control AddLine(TableLayoutPanel page, Control control)
    {
        if (control is Label label) _wrapLabels.Add(label);
        control.Margin = new Padding(6, 4, 3, 4);
        page.Controls.Add(control, 0, page.Controls.Count);
        return control;
    }

    /// <summary>
    /// Размеры, зависящие от масштаба экрана: высота строк дерева, общая ширина столбца подписей
    /// во всех разделах страниц, ширина переносимых пояснений.
    /// </summary>
    private void ScaleSettingsLayout()
    {
        int dpi = DeviceDpi;
        _tree.ItemHeight = TextRenderer.MeasureText("Ag", _tree.Font).Height + 8 * dpi / 96;
        foreach (Label label in _captionLabels) label.MinimumSize = new Size(175 * dpi / 96, 0);
        foreach (Label label in _wrapLabels) label.MaximumSize = new Size(Math.Max(200, _pageHost.ClientSize.Width - 40 * dpi / 96), 0);
    }

    /// <summary>Высота блока настроек — по самой высокой странице; если окно мало, часть высоты остаётся таблице.</summary>
    /// <param name="measure">Заново измерить страницы (после смены их состава).</param>
    private void FitSettings(bool measure)
    {
        if (_settingsArea == null || _leftPanel == null || !IsHandleCreated) return;

        if (measure || _settingsNeeded == 0)
        {
            int width = Math.Max(1, _pageHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            int content = 0;
            foreach (Control page in _pages.Values)
            {
                content = Math.Max(content, page.GetPreferredSize(new Size(width, 0)).Height);
            }
            int chrome = _settingsArea.Padding.Vertical + _pageTitle.Height + 4;
            // Блок только растёт: при переходе между страницами окно не «прыгает».
            if (content > 0) _settingsNeeded = Math.Max(_settingsNeeded, content + chrome);
        }
        if (_settingsNeeded == 0) return;

        int minGrid = MinGridHeight * DeviceDpi / 96;
        int available = _leftPanel.ClientSize.Height - _leftPanel.Padding.Vertical - _kindRow.Height - minGrid;
        int target = Math.Max(Math.Min(_settingsNeeded, available), 220 * DeviceDpi / 96);
        if (_settingsArea.Height != target) _settingsArea.Height = target;
    }

    private static NumberBox Number(int width, double min = double.NegativeInfinity, bool exclusiveMin = false, int decimals = 6) =>
        new NumberBox { Width = width, Minimum = min, ExclusiveMinimum = exclusiveMin, DisplayDecimals = decimals, Anchor = AnchorStyles.Left };

    private static Control LimitsBlock(NumberBox min, NumberBox max, CheckBox auto)
    {
        var bracketFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 13f);
        Label Bracket(string text) => new Label { Text = text, AutoSize = true, Font = bracketFont, Margin = new Padding(0, 0, 0, 0) };
        auto.Margin = new Padding(8, 5, 0, 3);
        return Ui.Row(Bracket("["), min, Bracket(","), max, Bracket("]"), auto);
    }

    private static NumericUpDown Every() => new NumericUpDown
    {
        Minimum = 1,
        Maximum = 100,
        Value = 1,
        Width = 46,
        Anchor = AnchorStyles.Left
    };

    private static ComboBox DecimalsCombo() => Ui.Combo(64, "авто", "0", "1", "2", "3", "4", "5", "6");

    private static RadioButton Radio(string text) => new RadioButton { Text = text, AutoSize = true, Margin = new Padding(3, 5, 12, 3) };

    private static TextBox TextField(int width) => new TextBox { Width = width, Anchor = AnchorStyles.Left };
}
