using System;
using System.Drawing;
using System.Windows.Forms;
using Grapher.UI.Controls;
using Grapher.UI.Rendering;

namespace Grapher.UI;

// Раскладка главного окна. Вид и формулировки повторяют окно FT Draw:
// вместо поля формулы — таблица данных, ниже — пределы, угол наклона, тип линии и группа «Координатная сетка».
public sealed partial class MainForm
{
    private const int LeftPanelWidth = 750;
    private const int TabsHeight = 462;

    // Строка меню и строка состояния
    private MenuStrip _menu;
    private ToolStripMenuItem _removeCurveMenuItem;
    private StatusStrip _statusStrip;
    private ToolStripStatusLabel _statusKompas;
    private ToolStripStatusLabel _statusMessage;

    // Данные и предпросмотр
    private TabControl _tabs;
    private DataTableView _grid;
    private PreviewControl _preview;
    private Label _infoLabel;
    private ListBox _issuesList;
    private Button _buildButton;

    // Вкладка «График»
    private CheckBox _xAuto, _yAuto;
    private NumberBox _xMin, _xMax, _yMin, _yMax;
    private NumberBox _angle;
    private ComboBox _curveBox;
    private Button _curveAdd, _curveRemove, _curveColorReset;
    private TextBox _curveName;
    private LineStyleComboBox _curveStyle;
    private ColorSwatchButton _curveColor;
    private ComboBox _curveMode, _curveMarker;
    private NumberBox _curveMarkerSize;
    private CheckBox _curveVisible;
    private CheckBox _gridOn, _equalScale;
    private NumberBox _width, _height, _scaleX, _scaleY, _stepX, _stepY;
    private TextBox _rangeX, _rangeY;
    private ComboBox _gridMode;
    private NumericUpDown _everyX, _everyY;

    // Вкладка «Оси и подписи»
    private CheckBox _axesOn, _arrowsOn, _frameOn, _labelsOn, _italic;
    private NumberBox _axisExtension, _arrowLength, _tickLength, _labelHeight, _titleHeight;
    private ComboBox _crossing, _titlePlacement, _decimalsX, _decimalsY, _decimalSeparator, _fontName;
    private TextBox _titleX, _titleY;

    // Вкладка «Размещение»
    private RadioButton _pickPoint, _useCoords;
    private NumberBox _placeX, _placeY, _decimateThreshold, _decimateTolerance;
    private CheckBox _legendOn, _decimateOn;

    private void BuildLayout()
    {
        SuspendLayout();

        Text = AppTitle;
        Icon = AppIcon.Get();
        Font = SystemFonts.MessageBoxFont;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1320, 810);
        MinimumSize = new Size(1000, 640);
        KeyPreview = true;

        BuildMenu();
        BuildStatusStrip();

        // Слева — таблица и настройки, справа — предпросмотр.
        var left = new Panel { Dock = DockStyle.Left, Width = LeftPanelWidth, Padding = new Padding(6, 4, 3, 4) };
        _grid = new DataTableView { Dock = DockStyle.Fill };
        TabControl tabs = BuildTabs();
        left.Controls.Add(_grid);
        left.Controls.Add(tabs);

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

    private TabControl BuildTabs()
    {
        _tabs = new TabControl { Dock = DockStyle.Bottom, Height = TabsHeight };
        _tabs.TabPages.Add(Page("График", BuildGraphPage()));
        _tabs.TabPages.Add(Page("Оси и подписи", BuildAxesPage()));
        _tabs.TabPages.Add(Page("Размещение", BuildPlacementPage()));
        return _tabs;
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { AutoScroll = true, Padding = new Padding(4), UseVisualStyleBackColor = true };
        content.Dock = DockStyle.Top;
        page.Controls.Add(content);
        return page;
    }

    private static NumberBox Number(int width, double min = double.NegativeInfinity, bool exclusiveMin = false, int decimals = 6) =>
        new NumberBox { Width = width, Minimum = min, ExclusiveMinimum = exclusiveMin, DisplayDecimals = decimals, Anchor = AnchorStyles.Left };

    // ------------------------------------------------------------------ вкладка «График»

    private Control BuildGraphPage()
    {
        TableLayoutPanel page = Ui.Table(1);
        page.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100);

