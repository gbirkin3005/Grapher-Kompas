using System.Drawing;
using System.Windows.Forms;
using Grapher.UI.Controls;
using Grapher.UI.Rendering;

namespace Grapher.UI;

// Страницы параметров: по одной на узел дерева настроек.
public sealed partial class MainForm
{
    // «Диаграмма»
    private NumberBox _width, _height, _angle;
    private CheckBox _equalScale, _gridOn, _axesOn, _arrowsOn, _frameOn;
    private ComboBox _gridMode, _crossing;
    private NumberBox _axisExtension, _tickLength, _arrowLength;
    private Field _fWidth, _fHeight, _fGridMode;
    private GroupBox _axesSection;

    // «Шкалы»
    private Button _scalesAdd;
    private Label _scalesHint;

    // Шкала прямоугольной диаграммы (X, Y, дополнительные Y)
    private TextBox _axTitle, _axUnit;
    private ComboBox _axSide, _axDecimals;
    private RadioButton _axLinear, _axLog;
    private NumberBox _axMin, _axMax, _axStep, _axScale;
    private CheckBox _axAuto, _axAlign, _axPi;
    private NumericUpDown _axEvery;
    private Label _axStepUnit;
    private Button _axRemove;
    private Field _fAxSide, _fAxAlign, _fAxStep, _fAxScale;

    // Угловая шкала полярной диаграммы
    private ComboBox _polarUnit, _polarZeroPreset, _polarRange, _polarDegree;
    private NumberBox _polarZero, _polarStep;
    private RadioButton _polarCcw, _polarCw;

    // Радиальная шкала
    private TextBox _radTitle, _radUnit;
    private NumberBox _radMin, _radMax, _radStep;
    private CheckBox _radAuto;
    private NumericUpDown _radEvery;
    private ComboBox _radDecimals, _radRay, _radSide;
    private Label _radStepUnit;

    // Треугольная диаграмма
    private TextBox _ternA, _ternB, _ternC, _ternUnit;
    private NumberBox _ternTotal, _ternStep;
    private NumericUpDown _ternEvery;

    // «Кривые» и кривая
    private Button _curvesAdd, _curveAdd, _curveRemove, _curveColorReset;
    private TextBox _curveName, _curveLabelText;
    private CheckBox _curveVisible, _curveLabelOn, _curveLabelAuto;
    private ComboBox _curveAxis, _curveMode, _curveMarker, _curveMarkerPlacement, _curveLabelDir;
    private LineStyleComboBox _curveStyle;
    private ColorSwatchButton _curveColor;
    private NumberBox _curveMarkerSize, _curveLabelPos;
    private NumericUpDown _curveMarkerEvery;
    private Field _fCurveAxis;

    // «Оформление»
    private ComboBox _titlePlacement, _units, _decimalSeparator, _fontName;
    private CheckBox _separateArrows, _nativeArrows, _labelsOn, _italic, _scaleFactors, _breakGrid, _dashedJumps, _smoothPeriodic, _legendOn;
    private NumberBox _labelHeight, _titleHeight;
    private Field _fTitlePlacement, _fNativeArrows;

    // «Размещение»
    private GroupBox _placementGroup;
    private RadioButton _pickPoint, _useCoords;
    private NumberBox _placeX, _placeY, _decimateThreshold, _decimateTolerance;
    private CheckBox _decimateOn;

    private readonly ToolTip _tips = new ToolTip();

    // ------------------------------------------------------------------ «Диаграмма»

