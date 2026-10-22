using System.Collections.Generic;
using Newtonsoft.Json;

namespace Grapher.Core.Model;

/// <summary>Вид диаграммы (система координат).</summary>
public enum DiagramKind
{
    /// <summary>Прямоугольная система координат.</summary>
    Cartesian,
    /// <summary>Полярная система координат: угол и радиус.</summary>
    Polar,
    /// <summary>Треугольная диаграмма трёх составляющих, сумма которых постоянна.</summary>
    Ternary
}

/// <summary>Вид кривой.</summary>
public enum CurveMode
{
    /// <summary>Ломаная через точки.</summary>
    Polyline,
    /// <summary>Плавная кривая (сплайн через точки).</summary>
    Smooth
}

public enum MarkerShape
{
    None,
    Circle,
    Square,
    Triangle,
    Diamond,
    Cross,
    /// <summary>Залитый кружок.</summary>
    Dot,

    // Точки — объекты «Точка» КОМПАС. Размер точки задаёт КОМПАС (около 3 мм, «точка» — 1,6 мм).

    /// <summary>Точка КОМПАС, стиль «точка» (залитый кружок).</summary>
    KompasPoint,
    /// <summary>Точка КОМПАС, стиль «крестик».</summary>
    KompasPointCross,
    /// <summary>Точка КОМПАС, стиль «x-точка» (косой крест).</summary>
    KompasPointX,
    /// <summary>Точка КОМПАС, стиль «квадрат».</summary>
    KompasPointSquare,
    /// <summary>Точка КОМПАС, стиль «треугольник».</summary>
    KompasPointTriangle,
    /// <summary>Точка КОМПАС, стиль «окружность».</summary>
    KompasPointCircle,
    /// <summary>Точка КОМПАС, стиль «звезда».</summary>
    KompasPointStar,
    /// <summary>Точка КОМПАС, стиль «перечёркнутый квадрат».</summary>
    KompasPointCrossedSquare,
    /// <summary>Точка КОМПАС, стиль «утолщённый крестик».</summary>
    KompasPointBoldCross
}

public static class MarkerShapes
{
    /// <summary>Маркер — объект «Точка» КОМПАС (размер задаёт КОМПАС).</summary>
    public static bool IsKompasPoint(MarkerShape shape) => shape >= MarkerShape.KompasPoint && shape <= MarkerShape.KompasPointBoldCross;

    /// <summary>Номер стиля точки КОМПАС (1 — точка, 2 — крестик … 9 — утолщённый крестик); 0 — не точка КОМПАС.</summary>
    public static int KompasPointStyle(MarkerShape shape) =>
        IsKompasPoint(shape) ? 1 + (shape - MarkerShape.KompasPoint) : 0;
}

/// <summary>Какие точки кривой отмечать маркерами.</summary>
public enum MarkerPlacement
{
    /// <summary>Точки таблицы (каждую N-ю).</summary>
    DataPoints,
    /// <summary>Характерные точки: концы, экстремумы и пересечения с нулём (Р 50-77-88, п. 3.9).</summary>
    Characteristic
}

/// <summary>Подпись кривой на поле диаграммы.</summary>
public enum CurveLabelMode
{
    None,
    /// <summary>Обозначение на полке линии-выноски.</summary>
    Leader
}

/// <summary>Куда отведена линия-выноска от кривой.</summary>
public enum LabelDirection
{
    Auto,
    UpRight,
    UpLeft,
    DownRight,
    DownLeft
}

/// <summary>Способ задания шага сетки.</summary>
public enum GridStepMode
{
    /// <summary>Подобрать «красивый» шаг автоматически.</summary>
    Auto,
    /// <summary>Шаг в единицах оси.</summary>
    Units,
    /// <summary>Шаг в миллиметрах на листе.</summary>
    Millimeters,
    /// <summary>Число делений по оси.</summary>
    Divisions
}

