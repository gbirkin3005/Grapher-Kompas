using System;
using System.Collections.Generic;
using Grapher.Core.Geometry;
using Grapher.Core.Model;
using Grapher.Core.Numerics;
using Grapher.Core.Scene;

namespace Grapher.Core.Build;

/// <summary>Кривая после построения: её видимые участки в виде ломаных (для расстановки подписей).</summary>
internal sealed class BuiltCurve
{
    public int CurveIndex;
    public CurveSettings Settings;
    public int? Color;

    /// <summary>Видимые участки кривой, мм. Плавная кривая представлена частыми точками.</summary>
    public List<List<Pt>> Pieces { get; } = new List<List<Pt>>();
}

/// <summary>Геометрия кривых, общая для всех видов диаграмм.</summary>
internal static class CurveTools
{
    /// <summary>
    /// Делит точки на участки по разрывам функции: две точки подряд с одним и тем же X и разными Y.
    /// </summary>
    /// <param name="xRange">Размах X — для сравнения абсцисс с допуском.</param>
    public static List<List<Pt>> SplitAtJumps(IReadOnlyList<Pt> points, bool enabled, double xRange)
    {
        var runs = new List<List<Pt>>();
        if (points.Count == 0) return runs;

        var current = new List<Pt> { points[0] };
        double tolerance = Math.Abs(xRange) * 1e-9;
        for (int i = 1; i < points.Count; i++)
        {
            Pt previous = points[i - 1], point = points[i];
            if (enabled && Math.Abs(point.X - previous.X) <= tolerance && point.Y != previous.Y)
            {
                runs.Add(current);
                current = new List<Pt>();
            }
            current.Add(point);
        }
        runs.Add(current);
        return runs;
    }

    /// <summary>Прореживание участка кривой, если точек больше порога.</summary>
    public static List<Pt> Decimate(BuildContext c, List<Pt> mapped)
    {
        GraphSettings g = c.G;
        if (!g.Decimate || mapped.Count <= Math.Max(2, g.DecimateThreshold) || !(g.DecimateToleranceMm > 0)) return mapped;

        List<Pt> simplified = Decimator.Simplify(mapped, g.DecimateToleranceMm);
        if (simplified.Count < mapped.Count) c.Result.Decimated = true;
        return simplified;
    }

    /// <summary>Частые точки вдоль цепочки сегментов Безье.</summary>
    public static List<Pt> Sample(IReadOnlyList<CubicBezier> chain, int perSegment = 6)
    {
        var points = new List<Pt>(chain.Count * perSegment + 1);
        if (chain.Count == 0) return points;

        points.Add(chain[0].P0);
        foreach (CubicBezier segment in chain)
        {
            for (int i = 1; i <= perSegment; i++)
            {
                points.Add(segment.Eval((double)i / perSegment));
            }
        }
        return points;
    }

    /// <summary>
    /// Ломаная, повторяющая цепочку сегментов Безье с заданной точностью, мм.
    /// Нужна там, где кривую приходится обрезать не по прямоугольнику.
    /// </summary>
    public static List<Pt> Flatten(IReadOnlyList<CubicBezier> chain, double toleranceMm)
    {
        var points = new List<Pt>();
        if (chain.Count == 0) return points;

        points.Add(chain[0].P0);
        foreach (CubicBezier segment in chain)
        {
            // Отклонение хорды от кривой оценивается по удалению управляющих точек от хорды.
            double bulge = Math.Max(Pt.DistanceToSegment(segment.P1, segment.P0, segment.P3),
                Pt.DistanceToSegment(segment.P2, segment.P0, segment.P3));
            int steps = (int)Math.Ceiling(Math.Sqrt(bulge / Math.Max(toleranceMm, 1e-6) * 0.75));
            if (steps < 1) steps = 1;
            if (steps > 64) steps = 64;
            for (int i = 1; i <= steps; i++)
            {
                points.Add(segment.Eval((double)i / steps));
            }
        }
        return points;
    }

