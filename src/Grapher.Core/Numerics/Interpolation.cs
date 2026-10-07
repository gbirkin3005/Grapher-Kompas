using System;
using System.Collections.Generic;
using Grapher.Core.Geometry;

namespace Grapher.Core.Numerics;

/// <summary>
/// Плавная кривая через точки в виде цепочки кубических сегментов Безье.
/// Сегменты Безье одинаково рисуются и в КОМПАС, и в предпросмотре, поэтому геометрия совпадает.
/// </summary>
public static class Interpolation
{
    /// <summary>Убирает подряд идущие совпадающие точки: они дают нулевые хорды.</summary>
    public static List<Pt> RemoveConsecutiveDuplicates(IReadOnlyList<Pt> points, double tolerance = 1e-9)
    {
        var result = new List<Pt>(points.Count);
        foreach (Pt p in points)
        {
            if (result.Count == 0 || Pt.Distance(result[result.Count - 1], p) > tolerance)
            {
                result.Add(p);
            }
        }
        return result;
    }

    /// <summary>X строго возрастает или строго убывает — кривая является графиком функции y(x).</summary>
    public static bool IsStrictlyMonotonicX(IReadOnlyList<Pt> points)
    {
        if (points.Count < 2) return false;
        bool increasing = points[1].X > points[0].X;
        for (int i = 0; i + 1 < points.Count; i++)
        {
            double dx = points[i + 1].X - points[i].X;
            if (increasing ? dx <= 0 : dx >= 0) return false;
        }
        return true;
    }

    /// <summary>
    /// Выбирает способ сглаживания: для графика функции — монотонный кубический сплайн
    /// (без ложных выбросов), для произвольной линии — центростремительный сплайн Катмулла — Рома.
    /// </summary>
    public static List<CubicBezier> Smooth(IReadOnlyList<Pt> points)
    {
        List<Pt> clean = RemoveConsecutiveDuplicates(points);
        if (clean.Count < 2) return new List<CubicBezier>();
        if (clean.Count == 2) return new List<CubicBezier> { CubicBezier.FromLine(clean[0], clean[1]) };
        return IsStrictlyMonotonicX(clean) ? MonotoneCubic(clean) : CatmullRomCentripetal(clean);
    }

    /// <summary>
    /// Монотонная кубическая интерполяция Эрмита (метод Фрича — Карлсона, PCHIP).
    /// На каждом участке кривая не выходит за значения в его концах, поэтому выбросов нет.
    /// Требует строго монотонных X.
    /// </summary>
    public static List<CubicBezier> MonotoneCubic(IReadOnlyList<Pt> points)
    {
        int n = points.Count;
        if (n < 2) return new List<CubicBezier>();
        if (!IsStrictlyMonotonicX(points))
        {
            throw new ArgumentException("Для монотонной интерполяции значения X должны строго возрастать или убывать.", nameof(points));
        }

        if (points[1].X < points[0].X)
        {
            // Убывающие X: считаем по развёрнутой последовательности и разворачиваем результат.
            var reversed = new List<Pt>(n);
            for (int i = n - 1; i >= 0; i--) reversed.Add(points[i]);
            List<CubicBezier> forward = MonotoneCubic(reversed);
            var backward = new List<CubicBezier>(forward.Count);
            for (int i = forward.Count - 1; i >= 0; i--) backward.Add(forward[i].Reversed());
            return backward;
        }

        var h = new double[n - 1];
        var delta = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            h[i] = points[i + 1].X - points[i].X;
            delta[i] = (points[i + 1].Y - points[i].Y) / h[i];
        }

        var m = new double[n];
        if (n == 2)
        {
            m[0] = m[1] = delta[0];
        }
        else
        {
            for (int i = 1; i < n - 1; i++)
            {
                if (delta[i - 1] == 0 || delta[i] == 0 || (delta[i - 1] > 0) != (delta[i] > 0))
                {
                    // Локальный экстремум или плоский участок: горизонтальная касательная.
                    m[i] = 0;
                }
                else
                {
                    double w1 = 2 * h[i] + h[i - 1];
                    double w2 = h[i] + 2 * h[i - 1];
                    m[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]);
                }
            }
            m[0] = EndSlope(h[0], h[1], delta[0], delta[1]);
            m[n - 1] = EndSlope(h[n - 2], h[n - 3], delta[n - 2], delta[n - 3]);
        }

        var result = new List<CubicBezier>(n - 1);
        for (int i = 0; i < n - 1; i++)
        {
            double third = h[i] / 3;
            Pt p0 = points[i], p3 = points[i + 1];
            var p1 = new Pt(p0.X + third, p0.Y + m[i] * third);
            var p2 = new Pt(p3.X - third, p3.Y - m[i + 1] * third);
            result.Add(new CubicBezier(p0, p1, p2, p3));
        }
        return result;
    }

    /// <summary>Наклон в крайней точке по трём точкам с сохранением формы.</summary>
    private static double EndSlope(double h0, double h1, double delta0, double delta1)
    {
        double slope = ((2 * h0 + h1) * delta0 - h0 * delta1) / (h0 + h1);
        if (delta0 == 0 || (slope > 0) != (delta0 > 0))
        {
            return 0;
        }
        if ((delta0 > 0) != (delta1 > 0) && Math.Abs(slope) > 3 * Math.Abs(delta0))
        {
            return 3 * delta0;
        }
        return slope;
    }

    /// <summary>
    /// Центростремительный сплайн Катмулла — Рома: проходит через все точки,
    /// не образует петель и острых изломов. Подходит для линий, где X не монотонен.
    /// </summary>
    public static List<CubicBezier> CatmullRomCentripetal(IReadOnlyList<Pt> points)
    {
        int n = points.Count;
        var result = new List<CubicBezier>(Math.Max(0, n - 1));
        if (n < 2) return result;
        if (n == 2)
        {
            result.Add(CubicBezier.FromLine(points[0], points[1]));
            return result;
        }

        bool closed = Pt.Distance(points[0], points[n - 1]) < 1e-9;

        // d[i] — длина участка [i, i+1] в параметре: корень из хорды.
        var d = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            d[i] = Math.Sqrt(Pt.Distance(points[i], points[i + 1]));
            if (!(d[i] > 0)) d[i] = 1e-9;
        }

        var tangents = new Pt[n];
        for (int i = 0; i < n; i++)
        {
            Pt prev, next;
            double dPrev, dNext;

            if (i > 0)
            {
                prev = points[i - 1];
                dPrev = d[i - 1];
            }
            else if (closed)
            {
                prev = points[n - 2];
                dPrev = d[n - 2];
            }
            else
            {
                // Фиктивная точка за началом: отражение соседней.
                prev = points[0] + (points[0] - points[1]);
                dPrev = d[0];
            }

            if (i < n - 1)
            {
                next = points[i + 1];
                dNext = d[i];
            }
            else if (closed)
            {
                next = points[1];
                dNext = d[0];
            }
            else
            {
                next = points[n - 1] + (points[n - 1] - points[n - 2]);
                dNext = d[n - 2];
            }

            Pt p = points[i];
            tangents[i] = (p - prev) / dPrev - (next - prev) / (dPrev + dNext) + (next - p) / dNext;
        }

        for (int i = 0; i < n - 1; i++)
        {
            double third = d[i] / 3;
            result.Add(new CubicBezier(
                points[i],
                points[i] + tangents[i] * third,
                points[i + 1] - tangents[i + 1] * third,
                points[i + 1]));
        }
        return result;
    }
}