/// <summary>Где пересекаются оси.</summary>
public enum AxesCrossing
{
    /// <summary>В левом нижнем углу рамки.</summary>
    LowerLeft,
    /// <summary>В нуле, если ноль попадает внутрь пределов.</summary>
    AtZero
}

/// <summary>Положение обозначения величины у шкалы (Р 50-77-88, п. 4.2).</summary>
public enum AxisTitlePlacement
{
    /// <summary>У конца оси, за стрелкой.</summary>
    AtEnd,
    /// <summary>У середины шкалы с её внешней стороны.</summary>
    Centered
}

/// <summary>Способ нанесения единиц измерения (Р 50-77-88, п. 5.1).</summary>
public enum UnitPlacement
{
    /// <summary>В конце шкалы между последним и предпоследним числами.</summary>
    BetweenNumbers,
    /// <summary>Вместе с обозначением величины после запятой.</summary>
    AfterTitle,
    /// <summary>В конце шкалы дробью: в числителе обозначение величины, в знаменателе единица.</summary>
    Fraction,
    /// <summary>
    /// За остриём стрелки, показывающей направление возрастания величины («φ ⟶ рад»).
    /// Если отдельной стрелки нет, единица пишется после обозначения через запятую.
    /// </summary>
    AtArrow
}

/// <summary>С какой стороны поля расположена дополнительная шкала Y.</summary>
public enum AxisSide
{
    Left,
    Right
}

/// <summary>Вид чисел у шкалы.</summary>
public enum NumberStyle
{
    /// <summary>Десятичные числа.</summary>
    Decimal,
    /// <summary>Доли числа π (для углов в радианах): π/6, π/2, 2π.</summary>
    PiFractions
}

public enum AngleUnit
{
    Degrees,
    Radians
}

/// <summary>Направление нулевого луча полярной диаграммы (прежняя запись в файле проекта).</summary>
public enum ZeroDirection
{
    Right,
    Up,
    Left,
    Down
}

/// <summary>С какой стороны луча стоят числа радиальной шкалы (если смотреть от полюса вдоль луча).</summary>
public enum RadialLabelSide
{
    /// <summary>Справа от луча (по часовой стрелке от него).</summary>
    Right,
    /// <summary>Слева от луча (против часовой стрелки от него).</summary>
    Left
}

/// <summary>Настройки одной кривой (одного столбца Y).</summary>
public sealed class CurveSettings
{
    public string Name { get; set; } = "Y";
    public LineStyleId LineStyle { get; set; } = LineStyleId.Main;

    /// <summary>
    /// Свой цвет линии в записи «#RRGGBB»; null — цвет стиля КОМПАС.
    /// В КОМПАС цвет задаётся стилем, поэтому для своего цвета в документе создаётся стиль линии.
    /// </summary>
    public string Color { get; set; }
    public CurveMode Mode { get; set; } = CurveMode.Smooth;
    public MarkerShape Marker { get; set; } = MarkerShape.None;

    /// <summary>Размер маркера (диаметр), мм.</summary>
    public double MarkerSizeMm { get; set; } = 2.0;

    /// <summary>Ставить маркер на каждой N-й точке.</summary>
    public int MarkerEvery { get; set; } = 1;

    public MarkerPlacement MarkerPlacement { get; set; } = MarkerPlacement.DataPoints;

    /// <summary>Шкала Y, по которой строится кривая: 0 — основная, 1 и далее — дополнительные.</summary>
    public int Axis { get; set; }

    public CurveLabelMode Label { get; set; } = CurveLabelMode.None;

    /// <summary>Текст подписи на полке-выноске; пусто — название кривой.</summary>
    public string LabelText { get; set; } = "";

    /// <summary>Положение точки выноски вдоль кривой, % длины; отрицательное — подобрать автоматически.</summary>
    public double LabelPosition { get; set; } = -1;

    public LabelDirection LabelDirection { get; set; } = LabelDirection.Auto;