    /// <summary>Точка на ломаной на заданной доле её длины и направление линии в этой точке.</summary>
    public static Pt PointAt(IReadOnlyList<Pt> polyline, double fraction, out Pt direction)
    {
        direction = new Pt(1, 0);
        if (polyline.Count == 0) return new Pt(0, 0);
        if (polyline.Count == 1) return polyline[0];

        double total = Length(polyline);
        double target = Math.Max(0, Math.Min(1, fraction)) * total;
        double walked = 0;
        for (int i = 0; i + 1 < polyline.Count; i++)
        {
            double step = Pt.Distance(polyline[i], polyline[i + 1]);
            if (step <= 0) continue;
            if (walked + step >= target || i + 2 == polyline.Count)
            {
                direction = (polyline[i + 1] - polyline[i]) / step;
                return Pt.Lerp(polyline[i], polyline[i + 1], Math.Max(0, Math.Min(1, (target - walked) / step)));
            }
            walked += step;
        }
        return polyline[polyline.Count - 1];
    }

    public static double Length(IReadOnlyList<Pt> polyline)
    {
        double length = 0;
        for (int i = 0; i + 1 < polyline.Count; i++)
        {
            length += Pt.Distance(polyline[i], polyline[i + 1]);
        }
        return length;
    }

    // ------------------------------------------------------------------ характерные точки

    /// <summary>
    /// Характерные точки участка кривой: концы, экстремумы и пересечения с нулевым уровнем.
    /// </summary>
    /// <param name="nodes">Вершины участка, мм.</param>
    /// <param name="segments">Сегменты плавной кривой между вершинами; null — ломаная.</param>
    /// <param name="zeroLevel">Ордината нулевого значения, мм; NaN — нулевой уровень не отмечать.</param>
    public static List<Pt> CharacteristicPoints(IReadOnlyList<Pt> nodes, IReadOnlyList<CubicBezier> segments, double zeroLevel)
    {
        var result = new List<Pt>();
        int n = nodes.Count;
        if (n == 0) return result;

        void Add(Pt p)
        {
            foreach (Pt existing in result)
            {
                if (Pt.Distance(existing, p) < 0.3) return;
            }
            result.Add(p);
        }

        Add(nodes[0]);

        // Экстремумы: вершина, в которой приращение ординаты меняет знак.
        int lastSign = 0, plateauStart = -1;
        for (int i = 0; i + 1 < n; i++)
        {
            double delta = nodes[i + 1].Y - nodes[i].Y;
            int sign = delta > 1e-9 ? 1 : delta < -1e-9 ? -1 : 0;
            if (sign == 0)
            {
                if (plateauStart < 0) plateauStart = i;
                continue;
            }
            if (lastSign != 0 && sign != lastSign)
            {
                Add(nodes[plateauStart >= 0 ? (plateauStart + i) / 2 : i]);
            }
            lastSign = sign;
            plateauStart = -1;
        }

        // Пересечения с нулём.
        if (!double.IsNaN(zeroLevel))
        {
            bool useSegments = segments != null && segments.Count == n - 1;
            for (int i = 0; i + 1 < n; i++)
            {
                double a = nodes[i].Y - zeroLevel, b = nodes[i + 1].Y - zeroLevel;
                if (Math.Abs(a) <= 1e-9)
                {
                    Add(nodes[i]);
                    continue;
                }
                if (a * b >= 0) continue;

                if (useSegments)
                {
                    CubicBezier s = segments[i];
                    foreach (double t in Clipper.CubicRoots01(s.P0.Y - zeroLevel, s.P1.Y - zeroLevel, s.P2.Y - zeroLevel, s.P3.Y - zeroLevel))
                    {
                        Add(s.Eval(t));
                    }
                }
                else
                {
                    Add(Pt.Lerp(nodes[i], nodes[i + 1], a / (a - b)));
                }
            }
            if (Math.Abs(nodes[n - 1].Y - zeroLevel) <= 1e-9) Add(nodes[n - 1]);
        }

        Add(nodes[n - 1]);
        return result;
    }
}