    private void BuildDiagramPage()
    {
        TableLayoutPanel page = NewPage(PageKind.Diagram);

        var size = new FormBuilder();
        _width = Number(70, 0, true, 3);
        _height = Number(70, 0, true, 3);
        _equalScale = Ui.Check("одинаковый масштаб по осям");
        _angle = Number(56, decimals: 2);
        _fWidth = size.Add("Ширина поля (по оси X), мм", _width);
        _fHeight = size.Add("Высота поля (по оси Y), мм", _height, _equalScale);
        size.Add("Угол наклона, °", _angle, Ui.Hint("оси X графика к оси X чертежа"));
        size.Wide(Ui.Hint("Масштаб каждой шкалы (мм на единицу) — в параметрах шкалы."));
        AddSection(page, "Размер и положение", size);

        var grid = new FormBuilder();
        _gridOn = Ui.Check("показывать координатную сетку");
        _gridMode = Ui.Combo(230, "подобрать автоматически", "в единицах величины", "в миллиметрах на листе", "числом делений");
        grid.Add("", _gridOn);
        _fGridMode = grid.Add("Шаг сетки задаётся", _gridMode);
        grid.Wide(Ui.Hint("Сам шаг задаётся у каждой шкалы."));
        AddSection(page, "Координатная сетка", grid);

        var axes = new FormBuilder();
        _axesOn = Ui.Check("оси со стрелками");
        _arrowsOn = Ui.Check("стрелки на концах осей");
        _frameOn = Ui.Check("рамка поля");
        _crossing = Ui.Combo(260, "в левом нижнем углу рамки", "в нуле, если ноль внутри пределов");
        _axisExtension = Number(52, 0, false, 2);
        _tickLength = Number(52, 0, false, 2);
        _arrowLength = Number(52, 0, false, 2);
        axes.Add("Рисовать", _axesOn, _arrowsOn, _frameOn);
        axes.Add("Оси пересекаются", _crossing);
        axes.Add("Выступ оси за рамку, мм", _axisExtension, Ui.Label("длина засечек, мм"), _tickLength);
        axes.Add("Длина своей стрелки, мм", _arrowLength, Ui.Hint("(у стрелки-выноски размер задаёт КОМПАС)"));
        axes.Wide(Ui.Hint("Без осей шкалами служат стороны рамки — они чертятся основной линией."));
        _axesSection = AddSection(page, "Оси и рамка", axes);
    }

    // ------------------------------------------------------------------ «Шкалы»

    private void BuildScalesPage()
    {
        TableLayoutPanel page = NewPage(PageKind.Scales);
        _scalesHint = Ui.Hint("");
        AddLine(page, _scalesHint);
        _scalesAdd = Ui.Button("Добавить шкалу Y");
        AddLine(page, _scalesAdd);
        _tips.SetToolTip(_scalesAdd, "Ещё одна шкала ординат (Р 50-77-88, п. 2.4) — слева или справа от поля.");
    }

    private void BuildScalePage()
    {
        TableLayoutPanel page = NewPage(PageKind.Scale);

        var name = new FormBuilder();
        _axTitle = TextField(150);
        _axUnit = TextField(80);
        _axSide = Ui.Combo(90, "слева", "справа");
        name.Add("Обозначение величины", _axTitle, Ui.Hint("V_q, x^2, M_{с пр}"));
        name.Add("Единица измерения", _axUnit, Ui.Hint("м, рад, \\frac{м}{с^2}"));
        _fAxSide = name.Add("Положение шкалы", _axSide, Ui.Hint("относительно поля"));
        AddSection(page, "Обозначение", name);

        var limits = new FormBuilder();
        _axLinear = Radio("линейная");
        _axLog = Radio("логарифмическая");
        _axMin = Number(70);
        _axMax = Number(70);
        _axAuto = Ui.Check("авто");
        _axAlign = Ui.Check("деления — по линиям сетки основной шкалы");
        _axScale = Number(80, 0, true, 6);
        limits.Add("Шкала", _axLinear, _axLog);
        limits.Add("Пределы", LimitsBlock(_axMin, _axMax, _axAuto));
        _fAxAlign = limits.Add("", _axAlign);
        _fAxScale = limits.Add("Масштаб, мм на единицу", _axScale);
        AddSection(page, "Пределы и масштаб", limits);
        _tips.SetToolTip(_axAuto, "Пределы подбираются по данным и округляются до «красивых» значений.\nСнимите флажок, чтобы задать пределы вручную.");
        _tips.SetToolTip(_axLog, "Десятичные логарифмы: числа у 1, 10, 100…, промежуточные линии сетки — у 2…9.\nЗначения должны быть больше нуля.");
        _tips.SetToolTip(_axScale, "Сколько миллиметров на листе приходится на единицу величины (на логарифмической шкале — на декаду).\n" +
                                   "Можно ввести масштаб — размер поля пересчитается.");

        var numbers = new FormBuilder();
        _axStep = Number(80, 0, true, 6);
        _axStepUnit = Ui.Hint("");
        _axEvery = Every();
        _axDecimals = DecimalsCombo();
        _axPi = Ui.Check("в долях π (угол в радианах): π/6, π/2, 2π");
        _fAxStep = numbers.Add("Шаг сетки", _axStep, _axStepUnit);
        numbers.Add("Подписывать каждую", _axEvery, Ui.Label("-ю линию сетки"));
        numbers.Add("Знаков после запятой", _axDecimals);
        numbers.Add("Числа", _axPi);
        AddSection(page, "Числа и сетка", numbers);

        _axRemove = Ui.Button("Удалить эту шкалу");
        AddLine(page, _axRemove);
    }

    // ------------------------------------------------------------------ полярная диаграмма