    public bool Visible { get; set; } = true;

    public CurveSettings Clone() => (CurveSettings)MemberwiseClone();
}

/// <summary>Таблица исходных данных. Ячейки хранятся текстом, как их ввёл пользователь.</summary>
public sealed class GraphData
{
    public string XName { get; set; } = "X";

    /// <summary>Строки таблицы: первая ячейка — X, остальные — Y по кривым.</summary>
    [Newtonsoft.Json.JsonConverter(typeof(Serialization.DataRowsConverter))]
    public List<string[]> Rows { get; set; } = new List<string[]>();
}

/// <summary>Настройки одной оси (шкалы).</summary>
public sealed class AxisSettings
{
    /// <summary>Пределы подбираются по данным с округлением до «красивых» значений.</summary>
    public bool AutoLimits { get; set; } = true;

    public double Min { get; set; } = 0;
    public double Max { get; set; } = 100;

    /// <summary>Обозначение величины: символ («σ», «V_q») или наименование («Скорость вытяжки»).</summary>
    public string Title { get; set; } = "";

    /// <summary>Единица измерения («МПа», «рад»); способ нанесения задаёт <see cref="GraphSettings.Units"/>.</summary>
    public string Unit { get; set; } = "";

    /// <summary>Шаг сетки; смысл зависит от <see cref="GraphSettings.GridMode"/>.</summary>
    public double GridStep { get; set; } = 10;

    /// <summary>Подписывать каждую N-ю линию сетки.</summary>
    public int LabelEvery { get; set; } = 1;

    /// <summary>Число знаков после запятой в подписях; −1 — подобрать автоматически.</summary>
    public int Decimals { get; set; } = -1;

    public NumberStyle Numbers { get; set; } = NumberStyle.Decimal;

    /// <summary>Сторона поля, у которой стоит шкала (для дополнительных шкал Y).</summary>
    public AxisSide Side { get; set; } = AxisSide.Right;

    /// <summary>
    /// Логарифмическая шкала (десятичные логарифмы): числа у декад (1, 10, 100…),
    /// промежуточные линии сетки — у 2…9 единиц каждой декады. Значения должны быть больше нуля.
    /// </summary>
    public bool Logarithmic { get; set; }

    /// <summary>
    /// Дополнительная шкала: при автоматических пределах подобрать их так,
    /// чтобы деления совпали с линиями сетки основной шкалы.
    /// </summary>
    public bool AlignToGrid { get; set; } = true;

    public AxisSettings Clone() => (AxisSettings)MemberwiseClone();
}

/// <summary>Общие настройки графика.</summary>
public sealed class GraphSettings
{
    public DiagramKind Kind { get; set; } = DiagramKind.Cartesian;

    /// <summary>
    /// Габарит поля графика по оси X на листе, мм.
    /// Для полярной диаграммы — диаметр, для треугольной — длина стороны.
    /// </summary>
    public double WidthMm { get; set; } = 100;

    /// <summary>Габарит поля графика по оси Y на листе, мм.</summary>
    public double HeightMm { get; set; } = 100;

    /// <summary>Одинаковый масштаб по осям: высота вычисляется по ширине.</summary>
    public bool EqualScale { get; set; }

    public bool ShowGrid { get; set; } = true;
    public GridStepMode GridMode { get; set; } = GridStepMode.Auto;
    public LineStyleId GridStyle { get; set; } = LineStyleId.Thin;

    public bool ShowFrame { get; set; } = true;
    public bool ShowAxes { get; set; } = true;
    public bool ShowArrows { get; set; } = true;

    /// <summary>
    /// Стрелки — объекты «Линия-выноска» КОМПАС со стрелкой на конце (размер стрелки задаёт КОМПАС);
    /// иначе — отрезок и залитый треугольник.
    /// </summary>
    public bool NativeArrows { get; set; } = true;

