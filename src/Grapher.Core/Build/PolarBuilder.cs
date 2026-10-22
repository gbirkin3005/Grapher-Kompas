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
/// Диаграмма в полярной системе координат (Р 50-77-88, п. 1.2, 1.3, черт. 2).
/// Первый столбец таблицы — угол, остальные — радиусы кривых. Начало координат сцены — полюс.
/// </summary>
internal static class PolarBuilder
{
    private const double Gap = SceneTools.GapMm;

    public static void Build(BuildContext c)
    {
        GraphProject project = c.Project;
        GraphSettings g = c.G;
        PolarSettings polar = project.Polar;
        AxisSettings radial = project.YAxis;
        BuildResult result = c.Result;

        double diameter = GraphSizeSolver.Clamp(g.WidthMm);
        double radius = diameter / 2;

        // 1. Пределы радиальной шкалы.
        double low = double.MaxValue, high = double.MinValue;
        bool hasData = false;
        foreach (SeriesPoints series in c.Data.Series)
        {
            CurveSettings curve = project.Curves[series.CurveIndex];
            if (curve == null || !curve.Visible) continue;
            foreach (Pt p in series.Points)
            {
                hasData = true;
                if (p.Y < low) low = p.Y;
                if (p.Y > high) high = p.Y;
            }
        }

        double rMin, rMax;
        bool manual = !radial.AutoLimits && radial.Max > radial.Min && !double.IsNaN(radial.Min) && !double.IsInfinity(radial.Max);
        if (!radial.AutoLimits && !manual)
        {
            c.Error("Пределы радиуса заданы неверно: максимум должен быть больше минимума. Временно использованы пределы по данным.");
        }
        if (manual)
        {
            rMin = radial.Min;
            rMax = radial.Max;
        }
        else if (!hasData)
        {
            rMin = 0;
            rMax = 10;
        }
        else
        {
            // Радиус обычно отсчитывают от нуля; отрицательные значения сдвигают начало шкалы.
            NiceScale.NiceLimits(Math.Min(0, low), high, TickGenerator.AutoDivisions(radius), out rMin, out rMax, out _);
        }

        double k = radius / (rMax - rMin);
        AxisTicks ticks = TickGenerator.Generate(g.GridMode, radial.GridStep, rMin, rMax, radius, "R");
        if (ticks.Warning != null) c.Warn(ticks.Warning);

        // 2. Угловая сетка.
        double step = polar.AngleStepDeg;
        if (!(step >= 1) || step > 180) step = 30;
        int rayCount = (int)Math.Floor(360 / step + 1e-9);
        if (Math.Abs(rayCount * step - 360) < 1e-6) rayCount = (int)Math.Round(360 / step);
        else rayCount++;
        var angleTicks = new AxisTicks { Step = step };
        for (int i = 0; i < rayCount && i * step < 360 - 1e-9; i++)
        {
            angleTicks.Values.Add(i * step);
        }

        double zero = double.IsNaN(polar.ZeroAngleDeg) || double.IsInfinity(polar.ZeroAngleDeg) ? 0 : polar.ZeroAngleDeg;
        double sign = polar.Clockwise ? -1 : 1;
        Pt Direction(double dataDeg)
        {
            double a = (zero + sign * dataDeg) * Math.PI / 180;
            return new Pt(Math.Cos(a), Math.Sin(a));
        }

        result.XMin = 0;
        result.XMax = 360;
        result.YMin = rMin;
        result.YMax = rMax;
        result.WidthMm = diameter;
        result.HeightMm = diameter;
        result.ScaleX = k;
        result.ScaleY = k;
        result.TicksX = angleTicks;
        result.TicksY = ticks;
        result.YScales.Add(new ScaleResult { Name = "R", Min = rMin, Max = rMax, Step = ticks.Step, MmPerUnit = k });

        GraphScene scene = c.Scene;
        scene.PlotRect = new RectD(-radius, -radius, radius, radius);
        scene.InsertionPoint = new Pt(0, 0);
        var pole = new Pt(0, 0);

        // 3. Окружности и лучи сетки.
        double firstCircle = 0;
        foreach (double value in ticks.Values)
        {
            double rho = (value - rMin) * k;
            if (rho <= 1e-6 || rho >= radius - 1e-6) continue;
            if (firstCircle == 0) firstCircle = rho;
            if (g.ShowGrid)
            {
                scene.Add(new CirclePrimitive { Role = PrimitiveRole.Grid, Center = pole, Radius = rho, Style = g.GridStyle });
            }
        }

        if (g.ShowGrid)
        {
            foreach (double angle in angleTicks.Values)
            {
                // Все лучи в полюс не сводим: кроме четырёх главных, остальные начинаются от первой окружности.
                bool principal = Math.Abs(angle % 90) < 1e-6;
                double from = principal ? 0 : firstCircle;
                Pt u = Direction(angle);
                scene.Add(new LinePrimitive { Role = PrimitiveRole.Grid, A = u * from, B = u * radius, Style = g.GridStyle });
            }
        }

        // Внешняя окружность — шкала углов, ограничивающая поле: основной линией.
        scene.Add(new CirclePrimitive { Role = PrimitiveRole.Frame, Center = pole, Radius = radius, Style = LineStyleId.Main });

        double h = g.LabelHeightMm;
        bool labels = g.ShowTickLabels && h > 0;
        double tick = Math.Max(0, g.TickLengthMm);

        // 4. Числа у шкалы углов — вне поля.
        double angleLabelExtent = 0;
        for (int i = 0; i < angleTicks.Values.Count; i++)
        {
            double angle = angleTicks.Values[i];
            Pt u = Direction(angle);
            if (tick > 0 && !g.ShowGrid)
            {
                scene.Add(new LinePrimitive { Role = PrimitiveRole.Tick, A = u * radius, B = u * (radius + tick), Style = LineStyleId.Thin });
            }
            if (!labels) continue;

            bool last = i == angleTicks.Values.Count - 1;
            // Числа шкалы углов — от 0 до 360 либо от −180 до 180.
            double shown = polar.SignedAngles && angle > 180 + 1e-9 ? angle - 360 : angle;
            string text;
            if (polar.AngleUnit == AngleUnit.Radians && NumberFormatter.TryPiFraction(shown * Math.PI / 180, out int numerator, out int den))
            {
                text = NumberFormatter.PiNumerator(numerator);
                if (den != 1) text = TextMarkup.Fraction(text, den.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                text = NumberFormatter.Format(shown, TickGenerator.DecimalsFor(angleTicks.Values, step), g.DecimalComma);
                // Знак градуса — один раз, у последнего числа шкалы (п. 5.2), либо у каждого числа.
                if (polar.AngleUnit == AngleUnit.Degrees && (polar.DegreeSignOnAll || last)) text += "°";
            }

            // Надпись ставится так, чтобы область, которую текст очищает на чертеже, не задела окружность.
            RectD box = TextMetrics.ClearedBox(text, h, new Pt(0, 0), TextAlign.Center);
            double reach = Math.Abs(u.X) * box.Width / 2 + Math.Abs(u.Y) * box.Height / 2;
            Pt center = u * (radius + (g.ShowGrid ? 0 : tick) + Gap + reach);
            angleLabelExtent = Math.Max(angleLabelExtent, Gap + 2 * reach);

            var anchor = new Pt(center.X - box.Center.X, center.Y - box.Center.Y);
            scene.Add(SceneTools.Text(g, PrimitiveRole.TickLabel, text, anchor, h, TextAlign.Center));
        }

        // 5. Числа радиальной шкалы — на поле, вдоль выбранного луча, в углу между лучом и окружностью.
        if (labels)
        {
            DrawRadialLabels(c, ticks, radial, rMin, k, radius, Direction(polar.LabelRayDeg), polar.LabelSide, angleLabelExtent);
        }

        // 6. Кривые.
        var built = new List<BuiltCurve>();
        foreach (SeriesPoints series in c.Data.Series)
        {
            CurveSettings curve = project.Curves[series.CurveIndex];
            if (curve == null || !curve.Visible || series.Points.Count == 0) continue;

            int? color = ColorValue.Parse(curve.Color);
            var info = new BuiltCurve { CurveIndex = series.CurveIndex, Settings = curve, Color = color };
            built.Add(info);

            // Точки с радиусом меньше начала шкалы построить нельзя: кривая на них прерывается.
            var runs = new List<List<Pt>>();
            var current = new List<Pt>();
            bool below = false;
            foreach (Pt p in series.Points)
            {
                double rho = (p.Y - rMin) * k;
                if (rho < -1e-9)
                {
                    below = true;
                    if (current.Count > 0) runs.Add(current);
                    current = new List<Pt>();
                    continue;
                }
                double dataDeg = polar.AngleUnit == AngleUnit.Radians ? p.X * 180 / Math.PI : p.X;
                current.Add(Direction(dataDeg) * Math.Max(0, rho));
            }
            if (current.Count > 0) runs.Add(current);
            if (below)
            {
                c.Warn("Кривая «" + curve.Name + "»: значения радиуса меньше начала шкалы не построены.");
            }

            var dataPoints = new List<Pt>();
            foreach (List<Pt> mapped in runs)
            {
                dataPoints.AddRange(mapped);
                List<Pt> line = CurveTools.Decimate(c, mapped);
                if (line.Count < 2) continue;

                if (curve.Mode == CurveMode.Smooth)
                {
                    List<Pt> nodes = Interpolation.RemoveConsecutiveDuplicates(line);
                    if (nodes.Count < 2) continue;
                    List<CubicBezier> segments = nodes.Count == 2
                        ? new List<CubicBezier> { CubicBezier.FromLine(nodes[0], nodes[1]) }
                        : Interpolation.CatmullRomCentripetal(nodes);

                    List<Pt> flat = CurveTools.Flatten(segments, 0.02);
                    if (Exceeds(flat, radius))
                    {
                        // Кривая выходит за поле: обрезается по внешней окружности и строится ломаной с шагом точности 0,02 мм.
                        AddPolylines(c, info, ClipByCircle(flat, radius), curve, color);
                    }
                    else
                    {
                        BezierPrimitive spline = BezierPrimitive.FromSegments(segments, curve.LineStyle, PrimitiveRole.Curve);
                        spline.Color = color;
                        scene.Add(spline);
                        result.CurveVertexCount += segments.Count + 1;
                        info.Pieces.Add(CurveTools.Sample(segments));
                    }
                }
                else
                {
                    AddPolylines(c, info, ClipByCircle(line, radius), curve, color);
                }
            }

            if (curve.Marker != MarkerShape.None && curve.MarkerSizeMm > 0)
            {
                int every = Math.Max(1, curve.MarkerEvery);
                for (int i = 0; i < dataPoints.Count; i += every)
                {
                    if (dataPoints[i].Length <= radius + 1e-9)
                    {
                        SceneTools.Marker(scene, curve.Marker, dataPoints[i], curve.MarkerSizeMm, PrimitiveRole.Marker, color);
                    }
                }
            }
        }

        bool Inside(RectD box)
        {
            double limit = radius - 0.5;
            return new Pt(box.XMin, box.YMin).Length <= limit && new Pt(box.XMax, box.YMin).Length <= limit &&
                   new Pt(box.XMax, box.YMax).Length <= limit && new Pt(box.XMin, box.YMax).Length <= limit;
        }
        c.KeepOut.AddRange(LeaderLabels.Place(c, built, Inside));

        if (g.ShowLegend && h > 0)
        {
            CartesianBuilder.DrawLegend(c, radius + angleLabelExtent + 6 * Gap, radius, h, Math.Max(h * 1.8, 5));
        }
    }

    private static void AddPolylines(BuildContext c, BuiltCurve info, List<List<Pt>> pieces, CurveSettings curve, int? color)
    {
        foreach (List<Pt> piece in pieces)
        {
            c.Scene.Add(new PolylinePrimitive { Role = PrimitiveRole.Curve, Color = color, Points = piece, Style = curve.LineStyle });
            c.Result.CurveVertexCount += piece.Count;
            info.Pieces.Add(piece);
        }
    }

    private static bool Exceeds(List<Pt> points, double radius)
    {
        foreach (Pt p in points)
        {
            if (p.Length > radius + 1e-9) return true;
        }
        return false;
    }

    /// <summary>Части ломаной, лежащие внутри окружности с центром в начале координат.</summary>
    internal static List<List<Pt>> ClipByCircle(IReadOnlyList<Pt> points, double radius)
    {
        var pieces = new List<List<Pt>>();
        List<Pt> current = null;

        void Flush()
        {
            if (current != null && current.Count >= 2) pieces.Add(current);
            current = null;
        }

        for (int i = 0; i + 1 < points.Count; i++)
        {
            Pt a = points[i], b = points[i + 1];
            Pt d = b - a;
            // |a + t·d|² = R²
            double qa = d.X * d.X + d.Y * d.Y;
            double qb = 2 * (a.X * d.X + a.Y * d.Y);
            double qc = a.X * a.X + a.Y * a.Y - radius * radius;

            double t0 = 0, t1 = 1;
            bool visible;
            if (qa <= 1e-18)
            {
                visible = qc <= 1e-9;
            }
            else
            {
                double discriminant = qb * qb - 4 * qa * qc;
                if (discriminant < 0)
                {
                    visible = false;
                }
                else
                {
                    double root = Math.Sqrt(discriminant);
                    t0 = Math.Max(0, (-qb - root) / (2 * qa));
                    t1 = Math.Min(1, (-qb + root) / (2 * qa));
                    visible = t1 - t0 > 1e-12;
                }
            }

            if (!visible)
            {
                Flush();
                continue;
            }

            Pt from = t0 > 0 ? Pt.Lerp(a, b, t0) : a;
            Pt to = t1 < 1 ? Pt.Lerp(a, b, t1) : b;
            if (current == null || t0 > 0)
            {
                Flush();
                current = new List<Pt> { from };
            }
            current.Add(to);
            if (t1 < 1) Flush();
        }
        Flush();
        return pieces;
    }

    private static void DrawRadialLabels(BuildContext c, AxisTicks ticks, AxisSettings radial, double rMin, double k, double radius,
        Pt u, RadialLabelSide labelSide, double angleLabelExtent)
    {
        GraphSettings g = c.G;
        double h = g.LabelHeightMm;
        // Числа стоят справа от луча (если смотреть от полюса) — под горизонтальным лучом, направленным вправо, — или слева.
        var n = labelSide == RadialLabelSide.Left ? new Pt(-u.Y, u.X) : new Pt(u.Y, -u.X);
        int decimals = radial.Decimals >= 0 ? radial.Decimals : ticks.Decimals;

        var texts = new List<string>();
        var rhos = new List<double>();
        foreach (double value in ticks.Values)
        {
            double rho = (value - rMin) * k;
            if (rho <= 1e-6 || rho > radius + 1e-6) continue;
            texts.Add(NumberFormatter.Format(value, decimals, g.DecimalComma));
            rhos.Add(rho);
        }
        if (texts.Count == 0) return;

        // Вдоль луча числу нужно место по его ширине (горизонтальный луч) или высоте (вертикальный).
        double widest = 0;
        foreach (string text in texts) widest = Math.Max(widest, TextMetrics.EstimateWidth(text, h));
        double needed = Math.Abs(u.X) * widest + Math.Abs(u.Y) * 1.3 * h + Gap;
        double spacing = rhos.Count > 1 ? rhos[1] - rhos[0] : radius;
        int stride = Math.Max(Math.Max(1, radial.LabelEvery), (int)Math.Ceiling(needed / Math.Max(spacing, 1e-6) - 1e-9));

        var labeled = new List<int>();
        for (int i = texts.Count - 1; i >= 0; i -= stride) labeled.Insert(0, i);

        // Единица измерения — между последним и предпоследним числами; если тесно, вместо предпоследнего (п. 5.1).
        string unit = (radial.Unit ?? "").Trim();
        int skipped = -1;
        double unitRho = double.NaN;
        if (unit.Length > 0 && g.Units != UnitPlacement.AfterTitle && labeled.Count >= 2)
        {
            int last = labeled[labeled.Count - 1], previous = labeled[labeled.Count - 2];
            if (rhos[last] - rhos[previous] >= 2 * needed)
            {
                unitRho = (rhos[last] + rhos[previous]) / 2;
            }
            else
            {
                skipped = previous;
                unitRho = rhos[previous];
            }
        }

        void Place(string text, double rho, PrimitiveRole role)
        {
            // Число стоит в углу между лучом шкалы и своей окружностью. Положение рассчитано по области,
            // которую текст очищает на чертеже: ни луч, ни окружность она задеть не должна.
            RectD box = TextMetrics.ClearedBox(text, h, new Pt(0, 0), TextAlign.Center);
            double along = Math.Abs(u.X) * box.Width / 2 + Math.Abs(u.Y) * box.Height / 2;
            double across = Math.Abs(n.X) * box.Width / 2 + Math.Abs(n.Y) * box.Height / 2;
            Pt center = u * (rho - along - 0.5 * Gap) + n * (across + 0.5 * Gap);
            var anchor = new Pt(center.X - box.Center.X, center.Y - box.Center.Y);
            c.Scene.Add(SceneTools.Text(g, role, text, anchor, h, TextAlign.Center));
            c.KeepOut.Add(TextMetrics.ClearedBox(text, h, anchor, TextAlign.Center));
        }

        foreach (int index in labeled)
        {
            if (index != skipped) Place(texts[index], rhos[index], PrimitiveRole.TickLabel);
        }
        if (!double.IsNaN(unitRho)) Place(unit, unitRho, PrimitiveRole.Unit);

        // Обозначение величины — за пределами поля, рядом с лучом шкалы.
        string title = (radial.Title ?? "").Trim();
        if (g.Units == UnitPlacement.AfterTitle && unit.Length > 0) title = title.Length == 0 ? unit : title + ", " + unit;
        double hTitle = g.TitleHeightMm;
        if (title.Length > 0 && hTitle > 0)
        {
            double width = TextMetrics.EstimateWidth(title, hTitle);
            double along = Math.Abs(u.X) * width / 2 + Math.Abs(u.Y) * hTitle / 2;
            double across = Math.Abs(n.X) * width / 2 + Math.Abs(n.Y) * hTitle / 2;
            Pt center = u * (radius + 2 * Gap + along) + n * (across + h + 2 * Gap);
            c.Scene.Add(SceneTools.Text(g, PrimitiveRole.AxisTitle, title, new Pt(center.X, center.Y - hTitle / 2), hTitle, TextAlign.Center));
        }
    }
}
