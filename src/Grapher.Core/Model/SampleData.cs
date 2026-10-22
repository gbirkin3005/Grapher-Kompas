using System;
using System.Collections.Generic;
using System.Globalization;

namespace Grapher.Core.Model;

/// <summary>Примеры графиков: контрольный пример и образцы каждого вида диаграммы.</summary>
public static class SampleData
{
    private static string Number(double value) =>
        Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>Точки функции y = √x на отрезке [xMin, xMax], равномерно по X.</summary>
    public static List<string[]> SqrtRows(double xMin = 0, double xMax = 100, int count = 100)
    {
        if (count < 2) throw new ArgumentOutOfRangeException(nameof(count));
        var rows = new List<string[]>(count);
        for (int i = 0; i < count; i++)
        {
            double x = xMin + (xMax - xMin) * i / (count - 1);
            if (i == count - 1) x = xMax;
            double y = Math.Sqrt(x);
            rows.Add(new[] { Number(x), Number(y) });
        }
        return rows;
    }

    /// <summary>
    /// Контрольный пример: y = √x при x от 0 до 100 (100 точек), поле 100×100 мм,
    /// сетка с шагом 10 мм по обеим осям, плавная кривая.
    /// </summary>
    public static GraphProject SqrtProject()
    {
        var project = new GraphProject();
        project.Data.XName = "x";
        project.Data.Rows = SqrtRows();
        project.Curves.Add(new CurveSettings { Name = "√x", LineStyle = LineStyleId.Main, Mode = CurveMode.Smooth });
        project.XAxis.Title = "x";
        project.YAxis.Title = "y";
        project.Graph.WidthMm = 100;
        project.Graph.HeightMm = 100;
        project.Graph.GridMode = GridStepMode.Millimeters;
        project.XAxis.GridStep = 10;
        project.YAxis.GridStep = 10;
        return project;
    }

    /// <summary>
    /// Образец диаграммы с двумя шкалами в оформлении курсового проекта по ТММ:
    /// аналоги скоростей (левая шкала, м) и аналог угловой скорости (правая шкала) от угла поворота кривошипа.
    /// Рамка со шкалами, угол в долях π, единицы между числами, подписи кривых на полках-выносках,
    /// характерные точки и масштабы μ.
    /// </summary>
    public static GraphProject TmmProject()
    {
        var project = new GraphProject();
        project.Data.XName = "φ";
        const int count = 73;
        for (int i = 0; i < count; i++)
        {
            double phi = 2 * Math.PI * i / (count - 1);
            project.Data.Rows.Add(new[]
            {
                Number(phi),
                Number(0.07 * Math.Cos(phi)),
                Number(-0.21 * Math.Sin(phi)),
                Number(-0.215 * Math.Cos(phi)),
                Number(-0.17 * Math.Sin(phi + 0.45))
            });
        }

        CurveSettings Curve(string name, LineStyleId style, int axis) => new CurveSettings
        {
            Name = name,
            LineStyle = style,
            Axis = axis,
            Mode = CurveMode.Smooth,
            Label = CurveLabelMode.Leader,
            Marker = MarkerShape.Dot,
            MarkerSizeMm = 1.6,
            MarkerPlacement = MarkerPlacement.Characteristic
        };
        project.Curves.Add(Curve("V_qS2X", LineStyleId.Main, 0));
        project.Curves.Add(Curve("V_qS2Y", LineStyleId.Thick, 0));
        project.Curves.Add(Curve("V_qBY", LineStyleId.Main, 0));
        project.Curves.Add(Curve("ω_q2", LineStyleId.DashedMain, 1));

        project.XAxis.Title = "φ";
        project.XAxis.Unit = "рад";
        project.XAxis.Numbers = NumberStyle.PiFractions;
        project.XAxis.GridStep = Math.PI / 6;

        project.YAxis.Title = "V_q";
        project.YAxis.Unit = "м";
        project.YAxis.AutoLimits = false;
        project.YAxis.Min = -0.25;
        project.YAxis.Max = 0.25;
        project.YAxis.GridStep = 0.1;

        project.ExtraAxes.Add(new AxisSettings
        {
            Title = "ω_q",
            Unit = "–",
            Side = AxisSide.Right,
            AutoLimits = false,
            Min = -0.25,
            Max = 0.25
        });

        GraphSettings g = project.Graph;
        g.WidthMm = 50 * Math.PI;          // 25 мм на радиан
        g.HeightMm = 82.5;                 // 165 мм на метр
        g.GridMode = GridStepMode.Units;
        g.ShowAxes = false;
        g.ShowFrame = true;
        g.TickLengthMm = 0;
        g.TitlePlacement = AxisTitlePlacement.Centered;
        g.SeparateArrows = true;
        g.Units = UnitPlacement.BetweenNumbers;
        g.ShowScaleFactors = true;
        return project;
    }