        // Ряд из трёх блоков, как в FT Draw: пределы и угол наклона.
        var top = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = Padding.Empty };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));

        _xMin = Number(62);
        _xMax = Number(62);
        _xAuto = Ui.Check("авто");
        top.Controls.Add(Ui.Group("Пределы изменения X", LimitsBlock(_xMin, _xMax, _xAuto)), 0, 0);

        _yMin = Number(62);
        _yMax = Number(62);
        _yAuto = Ui.Check("авто");
        top.Controls.Add(Ui.Group("Пределы изменения Y", LimitsBlock(_yMin, _yMax, _yAuto)), 1, 0);

        _angle = Number(56, decimals: 2);
        top.Controls.Add(Ui.Group("Угол наклона оси абсцисс", Ui.Row(_angle, Ui.Label("° к оси X чертежа"))), 2, 0);
        page.Controls.Add(top, 0, 0);

        var tips = new ToolTip();
        string autoTip = "Пределы подбираются по данным и округляются до «красивых» значений.\nСнимите флажок, чтобы задать пределы вручную.";
        tips.SetToolTip(_xAuto, autoTip);
        tips.SetToolTip(_yAuto, autoTip);
        tips.SetToolTip(_angle, "Угол наклона оси абсцисс графика к оси абсцисс чертежа, градусы.");

        page.Controls.Add(Ui.Group("Тип и цвет линии графика", BuildCurveBlock()), 0, 1);
        page.Controls.Add(Ui.Group("", BuildGridBlock()), 0, 2);
        return page;
    }

    private static Control LimitsBlock(NumberBox min, NumberBox max, CheckBox auto)
    {
        var bracketFont = new Font(SystemFonts.MessageBoxFont.FontFamily, 13f);
        Label Bracket(string text) => new Label { Text = text, AutoSize = true, Font = bracketFont, Margin = new Padding(0, 0, 0, 0) };
        auto.Margin = new Padding(8, 5, 0, 3);
        return Ui.Row(Bracket("["), min, Bracket(","), max, Bracket("]"), auto);
    }

    private Control BuildCurveBlock()
    {
        TableLayoutPanel block = Ui.Table(4);

        _curveBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Anchor = AnchorStyles.Left };
        _curveAdd = Ui.Button("Добавить");
        _curveRemove = Ui.Button("Удалить");
        _curveVisible = Ui.Check("показывать");
        block.Controls.Add(Ui.Label("Кривая:"), 0, 0);
        block.Controls.Add(_curveBox, 1, 0);
        Control actions = Ui.Row(_curveAdd, _curveRemove, _curveVisible);
        block.Controls.Add(actions, 2, 0);
        block.SetColumnSpan(actions, 2);

        _curveName = new TextBox { Width = 170, Anchor = AnchorStyles.Left };
        _curveStyle = new LineStyleComboBox { Width = 205, Anchor = AnchorStyles.Left };
        _curveColor = new ColorSwatchButton { Text = "Цвет…" };
        _curveColorReset = Ui.Button("×");
        _curveColorReset.Padding = new Padding(1);
        var tips = new ToolTip();
        tips.SetToolTip(_curveColor, "Свой цвет линии кривой. В чертеже для него создаётся стиль линии КОМПАС.");
        tips.SetToolTip(_curveColorReset, "Вернуть цвет стиля КОМПАС");
        block.Controls.Add(Ui.Label("Название:"), 0, 1);
        block.Controls.Add(_curveName, 1, 1);
        block.Controls.Add(Ui.Label("Тип линии:"), 2, 1);
        block.Controls.Add(Ui.Row(_curveStyle, _curveColor, _curveColorReset), 3, 1);

        _curveMode = Ui.Combo(170, "Плавная (сплайн)", "Ломаная");
        _curveMarker = Ui.Combo(120, "нет", "окружность", "квадрат", "треугольник", "ромб", "крест");
        _curveMarkerSize = Number(48, 0, true, 2);
        block.Controls.Add(Ui.Label("Вид:"), 0, 2);
        block.Controls.Add(_curveMode, 1, 2);
        block.Controls.Add(Ui.Label("Маркеры точек:"), 2, 2);
        block.Controls.Add(Ui.Row(_curveMarker, Ui.Label("размер, мм"), _curveMarkerSize), 3, 2);
        return block;
    }

    private Control BuildGridBlock()
    {
        TableLayoutPanel block = Ui.Table(4);

        _gridOn = Ui.Check("Координатная сетка");
        _gridMode = Ui.Combo(230, "подобрать автоматически", "в единицах осей", "в миллиметрах на листе", "числом делений");
        Label modeLabel = Ui.Label("шаг сетки задаётся:");
        modeLabel.Margin = new Padding(28, 6, 3, 3);
        Control header = Ui.Row(_gridOn, modeLabel, _gridMode);
        block.Controls.Add(header, 0, 0);
        block.SetColumnSpan(header, 4);

        _width = Number(70, 0, true, 3);
        _height = Number(70, 0, true, 3);
        _rangeX = ReadOnlyBox(80);
        _rangeY = ReadOnlyBox(80);
        block.Controls.Add(Ui.Label("Габарит по оси абсцисс (X), мм"), 0, 1);
        block.Controls.Add(_width, 1, 1);
        block.Controls.Add(Ui.Label("соответствует величине размаха Xmax−Xmin"), 2, 1);
        block.Controls.Add(_rangeX, 3, 1);
        block.Controls.Add(Ui.Label("Габарит по оси ординат (Y), мм"), 0, 2);
        block.Controls.Add(_height, 1, 2);
        block.Controls.Add(Ui.Label("соответствует величине размаха Ymax−Ymin"), 2, 2);
        block.Controls.Add(_rangeY, 3, 2);

        Ui.Separator(block, 3);

        // Коэффициенты редактируются: можно задать масштаб и получить габарит.
        _scaleX = Number(80, 0, true, 6);
        _scaleY = Number(80, 0, true, 6);
        _equalScale = Ui.Check("Одинаковый масштаб по осям");
        TableLayoutPanel scales = Ui.Table(3);
        scales.Controls.Add(Ui.Label("Масштабный коэффициент по оси абсцисс (X), мм на единицу"), 0, 0);
        scales.Controls.Add(_scaleX, 1, 0);
        scales.Controls.Add(_equalScale, 2, 0);
        scales.Controls.Add(Ui.Label("Масштабный коэффициент по оси ординат (Y), мм на единицу"), 0, 1);
        scales.Controls.Add(_scaleY, 1, 1);
        block.Controls.Add(scales, 0, 4);
        block.SetColumnSpan(scales, 4);

        Ui.Separator(block, 5);

        _stepX = Number(70, 0, true, 6);
        _stepY = Number(70, 0, true, 6);
        _everyX = Every();
        _everyY = Every();
        TableLayoutPanel steps = Ui.Table(3);
        steps.Controls.Add(Ui.Label("Шаг сетки по оси абсцисс (X)"), 0, 0);
        steps.Controls.Add(_stepX, 1, 0);
        steps.Controls.Add(Ui.Row(Ui.Label("подписывать каждую"), _everyX, Ui.Label("-ю линию")), 2, 0);
        steps.Controls.Add(Ui.Label("Шаг сетки по оси ординат (Y)"), 0, 1);
        steps.Controls.Add(_stepY, 1, 1);
        steps.Controls.Add(Ui.Row(Ui.Label("подписывать каждую"), _everyY, Ui.Label("-ю линию")), 2, 1);
        block.Controls.Add(steps, 0, 6);
        block.SetColumnSpan(steps, 4);
        return block;
    }

    private static TextBox ReadOnlyBox(int width) => new TextBox
    {
        Width = width,
        ReadOnly = true,
        TabStop = false,
        Anchor = AnchorStyles.Left
    };

    private static NumericUpDown Every() => new NumericUpDown
    {
        Minimum = 1,
        Maximum = 100,
        Value = 1,
        Width = 46,
        Anchor = AnchorStyles.Left
    };

    // ------------------------------------------------------------------ вкладка «Оси и подписи»

    private Control BuildAxesPage()
    {
        TableLayoutPanel page = Ui.Table(1);
        page.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100);

        _axesOn = Ui.Check("Рисовать оси");
        _arrowsOn = Ui.Check("Стрелки на концах осей");
        _frameOn = Ui.Check("Рамка поля графика");
        _axisExtension = Number(52, 0, false, 2);
        _arrowLength = Number(52, 0, false, 2);
        _tickLength = Number(52, 0, false, 2);
        _crossing = Ui.Combo(330, "в левом нижнем углу рамки", "в нуле, если ноль внутри пределов");
        TableLayoutPanel axes = Ui.Table(1);
        axes.Controls.Add(Ui.Row(_axesOn, _arrowsOn, _frameOn), 0, 0);
        axes.Controls.Add(Ui.Row(Ui.Label("Выступ оси за рамку, мм"), _axisExtension, Ui.Label("Длина стрелки, мм"), _arrowLength,
            Ui.Label("Длина засечек, мм"), _tickLength), 0, 1);
        axes.Controls.Add(Ui.Row(Ui.Label("Оси пересекаются"), _crossing), 0, 2);
        page.Controls.Add(Ui.Group("Оси координат", axes), 0, 0);

        _titleX = new TextBox { Width = 150, Anchor = AnchorStyles.Left };
        _titleY = new TextBox { Width = 150, Anchor = AnchorStyles.Left };
        _titlePlacement = Ui.Combo(150, "у конца оси", "по центру оси");
        TableLayoutPanel titles = Ui.Table(1);
        titles.Controls.Add(Ui.Row(Ui.Label("Ось X:"), _titleX, Ui.Label("Ось Y:"), _titleY, Ui.Label("Положение:"), _titlePlacement), 0, 0);
        titles.Controls.Add(Ui.Hint("Название и единица измерения через запятую, например: σ, МПа"), 0, 1);
        page.Controls.Add(Ui.Group("Подписи осей", titles), 0, 1);

        _labelsOn = Ui.Check("Подписывать значения");
        string[] decimals = { "авто", "0", "1", "2", "3", "4", "5", "6" };
        _decimalsX = Ui.Combo(64, decimals);
        _decimalsY = Ui.Combo(64, decimals);
        _decimalSeparator = Ui.Combo(96, "запятая", "точка");
        page.Controls.Add(Ui.Group("Числа на осях", Ui.Row(_labelsOn, Ui.Label("знаков после запятой: X"), _decimalsX, Ui.Label("Y"), _decimalsY,
            Ui.Label("разделитель"), _decimalSeparator)), 0, 2);

        _fontName = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 150, Anchor = AnchorStyles.Left };
        foreach (string name in Grapher.Core.Model.DrawingFonts.All)
        {
            if (PreviewRenderer.IsFontInstalled(name)) _fontName.Items.Add(name);
        }
        _italic = Ui.Check("наклонный");
        _labelHeight = Number(48, 0, true, 2);
        _titleHeight = Number(48, 0, true, 2);
        TableLayoutPanel font = Ui.Table(1);
        font.Controls.Add(Ui.Row(Ui.Label("Шрифт"), _fontName, _italic, Ui.Label("высота чисел, мм"), _labelHeight,
            Ui.Label("высота подписей осей, мм"), _titleHeight), 0, 0);
        font.Controls.Add(Ui.Hint("Стандартные высоты шрифта: 2,5; 3,5; 5; 7; 10 мм"), 0, 1);
        page.Controls.Add(Ui.Group("Шрифт по ГОСТ 2.304", font), 0, 3);
        return page;
    }

    // ------------------------------------------------------------------ вкладка «Размещение»

    private Control BuildPlacementPage()
    {
        TableLayoutPanel page = Ui.Table(1);
        page.ColumnStyles[0] = new ColumnStyle(SizeType.Percent, 100);

        _pickPoint = new RadioButton { Text = "Указать курсором в окне КОМПАС при построении", AutoSize = true, Margin = new Padding(3, 5, 3, 3) };
        _useCoords = new RadioButton { Text = "Задать координаты в текущем виде, мм:", AutoSize = true, Margin = new Padding(3, 5, 3, 3) };
        _placeX = Number(80, decimals: 3);
        _placeY = Number(80, decimals: 3);
        TableLayoutPanel point = Ui.Table(1);
        point.Controls.Add(_pickPoint, 0, 0);
        point.Controls.Add(Ui.Row(_useCoords, Ui.Label("X"), _placeX, Ui.Label("Y"), _placeY), 0, 1);
        point.Controls.Add(Ui.Hint("Угол наклона графика задаётся на вкладке «График». В предпросмотре точка вставки отмечена красным."), 0, 2);
        page.Controls.Add(Ui.Group("Точка вставки — левый нижний угол поля графика", point), 0, 0);

        _legendOn = Ui.Check("Легенда: названия кривых справа от графика");
        _decimateOn = Ui.Check("Прореживать кривые, если точек больше");
        _decimateThreshold = Number(64, 2, false, 0);
        _decimateTolerance = Number(52, 0, true, 3);
        TableLayoutPanel extra = Ui.Table(1);
        extra.Controls.Add(_legendOn, 0, 0);
        extra.Controls.Add(Ui.Row(_decimateOn, _decimateThreshold, Ui.Label("допуск, мм на листе"), _decimateTolerance), 0, 1);
        extra.Controls.Add(Ui.Hint("При прореживании остаются точки, без которых линия отклонилась бы от исходной больше допуска."), 0, 2);
        page.Controls.Add(Ui.Group("Дополнительно", extra), 0, 1);
        return page;
    }
}
