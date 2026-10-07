using System;
using System.Collections.Generic;
using System.Globalization;
using Grapher.Core.Geometry;
using Grapher.Core.Model;
using Grapher.Core.Numerics;
using Grapher.Core.Parsing;
using Grapher.Core.Scene;
using Grapher.Core.Serialization;

namespace Grapher.Core.Build;

/// <summary>
/// Превращает проект графика (данные и настройки) в список абстрактных примитивов.
/// Здесь сосредоточена вся геометрия; рендереры только переносят примитивы на чертёж или экран.
/// </summary>
public static class GraphBuilder
{
    /// <summary>Просвет между элементами оформления (засечка — подпись и т. п.), мм.</summary>
    private const double GapMm = 1.0;

    /// <summary>Тангенс половины угла раствора стрелки (угол стрелки 20°, как у размерных стрелок).</summary>
    private const double ArrowHalfWidthRatio = 0.176;

    private const double LegendSampleMm = 12.0;

    public static BuildResult Build(GraphProject project)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        ProjectSerializer.Normalize(project);

        var result = new BuildResult();
        GraphSettings g = project.Graph;

        ValidatedData data = DataValidator.Validate(project.Data, project.Curves);
        result.Issues.AddRange(data.Issues);
        foreach (SeriesPoints series in data.Series)
        {
            result.SourcePointCount += series.Points.Count;
        }

        double width = GraphSizeSolver.Clamp(g.WidthMm);
        double height = GraphSizeSolver.Clamp(g.HeightMm);

        // 1. Пределы осей.
        bool hasData = GetExtents(project, data, out double dataXMin, out double dataXMax, out double dataYMin, out double dataYMax);
        ResolveLimits(project.XAxis, "X", hasData, dataXMin, dataXMax, TickGenerator.AutoDivisions(width), result.Issues,
            out double xMin, out double xMax);
        ResolveLimits(project.YAxis, "Y", hasData, dataYMin, dataYMax, TickGenerator.AutoDivisions(height), result.Issues,
            out double yMin, out double yMax);

        // 2. Габариты и масштабные коэффициенты.
        if (g.EqualScale)
        {
            double equalHeight = GraphSizeSolver.HeightForEqualScale(width, xMax - xMin, yMax - yMin);
            height = GraphSizeSolver.Clamp(equalHeight);
            if (Math.Abs(height - equalHeight) > 1e-9)
            {
                result.Issues.Add(new DataIssue(IssueSeverity.Warning,
                    "При одинаковом масштабе по осям высота графика выходит за допустимые пределы — масштаб по Y отличается."));
            }
        }

        double kx = width / (xMax - xMin);
        double ky = height / (yMax - yMin);

        result.XMin = xMin;
        result.XMax = xMax;
        result.YMin = yMin;
        result.YMax = yMax;
        result.WidthMm = width;
        result.HeightMm = height;
        result.ScaleX = kx;
        result.ScaleY = ky;

        // 3. Сетка.
        AxisTicks ticksX = TickGenerator.Generate(g.GridMode, project.XAxis.GridStep, xMin, xMax, width, "X");
        AxisTicks ticksY = TickGenerator.Generate(g.GridMode, project.YAxis.GridStep, yMin, yMax, height, "Y");
        if (ticksX.Warning != null) result.Issues.Add(new DataIssue(IssueSeverity.Warning, ticksX.Warning));
        if (ticksY.Warning != null) result.Issues.Add(new DataIssue(IssueSeverity.Warning, ticksY.Warning));
        result.TicksX = ticksX;
        result.TicksY = ticksY;

        var ctx = new Context
        {
            Project = project,
            Result = result,
            Scene = new GraphScene { PlotRect = new RectD(0, 0, width, height) },
            Width = width,
            Height = height,
            Kx = kx,
            Ky = ky,
            XMin = xMin,
            YMin = yMin
        };

        // Положение осей: в углу рамки либо в нуле, если ноль внутри пределов.
        if (g.Crossing == AxesCrossing.AtZero)
        {
            if (xMin < 0 && xMax > 0) ctx.AxisYPos = (0 - xMin) * kx;
            if (yMin < 0 && yMax > 0) ctx.AxisXPos = (0 - yMin) * ky;
        }