    /// <summary>
    /// Образец полярной диаграммы (как на черт. 2 Р 50-77-88): кривая силы света,
    /// нулевой луч направлен вниз, шкала радиуса 0…140 cd.
    /// </summary>
    public static GraphProject PolarProject()
    {
        var project = new GraphProject();
        project.Graph.Kind = DiagramKind.Polar;
        project.Data.XName = "α";
        for (int angle = 0; angle <= 360; angle += 10)
        {
            double a = angle * Math.PI / 180;
            // Широкий «лепесток» вниз и два малых вверх.
            double intensity = 20 + 100 * Math.Pow(Math.Cos(a / 2), 2) + 12 * Math.Pow(Math.Sin(a), 2) * (Math.Cos(a) < 0 ? 1 : 0.3);
            project.Data.Rows.Add(new[] { Number(angle), Number(intensity) });
        }
        project.Curves.Add(new CurveSettings { Name = "I", LineStyle = LineStyleId.Thick, Mode = CurveMode.Smooth });

        project.YAxis.Unit = "cd";
        project.YAxis.AutoLimits = false;
        project.YAxis.Min = 0;
        project.YAxis.Max = 140;
        project.YAxis.GridStep = 20;
        project.Polar.ZeroAngleDeg = 270;
        project.Polar.AngleStepDeg = 30;
        project.Polar.LabelRayDeg = 0;

        project.Graph.WidthMm = 120;
        project.Graph.GridMode = GridStepMode.Units;
        return project;
    }

    /// <summary>
    /// Образец треугольной диаграммы (как на черт. 9 Р 50-77-88): сплав Sn–Sb–Pb,
    /// две линии на поле составов.
    /// </summary>
    public static GraphProject TernaryProject()
    {
        var project = new GraphProject();
        project.Graph.Kind = DiagramKind.Ternary;
        project.Data.XName = "Sn";
        for (int i = 0; i <= 18; i++)
        {
            double sn = 2 + 5 * i;                         // 2…92 %
            double low = 3 + 0.0015 * (sn - 52) * (sn - 52);
            double upper = 6 + 0.55 * (92 - sn);
            project.Data.Rows.Add(new[] { Number(sn), Number(low), Number(upper) });
        }
        project.Curves.Add(new CurveSettings { Name = "1", LineStyle = LineStyleId.Thick, Mode = CurveMode.Smooth });
        project.Curves.Add(new CurveSettings { Name = "2", LineStyle = LineStyleId.Thick, Mode = CurveMode.Smooth });

        project.XAxis.Title = "Sn";
        project.YAxis.Title = "Sb";
        project.Ternary.TitleC = "Pb";
        project.Ternary.Unit = "%";
        project.Ternary.Total = 100;
        project.Ternary.GridStep = 10;
        project.Ternary.LabelEvery = 2;
        project.Graph.WidthMm = 120;
        return project;
    }

    /// <summary>Пример по имени: <c>sample</c>, <c>sample:tmm</c>, <c>sample:polar</c>, <c>sample:ternary</c>; иначе null.</summary>
    public static GraphProject ByName(string name)
    {
        switch ((name ?? "").Trim().ToLowerInvariant())
        {
            case "sample":
            case "sample:sqrt":
                return SqrtProject();
            case "sample:tmm":
                return TmmProject();
            case "sample:polar":
                return PolarProject();
            case "sample:ternary":
                return TernaryProject();
            default:
                return null;
        }
    }
}
