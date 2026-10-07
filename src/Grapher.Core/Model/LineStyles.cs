using System.Collections.Generic;

namespace Grapher.Core.Model;

/// <summary>
/// Системные стили линий КОМПАС. Числовые значения совпадают с номерами стилей КОМПАС
/// (ksCurveStyleEnum), поэтому рендерер передаёт их в API без таблицы соответствия.
/// </summary>
public enum LineStyleId
{
    /// <summary>Основная.</summary>
    Main = 1,
    /// <summary>Тонкая.</summary>
    Thin = 2,
    /// <summary>Осевая (штрихпунктирная тонкая).</summary>
    Axial = 3,
    /// <summary>Штриховая.</summary>
    Dashed = 4,
    /// <summary>Утолщённая.</summary>
    Thick = 7,
    /// <summary>Пунктир 2 (штрихпунктирная с двумя точками).</summary>
    DashDotDot = 8,
    /// <summary>Штриховая основная (толстая).</summary>
    DashedMain = 9,
    /// <summary>Осевая основная (штрихпунктирная утолщённая).</summary>
    AxialMain = 10
}

/// <summary>Описание стиля линии: название как в КОМПАС и параметры для предпросмотра.</summary>
public sealed class LineStyleInfo
{
    public LineStyleInfo(LineStyleId id, string name, double widthMm, double[] dashPatternMm)
    {
        Id = id;
        Name = name;
        WidthMm = widthMm;
        DashPatternMm = dashPatternMm;
    }

    public LineStyleId Id { get; }
    public string Name { get; }

    /// <summary>Толщина на бумаге, мм (значения КОМПАС по умолчанию).</summary>
    public double WidthMm { get; }

    /// <summary>Чередование «штрих, пробел, ...» в мм; null для сплошной линии.</summary>
    public double[] DashPatternMm { get; }

    public override string ToString() => Name;
}

public static class LineStyles
{
    // Названия, порядок, толщины и штрихи — как у системных стилей КОМПАС-3D v25
    // (прочитаны через API: IStylesManager.CurvesStyles приложения).
    private const double ThinWidth = 0.18;
    private const double MainWidth = 0.6;
    private const double ThickWidth = 1.0;

    public static readonly IReadOnlyList<LineStyleInfo> All = new[]
    {
        new LineStyleInfo(LineStyleId.Main, "Основная", MainWidth, null),
        new LineStyleInfo(LineStyleId.Thin, "Тонкая", ThinWidth, null),
        new LineStyleInfo(LineStyleId.Axial, "Осевая", ThinWidth, new[] { 15.0, 1.5, 1.5, 1.5 }),
        new LineStyleInfo(LineStyleId.Dashed, "Штриховая", ThinWidth, new[] { 4.0, 2.0 }),
        new LineStyleInfo(LineStyleId.Thick, "Утолщенная", ThickWidth, null),
        new LineStyleInfo(LineStyleId.DashDotDot, "Пунктир 2", ThinWidth, new[] { 4.0, 2.0, 1.0, 2.0, 1.0, 2.0 }),
        new LineStyleInfo(LineStyleId.AxialMain, "Осевая осн.", MainWidth, new[] { 8.0, 2.0, 1.0, 2.0 }),
        new LineStyleInfo(LineStyleId.DashedMain, "Штриховая осн.", MainWidth, new[] { 4.0, 2.0 })
    };

    public static LineStyleInfo Get(LineStyleId id)
    {
        foreach (LineStyleInfo info in All)
        {
            if (info.Id == id) return info;
        }
        return All[0];
    }
}
