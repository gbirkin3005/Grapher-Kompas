using System;
using System.Collections.Generic;
using System.Globalization;
using Grapher.Core.Geometry;
using Grapher.Core.Model;
using Grapher.Core.Numerics;
using Grapher.Core.Parsing;
using Grapher.Core.Scene;

namespace Grapher.Core.Build;

/// <summary>
/// Треугольная диаграмма трёх составляющих с постоянной суммой (Р 50-77-88, черт. 9).
/// Шкала A идёт по нижней стороне слева направо, шкала B — по правой стороне снизу вверх,
/// шкала C — по левой стороне сверху вниз. Начало координат сцены — левая нижняя вершина.
/// </summary>
internal static class TernaryBuilder
{
    private const double Gap = SceneTools.GapMm;
    private const double Sin60 = 0.8660254037844386;

    public static void Build(BuildContext c)
    {
        GraphProject project = c.Project;
        GraphSettings g = c.G;
        TernarySettings ternary = project.Ternary;
        BuildResult result = c.Result;
        GraphScene scene = c.Scene;

        double side = GraphSizeSolver.Clamp(g.WidthMm);
        double triangleHeight = side * Sin60;
        double total = ternary.Total > 0 && !double.IsInfinity(ternary.Total) ? ternary.Total : 100;

        // Точка состава (a, b): c = total − a − b.
        Pt Map(double a, double b) => new Pt((a + b / 2) / total * side, b / total * triangleHeight);

        // 1. Шаг сетки.
        double step = ternary.GridStep > 0 ? ternary.GridStep : total / 10;
        int count = (int)Math.Round(total / step);
        if (count < 1 || count > 200 || Math.Abs(count * step - total) > total * 1e-9)
        {
            if (ternary.GridStep > 0)
            {
                c.Warn("Шаг сетки треугольной диаграммы должен укладываться в сумму составляющих целое число раз — взята десятая часть суммы.");
            }
            count = 10;
            step = total / 10;
        }

        var ticks = new AxisTicks { Step = step, FromMin = true };
        for (int i = 0; i <= count; i++)
        {
            ticks.Values.Add(NiceScale.Snap(i * step, step));
        }
        ticks.Decimals = TickGenerator.DecimalsFor(ticks.Values, step);

        result.XMin = 0;
        result.XMax = total;
        result.YMin = 0;
        result.YMax = total;
        result.WidthMm = side;
        result.HeightMm = triangleHeight;
        result.ScaleX = side / total;
        result.ScaleY = side / total;
        result.TicksX = ticks;
        result.TicksY = ticks;
        result.YScales.Add(new ScaleResult { Name = "B", Min = 0, Max = total, Step = step, MmPerUnit = side / total });

        scene.PlotRect = new RectD(0, 0, side, triangleHeight);
        scene.InsertionPoint = new Pt(0, 0);

        Pt vertexC = Map(0, 0), vertexA = Map(total, 0), vertexB = Map(0, total);

        // 2. Сетка: три семейства линий, параллельных сторонам.
        if (g.ShowGrid)
        {
            for (int i = 1; i < count; i++)
            {
                double v = ticks.Values[i];
                // A = v: от нижней стороны к правой, параллельно левой стороне.
                scene.Add(new LinePrimitive { Role = PrimitiveRole.Grid, A = Map(v, 0), B = Map(v, total - v), Style = g.GridStyle });
                // B = v: от левой стороны к правой, параллельно нижней стороне.
                scene.Add(new LinePrimitive { Role = PrimitiveRole.Grid, A = Map(0, v), B = Map(total - v, v), Style = g.GridStyle });
                // C = v: от нижней стороны к левой, параллельно правой стороне.
                scene.Add(new LinePrimitive { Role = PrimitiveRole.Grid, A = Map(total - v, 0), B = Map(0, total - v), Style = g.GridStyle });
            }
        }

        // 3. Стороны треугольника — шкалы, ограничивающие поле: основной линией.
        scene.Add(new LinePrimitive { Role = PrimitiveRole.Frame, A = vertexC, B = vertexA, Style = LineStyleId.Main });
        scene.Add(new LinePrimitive { Role = PrimitiveRole.Frame, A = vertexA, B = vertexB, Style = LineStyleId.Main });
        scene.Add(new LinePrimitive { Role = PrimitiveRole.Frame, A = vertexB, B = vertexC, Style = LineStyleId.Main });

        // 4. Числа у шкал.
        double h = g.LabelHeightMm;
        bool labels = g.ShowTickLabels && h > 0;
        int every = Math.Max(1, ternary.LabelEvery);
        double spacing = side / count;
        double widest = 0;
        var texts = new List<string>();
        foreach (double value in ticks.Values)
        {
            string text = NumberFormatter.Format(value, ticks.Decimals, g.DecimalComma);
            texts.Add(text);
            widest = Math.Max(widest, TextMetrics.EstimateWidth(text, h));
        }
        // Не чаще, чем позволяет место вдоль нижней стороны.
        every = Math.Max(every, (int)Math.Ceiling((widest + Gap) / Math.Max(spacing, 1e-6) - 1e-9));

        var labeled = new List<int>();
        for (int i = count; i >= 0; i -= every) labeled.Insert(0, i);

        string unit = (ternary.Unit ?? "").Trim();
        bool unitBetween = labels && unit.Length > 0 && (g.Units == UnitPlacement.BetweenNumbers || g.Units == UnitPlacement.Fraction) &&
                           labeled.Count >= 2;
        int skipped = -1;
        double unitValue = 0;
        if (unitBetween)
        {
            int last = labeled[labeled.Count - 1], previous = labeled[labeled.Count - 2];
            double room = (last - previous) * spacing;
            if (room >= widest + TextMetrics.EstimateWidth(unit, h) + 3 * Gap)
            {
                unitValue = (ticks.Values[last] + ticks.Values[previous]) / 2;
            }
            else
            {
                skipped = previous;
                unitValue = ticks.Values[previous];
            }
        }

        // Числа у наклонных сторон отодвинуты так, чтобы текст не стёр на чертеже линию стороны:
        // нижний угол области, которую очищает текст, ближе к стороне, чем его середина.
        double offset = 1.5 * Gap;
        double sideOffset = offset + 0.5 * h;
        // Числа под нижней стороной опущены так, чтобы текст не закрыл её линию.
        double bottomBaseline = -Math.Max(offset + h, TextMetrics.ClearedAbove("0", h) + 0.5);

        // Точки шкал: A — на нижней стороне, B — на правой, C — на левой.
        Pt OnA(double v) => Map(v, 0);
        Pt OnB(double v) => Map(total - v, v);
        Pt OnC(double v) => Map(0, total - v);

        void LabelA(string text, double v, PrimitiveRole role) =>
            scene.Add(SceneTools.Text(g, role, text, new Pt(OnA(v).X, bottomBaseline), h, TextAlign.Center));

        void LabelB(string text, double v, PrimitiveRole role)
        {
            Pt p = OnB(v);
            scene.Add(SceneTools.Text(g, role, text, new Pt(p.X + sideOffset, p.Y - h / 2), h, TextAlign.Left));
        }

        void LabelC(string text, double v, PrimitiveRole role)
        {
            Pt p = OnC(v);
            scene.Add(SceneTools.Text(g, role, text, new Pt(p.X - sideOffset, p.Y - h / 2), h, TextAlign.Right));
        }

        if (labels)
        {
            foreach (int index in labeled)
            {
                if (index == skipped) continue;
                double v = ticks.Values[index];
                LabelA(texts[index], v, PrimitiveRole.TickLabel);
                LabelB(texts[index], v, PrimitiveRole.TickLabel);
                LabelC(texts[index], v, PrimitiveRole.TickLabel);
            }
            if (unitBetween)
            {
                LabelA(unit, unitValue, PrimitiveRole.Unit);
                LabelB(unit, unitValue, PrimitiveRole.Unit);
                LabelC(unit, unitValue, PrimitiveRole.Unit);
            }
        }

        // 5. Обозначения составляющих со стрелками направления возрастания (п. 1.3).
        double hTitle = g.TitleHeightMm;
        double labelRoom = labels ? sideOffset + widest : 0;
        if (hTitle > 0)
        {
            string Title(string title) =>
                g.Units == UnitPlacement.AfterTitle && unit.Length > 0
                    ? ((title ?? "").Trim().Length == 0 ? unit : title.Trim() + ", " + unit)
                    : (title ?? "").Trim();
            // Единица измерения за остриём каждой стрелки.
            string arrowUnit = g.Units == UnitPlacement.AtArrow ? unit : "";

            double arrowLength = Math.Max(8, 3 * hTitle);
            double arrowHead = Math.Min(g.ArrowLengthMm > 0 ? g.ArrowLengthMm : 4, arrowLength / 2);

            // A: под нижней шкалой, «обозначение →».
            string titleA = Title(project.XAxis.Title);
            if (titleA.Length > 0)
            {
                double width = TextMetrics.EstimateWidth(titleA, hTitle);
                double unitWidth = arrowUnit.Length > 0 ? 1.5 * Gap + TextMetrics.EstimateWidth(arrowUnit, hTitle) : 0;
                double baseline = (labels ? bottomBaseline - TextMetrics.ClearedBelow("0", h) : -offset)
                                  - Math.Max(1.5 * Gap + hTitle, TextMetrics.ClearedAbove(titleA, hTitle) + 0.2);
                double start = side / 2 - (width + 2 * Gap + arrowLength + unitWidth) / 2;
                scene.Add(SceneTools.Text(g, PrimitiveRole.AxisTitle, titleA, new Pt(start + width, baseline), hTitle, TextAlign.Right));
                double from = start + width + 2 * Gap, y = baseline + hTitle / 2;
                SceneTools.Arrow(scene, new Pt(from, y), new Pt(from + arrowLength, y), arrowHead, LineStyleId.Thin, PrimitiveRole.AxisTitle, g.NativeArrows);
                if (arrowUnit.Length > 0)
                {
                    scene.Add(SceneTools.Text(g, PrimitiveRole.Unit, arrowUnit, new Pt(from + arrowLength + 1.5 * Gap, baseline), hTitle, TextAlign.Left));
                }
            }

            // B: справа от правой стороны, стрелка вдоль стороны вверх; обозначение у хвоста стрелки.
            SideTitle(c, Title(project.YAxis.Title), arrowUnit, Map(total / 2, total / 2), new Pt(Sin60, 0.5), new Pt(-0.5, Sin60),
                labelRoom, hTitle, arrowLength, arrowHead);

            // C: слева от левой стороны, стрелка вдоль стороны вниз.
            SideTitle(c, Title(ternary.TitleC), arrowUnit, Map(0, total / 2), new Pt(-Sin60, 0.5), new Pt(-0.5, -Sin60),
                labelRoom, hTitle, arrowLength, arrowHead);
        }

        // 6. Кривые.
        var built = new List<BuiltCurve>();
        double tolerance = total * 1e-9;
        foreach (SeriesPoints series in c.Data.Series)
        {
            CurveSettings curve = project.Curves[series.CurveIndex];
            if (curve == null || !curve.Visible || series.Points.Count == 0) continue;

            int? color = ColorValue.Parse(curve.Color);
            var info = new BuiltCurve { CurveIndex = series.CurveIndex, Settings = curve, Color = color };
            built.Add(info);

            var mapped = new List<Pt>(series.Points.Count);
            int outside = 0, firstOutsideRow = 0;
            for (int i = 0; i < series.Points.Count; i++)
            {
                Pt p = series.Points[i];
                if (p.X < -tolerance || p.Y < -tolerance || p.X + p.Y > total + tolerance)
                {
                    if (outside++ == 0) firstOutsideRow = series.Rows[i];
                    continue;
                }
                mapped.Add(Map(p.X, p.Y));
            }
            if (outside > 0)
            {
                result.Issues.Add(new DataIssue(IssueSeverity.Warning, string.Format(CultureInfo.CurrentCulture,
                    "Кривая «{0}»: точек вне треугольника — {1} (первая в строке {2}). Составляющие не должны быть отрицательными, а их сумма — больше {3}.",
                    curve.Name, outside, firstOutsideRow, NumberFormatter.ForInput(total)), firstOutsideRow, series.CurveIndex + 1));
            }

            List<Pt> line = CurveTools.Decimate(c, mapped);
            if (line.Count >= 2)
            {
                List<Pt> nodes = Interpolation.RemoveConsecutiveDuplicates(line);
                if (curve.Mode == CurveMode.Smooth && nodes.Count > 2)
                {
                    List<CubicBezier> segments = Interpolation.CatmullRomCentripetal(nodes);
                    BezierPrimitive spline = BezierPrimitive.FromSegments(segments, curve.LineStyle, PrimitiveRole.Curve);
                    spline.Color = color;
                    scene.Add(spline);
                    result.CurveVertexCount += segments.Count + 1;
                    info.Pieces.Add(CurveTools.Sample(segments));
                }
                else if (nodes.Count >= 2)
                {
                    scene.Add(new PolylinePrimitive { Role = PrimitiveRole.Curve, Color = color, Points = nodes, Style = curve.LineStyle });
                    result.CurveVertexCount += nodes.Count;
                    info.Pieces.Add(nodes);
                }
            }

            if (curve.Marker != MarkerShape.None && curve.MarkerSizeMm > 0)
            {
                int markerEvery = Math.Max(1, curve.MarkerEvery);
                for (int i = 0; i < mapped.Count; i += markerEvery)
                {
                    SceneTools.Marker(scene, curve.Marker, mapped[i], curve.MarkerSizeMm, PrimitiveRole.Marker, color);
                }
            }
        }

        bool InsideTriangle(Pt p) =>
            p.Y >= 0.5 && p.Y <= Sin60 * 2 * p.X - 0.5 && p.Y <= Sin60 * 2 * (side - p.X) - 0.5;
        bool Inside(RectD box) =>
            InsideTriangle(new Pt(box.XMin, box.YMin)) && InsideTriangle(new Pt(box.XMax, box.YMin)) &&
            InsideTriangle(new Pt(box.XMax, box.YMax)) && InsideTriangle(new Pt(box.XMin, box.YMax));
        c.KeepOut.AddRange(LeaderLabels.Place(c, built, Inside));

        if (g.ShowLegend && h > 0)
        {
            CartesianBuilder.DrawLegend(c, side + labelRoom + 6 * Gap, triangleHeight, h, Math.Max(h * 1.8, 5));
        }
    }