    private void BuildPolarAnglePage()
    {
        TableLayoutPanel page = NewPage(PageKind.PolarAngle);

        var origin = new FormBuilder();
        _polarZeroPreset = Ui.Combo(120, "вправо", "вверх", "влево", "вниз", "под углом");
        _polarZero = Number(56, decimals: 2);
        _polarCcw = Radio("против часовой стрелки");
        _polarCw = Radio("по часовой стрелке");
        origin.Add("Нулевой луч направлен", _polarZeroPreset, _polarZero, Ui.Label("° от горизонтали"));
        origin.Add("Углы отсчитываются", _polarCcw, _polarCw);
        origin.Wide(Ui.Hint("По Р 50-77-88 нулевой луч — на горизонтальной или вертикальной оси, углы растут против часовой стрелки."));
        AddSection(page, "Начало отсчёта угла", origin);

        var scale = new FormBuilder();
        _polarUnit = Ui.Combo(110, "в градусах", "в радианах");
        _polarStep = new NumberBox { Width = 56, Minimum = 1, Maximum = 180, DisplayDecimals = 3, Anchor = AnchorStyles.Left };
        _polarRange = Ui.Combo(150, "от 0 до 360", "от −180 до 180");
        _polarDegree = Ui.Combo(250, "у каждого числа", "только у последнего числа");
        scale.Add("Углы в таблице заданы", _polarUnit);
        scale.Add("Шаг лучей сетки, °", _polarStep);
        scale.Add("Числа шкалы углов", _polarRange);
        scale.Add("Знак градуса", _polarDegree);
        AddSection(page, "Шкала углов", scale);
        _tips.SetToolTip(_polarDegree, "По Р 50-77-88 (п. 5.2) единицу угла можно нанести один раз — у последнего числа шкалы.");
    }

