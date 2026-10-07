using System;
using System.Collections.Generic;
using Grapher.Core.Geometry;

namespace Grapher.Core.Numerics;

/// <summary>
/// Обрезка кривых по рамке графика. Сегменты режутся точно по границе,
/// точки за пределами не отбрасываются «как есть».
/// </summary>
public static class Clipper
{
    /// <summary>Куски короче этого значения (мм) считаются вырожденными и отбрасываются.</summary>
    private const double MinPieceLength = 1e-9;

    /// <summary>
    /// Обрезка отрезка по прямоугольнику (алгоритм Лианга — Барски).
    /// Возвращает параметры видимой части на исходном отрезке.
    /// </summary>
    public static bool ClipSegment(RectD rect, Pt a, Pt b, out double t0, out double t1)
    {
        t0 = 0;
        t1 = 1;
        double dx = b.X - a.X, dy = b.Y - a.Y;

        return ClipEdge(-dx, a.X - rect.XMin, ref t0, ref t1)
            && ClipEdge(dx, rect.XMax - a.X, ref t0, ref t1)
            && ClipEdge(-dy, a.Y - rect.YMin, ref t0, ref t1)
            && ClipEdge(dy, rect.YMax - a.Y, ref t0, ref t1);
    }

    public static bool ClipSegment(RectD rect, ref Pt a, ref Pt b)
    {
        if (!ClipSegment(rect, a, b, out double t0, out double t1)) return false;
        Pt a0 = a, b0 = b;
        if (t0 > 0) a = Pin(rect, Pt.Lerp(a0, b0, t0));
        if (t1 < 1) b = Pin(rect, Pt.Lerp(a0, b0, t1));
        return true;
    }

    private static bool ClipEdge(double p, double q, ref double t0, ref double t1)
    {
        if (p == 0)
        {
            // Отрезок параллелен границе: он либо целиком по нужную сторону, либо целиком снаружи.
            return q >= 0;
        }

        double r = q / p;
        if (p < 0)
        {
            if (r > t1) return false;
            if (r > t0) t0 = r;
        }
        else
        {
            if (r < t0) return false;
            if (r < t1) t1 = r;
        }
        return true;
    }

    /// <summary>Возвращает точку пересечения ровно на границу, убирая погрешность вычислений.</summary>
    private static Pt Pin(RectD rect, Pt p)
    {
        double eps = 1e-9 * Math.Max(1.0, Math.Max(rect.Width, rect.Height));
        double x = p.X, y = p.Y;
        if (Math.Abs(x - rect.XMin) < eps) x = rect.XMin;
        else if (Math.Abs(x - rect.XMax) < eps) x = rect.XMax;
        if (Math.Abs(y - rect.YMin) < eps) y = rect.YMin;
        else if (Math.Abs(y - rect.YMax) < eps) y = rect.YMax;
        return new Pt(x, y);
    }