    /// <summary>
    /// Обозначение у наклонной стороны: стрелка параллельна стороне и показывает направление возрастания,
    /// текст стоит горизонтально у хвоста стрелки.
    /// </summary>
    /// <param name="middle">Середина стороны.</param>
    /// <param name="outward">Единичная нормаль наружу.</param>
    /// <param name="direction">Единичное направление возрастания величины вдоль стороны.</param>
    private static void SideTitle(BuildContext c, string title, string unit, Pt middle, Pt outward, Pt direction, double labelRoom,
        double hTitle, double arrowLength, double arrowHead)
    {
        if (title.Length == 0) return;
        GraphSettings g = c.G;

        double width = TextMetrics.EstimateWidth(title, hTitle);
        Pt center = middle + outward * (labelRoom + 3 * Gap + hTitle);
        Pt tail = center - direction * (arrowLength / 2);
        Pt tip = center + direction * (arrowLength / 2);
        SceneTools.Arrow(c.Scene, tail, tip, arrowHead, LineStyleId.Thin, PrimitiveRole.AxisTitle, c.G.NativeArrows);

        // Центр надписи — за хвостом стрелки, на её продолжении.
        double reach = Math.Abs(direction.X) * width / 2 + Math.Abs(direction.Y) * hTitle / 2 + 1.5 * Gap;
        Pt textCenter = tail - direction * reach;
        c.Scene.Add(SceneTools.Text(g, PrimitiveRole.AxisTitle, title, new Pt(textCenter.X, textCenter.Y - hTitle / 2), hTitle, TextAlign.Center));

        // Единица измерения — за остриём стрелки, на её продолжении.
        if (string.IsNullOrEmpty(unit)) return;
        double unitWidth = TextMetrics.EstimateWidth(unit, hTitle);
        double unitReach = Math.Abs(direction.X) * unitWidth / 2 + Math.Abs(direction.Y) * hTitle / 2 + 1.5 * Gap;
        Pt unitCenter = tip + direction * unitReach;
        c.Scene.Add(SceneTools.Text(g, PrimitiveRole.Unit, unit, new Pt(unitCenter.X, unitCenter.Y - hTitle / 2), hTitle, TextAlign.Center));
    }
}