    /// <summary>Выступ оси со стрелкой за рамку графика, мм.</summary>
    public double AxisExtensionMm { get; set; } = 10;

    public double ArrowLengthMm { get; set; } = 5;

    /// <summary>Длина засечек, мм; 0 — без засечек.</summary>
    public double TickLengthMm { get; set; } = 1.5;

    public bool ShowTickLabels { get; set; } = true;

    public AxesCrossing Crossing { get; set; } = AxesCrossing.LowerLeft;
    public AxisTitlePlacement TitlePlacement { get; set; } = AxisTitlePlacement.AtEnd;

    /// <summary>
    /// Самостоятельные стрелки рядом с обозначениями величин, параллельные осям (Р 50-77-88, п. 1.3).
    /// Действует при обозначениях у середины шкалы.
    /// </summary>
    public bool SeparateArrows { get; set; }

    public UnitPlacement Units { get; set; } = UnitPlacement.BetweenNumbers;

    /// <summary>Подписать масштабы шкал: μ = … мм на единицу величины (принято в курсе ТММ).</summary>
    public bool ShowScaleFactors { get; set; }

    /// <summary>Шрифт по ГОСТ 2.304 (тип А, с греческими буквами).</summary>
    public string FontName { get; set; } = DrawingFonts.Default;

    public bool Italic { get; set; } = true;

    /// <summary>Высота числовых подписей, мм.</summary>
    public double LabelHeightMm { get; set; } = 3.5;

    /// <summary>Высота обозначений величин и подписей кривых, мм.</summary>
    public double TitleHeightMm { get; set; } = 5;

    /// <summary>Десятичная запятая в подписях (иначе точка).</summary>
    public bool DecimalComma { get; set; } = true;

    public bool ShowLegend { get; set; }

    /// <summary>Прерывать линии сетки под надписями на поле диаграммы (Р 50-77-88, п. 6.2).</summary>
    public bool BreakGridUnderLabels { get; set; } = true;

    /// <summary>
    /// Разрыв функции (две точки подряд с одним X) показывать штриховой вертикальной линией,
    /// а участки по обе стороны сглаживать отдельно.
    /// </summary>
    public bool DashedJumps { get; set; } = true;

    /// <summary>
    /// Плавная кривая периодической функции: если значения на концах совпадают,
    /// касательные в начале и в конце делаются одинаковыми (без излома на стыке циклов).
    /// </summary>
    public bool SmoothPeriodic { get; set; } = true;

    /// <summary>Прореживать кривые с большим числом точек.</summary>
    public bool Decimate { get; set; } = true;

    /// <summary>Прореживание включается, когда точек в кривой больше этого числа.</summary>
    public int DecimateThreshold { get; set; } = 1000;

    /// <summary>Допустимое отклонение прореженной кривой от исходной, мм на листе.</summary>
    public double DecimateToleranceMm { get; set; } = 0.02;

    public GraphSettings Clone() => (GraphSettings)MemberwiseClone();
}

/// <summary>
/// Полярная диаграмма. Первый столбец таблицы — угол, остальные — радиусы кривых.
/// Радиальная шкала настраивается осью Y проекта, диаметр — габаритом по X.
/// </summary>
public sealed class PolarSettings
{
    /// <summary>В каких единицах заданы углы в таблице.</summary>
    public AngleUnit AngleUnit { get; set; } = AngleUnit.Degrees;

    /// <summary>
    /// Направление нулевого луча — начало отсчёта углов: угол от горизонтали вправо против часовой стрелки, градусы
    /// (0 — вправо, 90 — вверх, 180 — влево, 270 — вниз).
    /// </summary>
    public double ZeroAngleDeg { get; set; }

    /// <summary>Прежняя запись нулевого луча (только для чтения старых файлов проекта).</summary>
    [JsonProperty("Zero")]
    private ZeroDirection LegacyZero
    {
        set => ZeroAngleDeg = value == ZeroDirection.Up ? 90 : value == ZeroDirection.Left ? 180 : value == ZeroDirection.Down ? 270 : 0;
    }