    private void BuildPolarRadiusPage()
    {
        TableLayoutPanel page = NewPage(PageKind.PolarRadius);

        var name = new FormBuilder();
        _radTitle = TextField(150);
        _radUnit = TextField(80);
        name.Add("Обозначение величины", _radTitle);
        name.Add("Единица измерения", _radUnit);
        AddSection(page, "Обозначение", name);

        var limits = new FormBuilder();
        _radMin = Number(70);
        _radMax = Number(70);
        _radAuto = Ui.Check("авто");
        _radStep = Number(80, 0, true, 6);
        _radStepUnit = Ui.Hint("");
        _radEvery = Every();
        _radDecimals = DecimalsCombo();
        limits.Add("Пределы радиуса", LimitsBlock(_radMin, _radMax, _radAuto));
        limits.Add("Шаг окружностей", _radStep, _radStepUnit);
        limits.Add("Подписывать каждую", _radEvery, Ui.Label("-ю окружность"));
        limits.Add("Знаков после запятой", _radDecimals);
        AddSection(page, "Пределы и сетка", limits);

        var place = new FormBuilder();
        _radRay = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 80, Anchor = AnchorStyles.Left };
        _radSide = Ui.Combo(170, "справа от луча", "слева от луча");
        place.Add("Числа радиуса вдоль луча", _radRay, Ui.Label("°"));
        place.Add("С какой стороны луча", _radSide, Ui.Hint("если смотреть от полюса"));
        place.Wide(Ui.Hint("Угол луча — как он подписан на шкале углов."));
        AddSection(page, "Размещение чисел радиуса", place);
    }

    // ------------------------------------------------------------------ треугольная диаграмма

    private void BuildTernaryPage()
    {
        TableLayoutPanel page = NewPage(PageKind.Ternary);

        var names = new FormBuilder();
        _ternA = TextField(130);
        _ternB = TextField(130);
        _ternC = TextField(130);
        names.Add("A — нижняя сторона", _ternA, Ui.Hint("первый столбец таблицы"));
        names.Add("B — правая сторона", _ternB, Ui.Hint("столбцы кривых"));
        names.Add("C — левая сторона", _ternC, Ui.Hint("C = сумма − A − B"));
        AddSection(page, "Обозначения составляющих", names);

        var scale = new FormBuilder();
        _ternTotal = Number(70, 0, true);
        _ternUnit = TextField(60);
        _ternStep = Number(70, 0, false);
        _ternEvery = Every();
        scale.Add("Сумма составляющих", _ternTotal, Ui.Hint("100 — проценты, 1 — доли"));
        scale.Add("Единица измерения", _ternUnit);
        scale.Add("Шаг сетки", _ternStep, Ui.Hint("0 — десятая часть суммы"));
        scale.Add("Подписывать каждую", _ternEvery, Ui.Label("-ю линию"));
        AddSection(page, "Шкалы", scale);
    }

    // ------------------------------------------------------------------ кривые

    private void BuildCurvesPage()
    {
        TableLayoutPanel page = NewPage(PageKind.Curves);
        AddLine(page, Ui.Hint("Кривая — столбец Y таблицы. Выберите кривую в дереве, чтобы настроить её линию, маркеры и подпись."));
        _curvesAdd = Ui.Button("Добавить кривую");
        AddLine(page, _curvesAdd);
    }

    private void BuildCurvePage()
    {
        TableLayoutPanel page = NewPage(PageKind.Curve);

        var curve = new FormBuilder();
        _curveName = TextField(170);
        _curveVisible = Ui.Check("показывать");
        _curveAxis = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Anchor = AnchorStyles.Left };
        curve.Add("Название", _curveName, _curveVisible);
        _fCurveAxis = curve.Add("Шкала", _curveAxis);
        AddSection(page, "Кривая", curve);
        _tips.SetToolTip(_curveName, "Название кривой. Индексы: V_q, x^2, a_{max}.");

        var line = new FormBuilder();
        _curveStyle = new LineStyleComboBox { Width = 205, Anchor = AnchorStyles.Left };
        _curveColor = new ColorSwatchButton { Text = "Цвет…" };
        _curveColorReset = Ui.Button("×");
        _curveColorReset.Padding = new Padding(1);
        _curveMode = Ui.Combo(170, "плавная (сплайн)", "ломаная");
        line.Add("Тип линии", _curveStyle, _curveColor, _curveColorReset);
        line.Add("Вид", _curveMode);
        AddSection(page, "Линия", line);
        _tips.SetToolTip(_curveColor, "Свой цвет линии кривой. В чертеже для него создаётся стиль линии КОМПАС.");
        _tips.SetToolTip(_curveColorReset, "Вернуть цвет стиля КОМПАС");

        var markers = new FormBuilder();
        _curveMarker = Ui.Combo(250, "нет", "окружность", "квадрат", "треугольник", "ромб", "крест", "залитый кружок",
            "точка КОМПАС: точка", "точка КОМПАС: крестик", "точка КОМПАС: косой крестик", "точка КОМПАС: квадрат",
            "точка КОМПАС: треугольник", "точка КОМПАС: окружность", "точка КОМПАС: звезда",
            "точка КОМПАС: перечёркнутый квадрат", "точка КОМПАС: утолщённый крестик");
        _curveMarkerSize = Number(48, 0, true, 2);
        _curveMarkerPlacement = Ui.Combo(170, "в точках таблицы", "в характерных точках");
        _curveMarkerEvery = Every();
        markers.Add("Маркер", _curveMarker);
        markers.Add("Размер, мм", _curveMarkerSize, Ui.Hint("(размер точки КОМПАС задаёт КОМПАС)"));
        markers.Add("Где ставить", _curveMarkerPlacement, Ui.Label("каждую"), _curveMarkerEvery, Ui.Label("-ю"));
        AddSection(page, "Маркеры точек", markers);
        _tips.SetToolTip(_curveMarker, "Фигуры рисуются отрезками и окружностями; «точка КОМПАС» — объект «Точка» КОМПАС.");
        _tips.SetToolTip(_curveMarkerPlacement, "Характерные точки — концы кривой, экстремумы и пересечения с нулём (Р 50-77-88, п. 3.9).");

        var label = new FormBuilder();
        _curveLabelOn = Ui.Check("подписать кривую на полке линии-выноски");
        _curveLabelText = TextField(170);
        _curveLabelAuto = Ui.Check("подобрать автоматически");
        _curveLabelPos = new NumberBox { Width = 44, Minimum = 0, Maximum = 100, DisplayDecimals = 1, Anchor = AnchorStyles.Left };
        _curveLabelDir = Ui.Combo(150, "подобрать", "вверх вправо", "вверх влево", "вниз вправо", "вниз влево");
        label.Add("", _curveLabelOn);
        label.Add("Текст", _curveLabelText, Ui.Hint("пусто — название кривой"));
        label.Add("Место", _curveLabelAuto, Ui.Label("или"), _curveLabelPos, Ui.Label("% длины кривой"));
        label.Add("Выноска", _curveLabelDir);
        AddSection(page, "Подпись (Р 50-77-88, п. 4.4)", label);

        _curveAdd = Ui.Button("Добавить кривую");
        _curveRemove = Ui.Button("Удалить кривую");
        AddLine(page, Ui.Row(_curveAdd, _curveRemove));
    }

    // ------------------------------------------------------------------ «Оформление»

    private void BuildDesignPage()
    {
        TableLayoutPanel page = NewPage(PageKind.Design);

        var titles = new FormBuilder();
        _titlePlacement = Ui.Combo(170, "у конца оси", "у середины шкалы");
        _separateArrows = Ui.Check("стрелка направления рядом");
        _units = Ui.Combo(330, "в конце шкалы между последними числами", "после обозначения через запятую",
            "дробью у конца шкалы: обозначение / единица", "за остриём стрелки направления");
        _nativeArrows = Ui.Check("стрелки — линией-выноской КОМПАС");
        _fTitlePlacement = titles.Add("Обозначения величин", _titlePlacement, _separateArrows);
        titles.Add("Единицы измерения", _units);
        _fNativeArrows = titles.Add("Стрелки", _nativeArrows);
        titles.Wide(Ui.Hint("Индексы: V_q, x^2, a_{max};  дробь: \\frac{м}{с}.  Наименование из нескольких слов пишется вдоль оси."));
        AddSection(page, "Обозначения и единицы", titles);
        _tips.SetToolTip(_nativeArrows, "Стрелка строится объектом «Линия-выноска» со стрелкой на конце, как командой КОМПАС.\n" +
                                        "Иначе — отрезком и залитым треугольником.");

        var font = new FormBuilder();
        _fontName = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 150, Anchor = AnchorStyles.Left };
        foreach (string fontName in Grapher.Core.Model.DrawingFonts.All)
        {
            if (PreviewRenderer.IsFontInstalled(fontName)) _fontName.Items.Add(fontName);
        }
        _italic = Ui.Check("наклонный");
        _labelHeight = Number(48, 0, true, 2);
        _titleHeight = Number(48, 0, true, 2);
        _labelsOn = Ui.Check("подписывать числа у шкал");
        _decimalSeparator = Ui.Combo(96, "запятая", "точка");
        font.Add("Шрифт", _fontName, _italic);
        font.Add("Высота, мм", Ui.Label("чисел"), _labelHeight, Ui.Label("обозначений"), _titleHeight);
        font.Add("Числа", _labelsOn, Ui.Label("разделитель"), _decimalSeparator);
        font.Wide(Ui.Hint("Стандартные высоты шрифта: 2,5; 3,5; 5; 7; 10 мм"));
        AddSection(page, "Шрифт по ГОСТ 2.304", font);

        var rules = new FormBuilder();
        _scaleFactors = Ui.Check("подписать масштабы шкал: μ = … мм на единицу величины");
        _breakGrid = Ui.Check("прерывать линии сетки под надписями на поле");
        _dashedJumps = Ui.Check("разрыв функции (две точки с одним X) — штриховой линией");
        _smoothPeriodic = Ui.Check("периодическая функция — без излома на стыке циклов");
        _legendOn = Ui.Check("легенда: названия кривых справа от графика");
        rules.Wide(_scaleFactors);
        rules.Wide(_breakGrid);
        rules.Wide(_dashedJumps);
        rules.Wide(_smoothPeriodic);
        rules.Wide(_legendOn);
        AddSection(page, "Р 50-77-88 «Правила выполнения диаграмм»", rules);
    }

    // ------------------------------------------------------------------ «Размещение»

    private void BuildPlacementPage()
    {
        TableLayoutPanel page = NewPage(PageKind.Placement);

        var point = new FormBuilder();
        _pickPoint = new RadioButton { Text = "указать курсором в окне КОМПАС при построении", AutoSize = true, Margin = new Padding(3, 5, 3, 3) };
        _useCoords = new RadioButton { Text = "по координатам в текущем виде, мм:", AutoSize = true, Margin = new Padding(3, 5, 3, 3) };
        _placeX = Number(80, decimals: 3);
        _placeY = Number(80, decimals: 3);
        point.Wide(_pickPoint);
        point.Wide(Ui.Row(_useCoords, Ui.Label("X"), _placeX, Ui.Label("Y"), _placeY));
        point.Wide(Ui.Hint("В предпросмотре точка вставки отмечена красным. Угол наклона — на странице «Диаграмма»."));
        _placementGroup = AddSection(page, "Точка вставки", point);

        var extra = new FormBuilder();
        _decimateOn = Ui.Check("прореживать кривые, если точек больше");
        _decimateThreshold = Number(64, 2, false, 0);
        _decimateTolerance = Number(52, 0, true, 3);
        extra.Wide(Ui.Row(_decimateOn, _decimateThreshold));
        extra.Add("Допуск, мм на листе", _decimateTolerance);
        extra.Wide(Ui.Hint("Остаются точки, без которых линия отклонилась бы от исходной больше допуска."));
        AddSection(page, "Прореживание", extra);
    }
}