    /// <summary>
    /// Обрезка ломаной. Результат — список кусков, лежащих внутри прямоугольника.
    /// </summary>
    public static List<List<Pt>> ClipPolyline(RectD rect, IReadOnlyList<Pt> points)
    {
        var pieces = new List<List<Pt>>();
        if (points == null || points.Count < 2) return pieces;

        List<Pt> current = null;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            Pt a = points[i], b = points[i + 1];
            if (!ClipSegment(rect, a, b, out double t0, out double t1))
            {
                Flush(pieces, ref current);
                continue;
            }

            Pt ca = t0 > 0 ? Pin(rect, Pt.Lerp(a, b, t0)) : a;
            Pt cb = t1 < 1 ? Pin(rect, Pt.Lerp(a, b, t1)) : b;

            if (current == null || t0 > 0)
            {
                // Сегмент вошёл в рамку извне: начинается новый кусок.
                Flush(pieces, ref current);
                current = new List<Pt> { ca };
            }
            current.Add(cb);

            if (t1 < 1)
            {
                Flush(pieces, ref current);
            }
        }
        Flush(pieces, ref current);
        return pieces;
    }

    private static void Flush(List<List<Pt>> pieces, ref List<Pt> current)
    {
        if (current != null && current.Count >= 2 && PolylineLength(current) > MinPieceLength)
        {
            pieces.Add(current);
        }
        current = null;
    }

    private static double PolylineLength(List<Pt> points)
    {
        double length = 0;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            length += Pt.Distance(points[i], points[i + 1]);
        }
        return length;
    }

    /// <summary>
    /// Обрезка цепочки сегментов Безье. Каждый сегмент делится в точках пересечения с границами,
    /// остаются части, лежащие внутри. Результат — список непрерывных цепочек.
    /// </summary>
    public static List<List<CubicBezier>> ClipBezierChain(RectD rect, IReadOnlyList<CubicBezier> segments)
    {
        var chains = new List<List<CubicBezier>>();
        if (segments == null || segments.Count == 0) return chains;

        double eps = 1e-7 * Math.Max(1.0, Math.Max(rect.Width, rect.Height));
        List<CubicBezier> current = null;
        var cuts = new List<double>();

        foreach (CubicBezier segment in segments)
        {
            cuts.Clear();
            cuts.Add(0);
            AddRoots(cuts, segment.P0.X, segment.P1.X, segment.P2.X, segment.P3.X, rect.XMin);
            AddRoots(cuts, segment.P0.X, segment.P1.X, segment.P2.X, segment.P3.X, rect.XMax);
            AddRoots(cuts, segment.P0.Y, segment.P1.Y, segment.P2.Y, segment.P3.Y, rect.YMin);
            AddRoots(cuts, segment.P0.Y, segment.P1.Y, segment.P2.Y, segment.P3.Y, rect.YMax);
            cuts.Add(1);
            cuts.Sort();

            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                double ta = cuts[i], tb = cuts[i + 1];
                if (tb - ta < 1e-12) continue;

                bool inside = rect.Contains(segment.Eval((ta + tb) / 2), eps);
                if (!inside)
                {
                    FlushChain(chains, ref current);
                    continue;
                }

                CubicBezier part = segment.Sub(ta, tb);
                // Концы, полученные делением, ставим точно на границу.
                Pt start = ta > 0 ? Pin(rect, part.P0) : part.P0;
                Pt end = tb < 1 ? Pin(rect, part.P3) : part.P3;
                part = new CubicBezier(start, part.P1, part.P2, end);

                current ??= new List<CubicBezier>();
                current.Add(part);
            }
        }

        FlushChain(chains, ref current);
        return chains;
    }

    private static void FlushChain(List<List<CubicBezier>> chains, ref List<CubicBezier> current)
    {
        if (current != null && current.Count > 0)
        {
            chains.Add(current);
        }
        current = null;
    }

    /// <summary>Добавляет параметры t из (0, 1), в которых координата сегмента равна <paramref name="level"/>.</summary>
    private static void AddRoots(List<double> cuts, double p0, double p1, double p2, double p3, double level)
    {
        // Все управляющие точки по одну сторону от границы — пересечений нет (свойство выпуклой оболочки).
        double min = Math.Min(Math.Min(p0, p1), Math.Min(p2, p3));
        double max = Math.Max(Math.Max(p0, p1), Math.Max(p2, p3));
        if (level < min || level > max) return;

        foreach (double t in CubicRoots01(p0 - level, p1 - level, p2 - level, p3 - level))
        {
            if (t > 1e-12 && t < 1 - 1e-12) cuts.Add(t);
        }
    }

    /// <summary>
    /// Корни кубического многочлена Бернштейна с коэффициентами b0…b3 на отрезке [0, 1].
    /// Отрезок делится на участки монотонности, на каждом корень ищется делением пополам.
    /// Касание границы без пересечения корнем не считается.
    /// </summary>
    public static List<double> CubicRoots01(double b0, double b1, double b2, double b3)
    {
        var roots = new List<double>(3);

        // Производная: 3·[(b1−b0)(1−t)² + 2(b2−b1)(1−t)t + (b3−b2)t²] = A·t² + B·t + C.
        double d0 = b1 - b0, d1 = b2 - b1, d2 = b3 - b2;
        double qa = d0 - 2 * d1 + d2;
        double qb = 2 * (d1 - d0);
        double qc = d0;

        var breaks = new List<double>(4) { 0 };
        if (Math.Abs(qa) > 1e-14 * (Math.Abs(qb) + Math.Abs(qc) + 1e-300))
        {
            double disc = qb * qb - 4 * qa * qc;
            if (disc > 0)
            {
                double sq = Math.Sqrt(disc);
                // Устойчивая форма корней квадратного уравнения.
                double q = -0.5 * (qb + (qb >= 0 ? sq : -sq));
                AddBreak(breaks, q / qa);
                if (q != 0) AddBreak(breaks, qc / q);
            }
        }
        else if (qb != 0)
        {
            AddBreak(breaks, -qc / qb);
        }
        breaks.Add(1);
        breaks.Sort();

        for (int i = 0; i + 1 < breaks.Count; i++)
        {
            double lo = breaks[i], hi = breaks[i + 1];
            double flo = Bernstein(b0, b1, b2, b3, lo), fhi = Bernstein(b0, b1, b2, b3, hi);
            if (flo == 0)
            {
                AddRoot(roots, lo);
                continue;
            }
            if (fhi == 0)
            {
                if (i + 2 == breaks.Count) AddRoot(roots, hi);
                continue;
            }
            if ((flo < 0) == (fhi < 0)) continue;

            for (int iter = 0; iter < 80 && hi - lo > 1e-15; iter++)
            {
                double mid = 0.5 * (lo + hi);
                double fm = Bernstein(b0, b1, b2, b3, mid);
                if (fm == 0)
                {
                    lo = hi = mid;
                    break;
                }
                if ((fm < 0) == (flo < 0))
                {
                    lo = mid;
                    flo = fm;
                }
                else
                {
                    hi = mid;
                }
            }
            AddRoot(roots, 0.5 * (lo + hi));
        }

        return roots;
    }

    private static void AddBreak(List<double> breaks, double t)
    {
        if (t > 0 && t < 1) breaks.Add(t);
    }

    private static void AddRoot(List<double> roots, double t)
    {
        foreach (double existing in roots)
        {
            if (Math.Abs(existing - t) < 1e-12) return;
        }
        roots.Add(t);
    }

    private static double Bernstein(double b0, double b1, double b2, double b3, double t)
    {
        double u = 1 - t;
        return u * u * u * b0 + 3 * u * u * t * b1 + 3 * u * t * t * b2 + t * t * t * b3;
    }
}