        BuildGrid(ctx, ticksX, ticksY);
        BuildFrame(ctx);
        BuildAxes(ctx);
        BuildTicksAndLabels(ctx, ticksX, ticksY);
        BuildTitles(ctx);
        BuildCurves(ctx, data);
        BuildLegend(ctx);

        result.Scene = ctx.Scene;
        return result;
    }

    private sealed class Context
    {
        public GraphProject Project;
        public BuildResult Result;
        public GraphScene Scene;
        public double Width, Height, Kx, Ky, XMin, YMin;

        /// <summary>Ордината оси X и абсцисса оси Y в локальных координатах, мм.</summary>
        public double AxisXPos, AxisYPos;

        /// <summary>Наибольшая ширина числовых подписей оси Y — для отступа повёрнутого названия оси.</summary>
        public double MaxYLabelWidth;

        public GraphSettings G => Project.Graph;

        public Pt Map(Pt data) => new Pt((data.X - XMin) * Kx, (data.Y - YMin) * Ky);

        public double AxisEndX => G.ShowAxes && G.ShowArrows ? Width + Math.Max(0, G.AxisExtensionMm) : Width;
        public double AxisEndY => G.ShowAxes && G.ShowArrows ? Height + Math.Max(0, G.AxisExtensionMm) : Height;
    }

    // ------------------------------------------------------------------ пределы

    private static bool GetExtents(GraphProject project, ValidatedData data,
        out double xMin, out double xMax, out double yMin, out double yMax)
    {
        xMin = yMin = double.MaxValue;
        xMax = yMax = double.MinValue;
        bool any = false;

        foreach (SeriesPoints series in data.Series)
        {
            CurveSettings curve = project.Curves[series.CurveIndex];
            if (curve == null || !curve.Visible) continue;
            foreach (Pt p in series.Points)
            {
                any = true;
                if (p.X < xMin) xMin = p.X;
                if (p.X > xMax) xMax = p.X;
                if (p.Y < yMin) yMin = p.Y;
                if (p.Y > yMax) yMax = p.Y;
            }
        }
        return any;
    }

    private static void ResolveLimits(AxisSettings axis, string axisName, bool hasData, double dataMin, double dataMax,
        int targetDivisions, List<DataIssue> issues, out double min, out double max)
    {
        if (!axis.AutoLimits)
        {
            min = axis.Min;
            max = axis.Max;
            bool finite = !double.IsNaN(min) && !double.IsNaN(max) && !double.IsInfinity(min) && !double.IsInfinity(max);
            if (finite && max > min) return;

            issues.Add(new DataIssue(IssueSeverity.Error, string.Format(CultureInfo.CurrentCulture,
                "Пределы оси {0} заданы неверно: максимум должен быть больше минимума. Временно использованы пределы по данным.",
                axisName)));
        }

        if (!hasData)
        {
            min = 0;
            max = 10;
            return;
        }

        NiceScale.NiceLimits(dataMin, dataMax, targetDivisions, out min, out max, out _);
    }

    // ------------------------------------------------------------------ сетка, рамка, оси

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-6;

    private static void BuildGrid(Context c, AxisTicks ticksX, AxisTicks ticksY)
    {
        GraphSettings g = c.G;
        if (!g.ShowGrid) return;

        foreach (double value in ticksX.Values)
        {
            double x = (value - c.XMin) * c.Kx;
            // Линии, совпадающие с рамкой или осью, не дублируем: в КОМПАС это были бы наложенные объекты.
            if (g.ShowFrame && (Near(x, 0) || Near(x, c.Width))) continue;
            if (g.ShowAxes && Near(x, c.AxisYPos)) continue;
            c.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Grid, A = new Pt(x, 0), B = new Pt(x, c.Height), Style = g.GridStyle });
        }

        foreach (double value in ticksY.Values)
        {
            double y = (value - c.YMin) * c.Ky;
            if (g.ShowFrame && (Near(y, 0) || Near(y, c.Height))) continue;
            if (g.ShowAxes && Near(y, c.AxisXPos)) continue;
            c.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Grid, A = new Pt(0, y), B = new Pt(c.Width, y), Style = g.GridStyle });
        }
    }

    private static void BuildFrame(Context c)
    {
        GraphSettings g = c.G;
        if (!g.ShowFrame) return;

        double w = c.Width, h = c.Height;
        void Edge(Pt a, Pt b) =>
            c.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Frame, A = a, B = b, Style = LineStyleId.Thin });

        // Стороны, по которым проходят оси, рисует ось (основной линией).
        if (!(g.ShowAxes && Near(c.AxisXPos, 0))) Edge(new Pt(0, 0), new Pt(w, 0));
        if (!(g.ShowAxes && Near(c.AxisYPos, 0))) Edge(new Pt(0, 0), new Pt(0, h));
        Edge(new Pt(0, h), new Pt(w, h));
        Edge(new Pt(w, 0), new Pt(w, h));
    }

    private static void BuildAxes(Context c)
    {
        GraphSettings g = c.G;
        if (!g.ShowAxes) return;

        AddAxis(c, new Pt(0, c.AxisXPos), new Pt(c.AxisEndX, c.AxisXPos));
        AddAxis(c, new Pt(c.AxisYPos, 0), new Pt(c.AxisYPos, c.AxisEndY));
    }

    private static void AddAxis(Context c, Pt start, Pt end)
    {
        GraphSettings g = c.G;
        double arrow = g.ShowArrows ? Math.Max(0, g.ArrowLengthMm) : 0;
        double length = Pt.Distance(start, end);
        if (!(length > 0)) return;

        Pt dir = (end - start) / length;
        if (arrow <= 0 || arrow >= length)
        {
            c.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Axis, A = start, B = end, Style = LineStyleId.Main });
            return;
        }

        // Линия оси доходит до середины стрелки: остриё остаётся тонким.
        Pt lineEnd = end - dir * (arrow * 0.5);
        c.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Axis, A = start, B = lineEnd, Style = LineStyleId.Main });

        var normal = new Pt(-dir.Y, dir.X);
        Pt back = end - dir * arrow;
        double half = arrow * ArrowHalfWidthRatio;
        c.Scene.Add(new PolylinePrimitive
        {
            Role = PrimitiveRole.Arrow,
            Points = new List<Pt> { end, back + normal * half, back - normal * half },
            Closed = true,
            Filled = true,
            Style = LineStyleId.Thin
        });
    }

    // ------------------------------------------------------------------ засечки и подписи

    private static void BuildTicksAndLabels(Context c, AxisTicks ticksX, AxisTicks ticksY)
    {
        GraphSettings g = c.G;
        double tick = Math.Max(0, g.TickLengthMm);
        double hLabel = g.LabelHeightMm;
        bool labels = g.ShowTickLabels && hLabel > 0;

        // Ось X: засечки вниз от нижней стороны рамки, числа под ними.
        List<string> textsX = FormatLabels(ticksX, c.Project.XAxis, g.DecimalComma);
        int strideX = LabelStride(ticksX, c.Kx, c.Project.XAxis.LabelEvery, MaxWidth(textsX, hLabel) + GapMm, labels);
        for (int i = 0; i < ticksX.Values.Count; i++)
        {
            if (!IsLabeled(ticksX, i, strideX)) continue;
            double x = (ticksX.Values[i] - c.XMin) * c.Kx;
            if (tick > 0)
            {
                c.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Tick, A = new Pt(x, 0), B = new Pt(x, -tick), Style = LineStyleId.Thin });
            }
            if (labels)
            {
                c.Scene.Add(new TextPrimitive
                {
                    Role = PrimitiveRole.TickLabel,
                    Anchor = new Pt(x, -(tick + GapMm) - hLabel),
                    Text = textsX[i],
                    HeightMm = hLabel,
                    Align = TextAlign.Center,
                    FontName = DrawingFonts.ForText(g.FontName, textsX[i]),
                    Italic = g.Italic
                });
            }
        }

        // Ось Y: засечки влево от левой стороны рамки, числа слева, по центру засечки.
        List<string> textsY = FormatLabels(ticksY, c.Project.YAxis, g.DecimalComma);
        int strideY = LabelStride(ticksY, c.Ky, c.Project.YAxis.LabelEvery, hLabel * 1.4, labels);
        for (int i = 0; i < ticksY.Values.Count; i++)
        {
            if (!IsLabeled(ticksY, i, strideY)) continue;
            double y = (ticksY.Values[i] - c.YMin) * c.Ky;
            if (tick > 0)
            {
                c.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Tick, A = new Pt(0, y), B = new Pt(-tick, y), Style = LineStyleId.Thin });
            }
            if (labels)
            {
                c.MaxYLabelWidth = Math.Max(c.MaxYLabelWidth, TextMetrics.EstimateWidth(textsY[i], hLabel));
                c.Scene.Add(new TextPrimitive
                {
                    Role = PrimitiveRole.TickLabel,
                    Anchor = new Pt(-(tick + GapMm), y - hLabel / 2),
                    Text = textsY[i],
                    HeightMm = hLabel,
                    Align = TextAlign.Right,
                    FontName = DrawingFonts.ForText(g.FontName, textsY[i]),
                    Italic = g.Italic
                });
            }
        }
    }

    private static List<string> FormatLabels(AxisTicks ticks, AxisSettings axis, bool decimalComma)
    {
        int decimals = axis.Decimals >= 0 ? axis.Decimals : ticks.Decimals;
        var texts = new List<string>(ticks.Values.Count);
        foreach (double value in ticks.Values)
        {
            texts.Add(NumberFormatter.Format(value, decimals, decimalComma));
        }
        return texts;
    }

    private static double MaxWidth(List<string> texts, double height)
    {
        double max = 0;
        foreach (string text in texts)
        {
            max = Math.Max(max, TextMetrics.EstimateWidth(text, height));
        }
        return max;
    }

    /// <summary>
    /// Через сколько линий сетки ставить подпись: не чаще, чем задал пользователь,
    /// и не чаще, чем позволяет место (чтобы числа не налезали друг на друга).
    /// </summary>
    private static int LabelStride(AxisTicks ticks, double mmPerUnit, int userEvery, double neededMm, bool autoThin)
    {
        int stride = Math.Max(1, userEvery);
        if (!autoThin || ticks.Values.Count < 2) return stride;

        double spacing = Math.Abs(ticks.Values[1] - ticks.Values[0]) * mmPerUnit;
        if (!(spacing > 0)) return stride;

        int needed = (int)Math.Ceiling(neededMm / spacing - 1e-9);
        if (needed <= stride) return stride;

        // Прореживаем «круглым» шагом (2, 5, 10, 20…), чтобы подписанные значения остались ровными.
        int nice = 1;
        foreach (int factor in new[] { 1, 2, 5 })
        {
            for (int power = 1; power <= 100000; power *= 10)
            {
                int candidate = factor * power;
                if (candidate >= needed && (nice < needed || candidate < nice)) nice = candidate;
            }
        }
        return Math.Max(stride, nice);
    }

    private static bool IsLabeled(AxisTicks ticks, int index, int stride)
    {
        if (stride <= 1) return true;
        if (ticks.FromMin || !(ticks.Step > 0)) return index % stride == 0;

        // Значения кратны шагу: подписываем «круглые» номера, чтобы ноль всегда попадал в подписи.
        long number = (long)Math.Round(ticks.Values[index] / ticks.Step);
        return number % stride == 0;
    }

    // ------------------------------------------------------------------ названия осей

    private static void BuildTitles(Context c)
    {
        GraphSettings g = c.G;
        double hTitle = g.TitleHeightMm;
        if (!(hTitle > 0)) return;

        string titleX = (c.Project.XAxis.Title ?? "").Trim();
        string titleY = (c.Project.YAxis.Title ?? "").Trim();
        double tick = Math.Max(0, g.TickLengthMm);
        bool labels = g.ShowTickLabels && g.LabelHeightMm > 0;

        if (titleX.Length > 0)
        {
            var text = new TextPrimitive
            {
                Role = PrimitiveRole.AxisTitle,
                Text = titleX,
                HeightMm = hTitle,
                FontName = DrawingFonts.ForText(g.FontName, titleX),
                Italic = g.Italic
            };
            if (g.TitlePlacement == AxisTitlePlacement.AtEnd)
            {
                // Продолжение оси: текст сразу за стрелкой, по высоте — по центру линии оси.
                text.Align = TextAlign.Left;
                text.Anchor = new Pt(c.AxisEndX + 1.5 * GapMm, c.AxisXPos - hTitle / 2);
            }
            else
            {
                double offset = tick + GapMm + (labels ? g.LabelHeightMm + GapMm : 0) + 0.5 * GapMm;
                text.Align = TextAlign.Center;
                text.Anchor = new Pt(c.Width / 2, -offset - hTitle);
            }
            c.Scene.Add(text);
        }

        if (titleY.Length > 0)
        {
            var text = new TextPrimitive
            {
                Role = PrimitiveRole.AxisTitle,
                Text = titleY,
                HeightMm = hTitle,
                Align = TextAlign.Center,
                FontName = DrawingFonts.ForText(g.FontName, titleY),
                Italic = g.Italic
            };
            if (g.TitlePlacement == AxisTitlePlacement.AtEnd)
            {
                text.Anchor = new Pt(c.AxisYPos, c.AxisEndY + 1.5 * GapMm);
            }
            else
            {
                // Текст повёрнут на 90°: читается снизу вверх, буквы «растут» влево от базовой линии.
                double offset = tick + GapMm + (labels ? c.MaxYLabelWidth + GapMm : 0) + 0.5 * GapMm;
                text.AngleDeg = 90;
                text.Anchor = new Pt(-offset, c.Height / 2);
            }
            c.Scene.Add(text);
        }
    }

    // ------------------------------------------------------------------ кривые и маркеры

    private static void BuildCurves(Context c, ValidatedData data)
    {
        GraphSettings g = c.G;
        RectD rect = c.Scene.PlotRect;

        foreach (SeriesPoints series in data.Series)
        {
            CurveSettings curve = c.Project.Curves[series.CurveIndex];
            if (curve == null || !curve.Visible || series.Points.Count == 0) continue;

            int? color = ColorValue.Parse(curve.Color);
            var mapped = new List<Pt>(series.Points.Count);
            foreach (Pt p in series.Points)
            {
                mapped.Add(c.Map(p));
            }

            List<Pt> line = mapped;
            if (g.Decimate && mapped.Count > Math.Max(2, g.DecimateThreshold) && g.DecimateToleranceMm > 0)
            {
                line = Decimator.Simplify(mapped, g.DecimateToleranceMm);
                if (line.Count < mapped.Count) c.Result.Decimated = true;
            }

            if (line.Count >= 2)
            {
                if (curve.Mode == CurveMode.Smooth)
                {
                    // Сначала сглаживаем по всем точкам, потом обрезаем: так форма у границы не искажается.
                    List<CubicBezier> segments = Interpolation.Smooth(line);
                    foreach (List<CubicBezier> chain in Clipper.ClipBezierChain(rect, segments))
                    {
                        BezierPrimitive spline = BezierPrimitive.FromSegments(chain, curve.LineStyle, PrimitiveRole.Curve);
                        spline.Color = color;
                        c.Scene.Add(spline);
                        c.Result.CurveVertexCount += chain.Count + 1;
                    }
                }
                else
                {
                    foreach (List<Pt> piece in Clipper.ClipPolyline(rect, line))
                    {
                        c.Scene.Add(new PolylinePrimitive { Role = PrimitiveRole.Curve, Color = color, Points = piece, Style = curve.LineStyle });
                        c.Result.CurveVertexCount += piece.Count;
                    }
                }
            }

            if (curve.Marker != MarkerShape.None && curve.MarkerSizeMm > 0)
            {
                int every = Math.Max(1, curve.MarkerEvery);
                for (int i = 0; i < mapped.Count; i += every)
                {
                    if (rect.Contains(mapped[i], 1e-9))
                    {
                        AddMarker(c.Scene, curve.Marker, mapped[i], curve.MarkerSizeMm, PrimitiveRole.Marker, color);
                    }
                }
            }
        }
    }

    private static void AddMarker(GraphScene scene, MarkerShape shape, Pt center, double size, PrimitiveRole role, int? color)
    {
        double r = size / 2;
        switch (shape)
        {
            case MarkerShape.Circle:
                scene.Add(new CirclePrimitive { Role = role, Color = color, Center = center, Radius = r, Style = LineStyleId.Thin });
                break;

            case MarkerShape.Square:
                scene.Add(ClosedShape(role, color,
                    new Pt(center.X - r, center.Y - r), new Pt(center.X + r, center.Y - r),
                    new Pt(center.X + r, center.Y + r), new Pt(center.X - r, center.Y + r)));
                break;

            case MarkerShape.Triangle:
                double tr = r * 1.2;
                scene.Add(ClosedShape(role, color,
                    new Pt(center.X, center.Y + tr),
                    new Pt(center.X - tr * 0.866, center.Y - tr * 0.5),
                    new Pt(center.X + tr * 0.866, center.Y - tr * 0.5)));
                break;

            case MarkerShape.Diamond:
                double dr = r * 1.2;
                scene.Add(ClosedShape(role, color,
                    new Pt(center.X, center.Y + dr), new Pt(center.X - dr, center.Y),
                    new Pt(center.X, center.Y - dr), new Pt(center.X + dr, center.Y)));
                break;

            case MarkerShape.Cross:
                scene.Add(new LinePrimitive { Role = role, Color = color, A = new Pt(center.X - r, center.Y - r), B = new Pt(center.X + r, center.Y + r), Style = LineStyleId.Thin });
                scene.Add(new LinePrimitive { Role = role, Color = color, A = new Pt(center.X - r, center.Y + r), B = new Pt(center.X + r, center.Y - r), Style = LineStyleId.Thin });
                break;
        }
    }

    private static PolylinePrimitive ClosedShape(PrimitiveRole role, int? color, params Pt[] points) => new PolylinePrimitive
    {
        Role = role,
        Color = color,
        Points = new List<Pt>(points),
        Closed = true,
        Style = LineStyleId.Thin
    };

    // ------------------------------------------------------------------ легенда

    private static void BuildLegend(Context c)
    {
        GraphSettings g = c.G;
        if (!g.ShowLegend || !(g.LabelHeightMm > 0)) return;

        double h = g.LabelHeightMm;
        double pitch = Math.Max(h * 1.8, 5);
        double x0 = c.Width + 6 * GapMm;
        int row = 0;

        foreach (CurveSettings curve in c.Project.Curves)
        {
            if (curve == null || !curve.Visible) continue;

            double y = c.Height - pitch * (row + 0.5);
            row++;
            int? color = ColorValue.Parse(curve.Color);

            c.Scene.Add(new LinePrimitive
            {
                Role = PrimitiveRole.Legend,
                Color = color,
                A = new Pt(x0, y),
                B = new Pt(x0 + LegendSampleMm, y),
                Style = curve.LineStyle
            });
            if (curve.Marker != MarkerShape.None && curve.MarkerSizeMm > 0)
            {
                AddMarker(c.Scene, curve.Marker, new Pt(x0 + LegendSampleMm / 2, y), curve.MarkerSizeMm, PrimitiveRole.Legend, color);
            }
            c.Scene.Add(new TextPrimitive
            {
                Role = PrimitiveRole.Legend,
                Anchor = new Pt(x0 + LegendSampleMm + 2 * GapMm, y - h / 2),
                Text = string.IsNullOrWhiteSpace(curve.Name) ? "Y" : curve.Name.Trim(),
                HeightMm = h,
                Align = TextAlign.Left,
                FontName = DrawingFonts.ForText(g.FontName, string.IsNullOrWhiteSpace(curve.Name) ? "Y" : curve.Name.Trim()),
                Italic = g.Italic
            });
        }
    }
}
