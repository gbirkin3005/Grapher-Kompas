using System.Collections.Generic;

namespace Grapher.Core.Model;

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
    Cross
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

/// <summary>Положение названия оси.</summary>
public enum AxisTitlePlacement
{
    /// <summary>У конца оси, за стрелкой.</summary>
    AtEnd,
    /// <summary>По центру оси, снаружи числовых подписей.</summary>
    Centered
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

/// <summary>Настройки одной оси.</summary>
public sealed class AxisSettings
{
    /// <summary>Пределы подбираются по данным с округлением до «красивых» значений.</summary>
    public bool AutoLimits { get; set; } = true;

    public double Min { get; set; } = 0;
    public double Max { get; set; } = 100;

    /// <summary>Подпись оси, например «σ, МПа».</summary>
    public string Title { get; set; } = "";

    /// <summary>Шаг сетки; смысл зависит от <see cref="GraphSettings.GridMode"/>.</summary>
    public double GridStep { get; set; } = 10;

    /// <summary>Подписывать каждую N-ю линию сетки.</summary>
    public int LabelEvery { get; set; } = 1;

    /// <summary>Число знаков после запятой в подписях; −1 — подобрать автоматически.</summary>
    public int Decimals { get; set; } = -1;

    public AxisSettings Clone() => (AxisSettings)MemberwiseClone();
}

/// <summary>Общие настройки графика.</summary>
public sealed class GraphSettings
{
    /// <summary>Габарит поля графика по оси X на листе, мм.</summary>
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

    /// <summary>Выступ оси со стрелкой за рамку графика, мм.</summary>
    public double AxisExtensionMm { get; set; } = 10;

    public double ArrowLengthMm { get; set; } = 5;

    /// <summary>Длина засечек, мм; 0 — без засечек.</summary>
    public double TickLengthMm { get; set; } = 1.5;

    public bool ShowTickLabels { get; set; } = true;

    public AxesCrossing Crossing { get; set; } = AxesCrossing.LowerLeft;
    public AxisTitlePlacement TitlePlacement { get; set; } = AxisTitlePlacement.AtEnd;

    /// <summary>Шрифт по ГОСТ 2.304 (тип А, с греческими буквами).</summary>
    public string FontName { get; set; } = DrawingFonts.Default;

    public bool Italic { get; set; } = true;

    /// <summary>Высота числовых подписей, мм.</summary>
    public double LabelHeightMm { get; set; } = 3.5;

    /// <summary>Высота подписей осей, мм.</summary>
    public double TitleHeightMm { get; set; } = 5;

    /// <summary>Десятичная запятая в подписях (иначе точка).</summary>
    public bool DecimalComma { get; set; } = true;

    public bool ShowLegend { get; set; }

    /// <summary>Прореживать кривые с большим числом точек.</summary>
    public bool Decimate { get; set; } = true;

    /// <summary>Прореживание включается, когда точек в кривой больше этого числа.</summary>
    public int DecimateThreshold { get; set; } = 1000;

    /// <summary>Допустимое отклонение прореженной кривой от исходной, мм на листе.</summary>
    public double DecimateToleranceMm { get; set; } = 0.02;

    public GraphSettings Clone() => (GraphSettings)MemberwiseClone();
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
    public int FormatVersion { get; set; } = 1;
    public GraphData Data { get; set; } = new GraphData();
    public List<CurveSettings> Curves { get; set; } = new List<CurveSettings>();
    public AxisSettings XAxis { get; set; } = new AxisSettings();
    public AxisSettings YAxis { get; set; } = new AxisSettings();
    public GraphSettings Graph { get; set; } = new GraphSettings();
    public PlacementSettings Placement { get; set; } = new PlacementSettings();
}