    /// <summary>Отсчёт углов по часовой стрелке (по Р 50-77-88 положительное направление — против часовой).</summary>
    public bool Clockwise { get; set; }

    /// <summary>Шаг лучей сетки, градусы.</summary>
    public double AngleStepDeg { get; set; } = 30;

    /// <summary>Числа шкалы углов от −180 до 180; иначе от 0 до 360.</summary>
    public bool SignedAngles { get; set; }

    /// <summary>Луч, вдоль которого подписаны значения радиуса, градусы диаграммы (как они подписаны на шкале углов).</summary>
    public double LabelRayDeg { get; set; }

    /// <summary>С какой стороны луча стоят числа радиуса.</summary>
    public RadialLabelSide LabelSide { get; set; } = RadialLabelSide.Right;

    /// <summary>Знак градуса у каждого числа шкалы углов; иначе — только у последнего (Р 50-77-88, п. 5.2).</summary>
    public bool DegreeSignOnAll { get; set; } = true;

    public PolarSettings Clone() => (PolarSettings)MemberwiseClone();
}

/// <summary>
/// Треугольная диаграмма. Первый столбец таблицы — составляющая A (шкала на нижней стороне),
/// остальные столбцы — составляющая B кривых (шкала на правой стороне); C = сумма − A − B (левая сторона).
/// Обозначения A и B берутся из осей X и Y проекта.
/// </summary>
public sealed class TernarySettings
{
    /// <summary>Сумма трёх составляющих: 100 (проценты) или 1 (доли).</summary>
    public double Total { get; set; } = 100;

    /// <summary>Обозначение третьей составляющей (левая сторона).</summary>
    public string TitleC { get; set; } = "";

    /// <summary>Единица измерения, общая для трёх шкал.</summary>
    public string Unit { get; set; } = "%";

    /// <summary>Шаг линий сетки в единицах составляющих; 0 — десятая часть суммы.</summary>
    public double GridStep { get; set; }

    /// <summary>Подписывать каждую N-ю линию сетки.</summary>
    public int LabelEvery { get; set; } = 2;

    public TernarySettings Clone() => (TernarySettings)MemberwiseClone();
}

/// <summary>Размещение графика на чертеже.</summary>
public sealed class PlacementSettings
{
    /// <summary>Указывать точку вставки курсором в окне КОМПАС.</summary>
    public bool PickPoint { get; set; } = true;

    /// <summary>Координаты точки вставки (левый нижний угол поля графика) в текущем виде, мм.</summary>
    public double X { get; set; }
    public double Y { get; set; }

    /// <summary>Угол наклона оси абсцисс графика к оси абсцисс чертежа, градусы.</summary>
    public double AngleDeg { get; set; }

    public PlacementSettings Clone() => (PlacementSettings)MemberwiseClone();
}

/// <summary>Проект графика: данные и все настройки.</summary>
public sealed class GraphProject
{
    public int FormatVersion { get; set; } = 2;
    public GraphData Data { get; set; } = new GraphData();
    public List<CurveSettings> Curves { get; set; } = new List<CurveSettings>();
    public AxisSettings XAxis { get; set; } = new AxisSettings();
    public AxisSettings YAxis { get; set; } = new AxisSettings();

    /// <summary>Дополнительные шкалы Y (Р 50-77-88, п. 2.4). Кривая выбирает шкалу свойством <see cref="CurveSettings.Axis"/>.</summary>
    public List<AxisSettings> ExtraAxes { get; set; } = new List<AxisSettings>();

    public GraphSettings Graph { get; set; } = new GraphSettings();
    public PolarSettings Polar { get; set; } = new PolarSettings();
    public TernarySettings Ternary { get; set; } = new TernarySettings();
    public PlacementSettings Placement { get; set; } = new PlacementSettings();
}
