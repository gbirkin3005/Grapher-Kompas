using System;
using System.Collections.Generic;
using System.Linq;
using Grapher.Core.Geometry;
using Grapher.Core.Numerics;
using Xunit;

namespace Grapher.Core.Tests;

internal static class Close
{
    public static void Equal(double expected, double actual, double tolerance = 1e-9)
    {
        Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"Ожидалось {expected:R}, получено {actual:R} (допуск {tolerance:R})");
    }

    public static void Equal(Pt expected, Pt actual, double tolerance = 1e-9)
    {
        Assert.True(Pt.Distance(expected, actual) <= tolerance,
            $"Ожидалась точка {expected}, получена {actual} (допуск {tolerance:R})");
    }
}

public class ClipperTests
{
    private static readonly RectD Rect = new RectD(0, 0, 100, 100);

    [Fact]
    public void Segment_inside_is_unchanged()
    {
        Pt a = new Pt(10, 20), b = new Pt(90, 80);

        Assert.True(Clipper.ClipSegment(Rect, ref a, ref b));
        Assert.Equal(new Pt(10, 20), a);
        Assert.Equal(new Pt(90, 80), b);
    }

    [Fact]
    public void Segment_is_cut_exactly_on_the_border()
    {
        Pt a = new Pt(50, 50), b = new Pt(150, 100);

        Assert.True(Clipper.ClipSegment(Rect, ref a, ref b));
        Assert.Equal(new Pt(50, 50), a);
        Assert.Equal(100, b.X);            // ровно на границе, а не «последняя точка внутри»
        Close.Equal(75, b.Y);
    }

    [Fact]
    public void Segment_crossing_the_whole_frame_is_cut_on_both_sides()
    {
        Pt a = new Pt(-50, 50), b = new Pt(150, 50);

        Assert.True(Clipper.ClipSegment(Rect, ref a, ref b));
        Assert.Equal(new Pt(0, 50), a);
        Assert.Equal(new Pt(100, 50), b);
    }

    [Theory]
    [InlineData(-20, 10, -5, 90)]      // слева
    [InlineData(10, 120, 90, 150)]     // сверху
    [InlineData(-50, 50, 50, 170)]     // мимо угла
    public void Segment_outside_is_rejected(double x1, double y1, double x2, double y2)
    {
        Pt a = new Pt(x1, y1), b = new Pt(x2, y2);

        Assert.False(Clipper.ClipSegment(Rect, ref a, ref b));
    }

    [Fact]
    public void Polyline_leaving_and_re_entering_gives_two_pieces()
    {
        var points = new[] { new Pt(10, 10), new Pt(50, 150), new Pt(90, 10) };

        List<List<Pt>> pieces = Clipper.ClipPolyline(Rect, points);

        Assert.Equal(2, pieces.Count);
        Assert.Equal(new Pt(10, 10), pieces[0][0]);
        Close.Equal(new Pt(10 + 40 * 90.0 / 140, 100), pieces[0][1]);
        Close.Equal(new Pt(50 + 40 * 50.0 / 140, 100), pieces[1][0]);
        Assert.Equal(new Pt(90, 10), pieces[1][1]);
        Assert.Equal(100, pieces[0][1].Y);
        Assert.Equal(100, pieces[1][0].Y);
    }

    [Fact]
    public void Polyline_with_all_vertices_outside_still_keeps_the_crossing_part()
    {
        // Если просто выбросить точки за пределами, от этой линии не останется ничего.
        var points = new[] { new Pt(-10, 50), new Pt(110, 50) };

        List<Pt> piece = Assert.Single(Clipper.ClipPolyline(Rect, points));

        Assert.Equal(new[] { new Pt(0, 50), new Pt(100, 50) }, piece);
    }

    [Fact]
    public void Polyline_touching_the_border_stays_in_one_piece()
    {
        var points = new[] { new Pt(10, 10), new Pt(50, 100), new Pt(90, 10) };

        List<Pt> piece = Assert.Single(Clipper.ClipPolyline(Rect, points));

        Assert.Equal(points, piece);
    }

    [Fact]
    public void Polyline_inside_is_unchanged()
    {
        Pt[] points = Enumerable.Range(0, 11).Select(i => new Pt(i * 10, 50 + 40 * Math.Sin(i))).ToArray();

        List<Pt> piece = Assert.Single(Clipper.ClipPolyline(Rect, points));

        Assert.Equal(points, piece);
    }

    [Fact]
    public void Polyline_outside_gives_nothing()
    {
        var points = new[] { new Pt(-10, -10), new Pt(-20, 50), new Pt(-5, 120) };

        Assert.Empty(Clipper.ClipPolyline(Rect, points));
    }

    [Fact]
    public void Cubic_roots_are_found_on_the_unit_interval()
    {
        // f(t) = t − 0,25 в базисе Бернштейна.
        List<double> linear = Clipper.CubicRoots01(-0.25, -0.25 + 1.0 / 3, -0.25 + 2.0 / 3, 0.75);
        Close.Equal(0.25, Assert.Single(linear), 1e-12);

        // Многочлен с тремя корнями: (t−0,2)(t−0,5)(t−0,8), коэффициенты Бернштейна получены из степенных.
        // t³ − 1,5t² + 0,66t − 0,08: b0 = −0,08; b1 = b0 + 0,66/3; b2 = b1 + (0,66 − 1,5)/3 + ... считаем напрямую.
        double c0 = -0.08, c1 = 0.66, c2 = -1.5, c3 = 1;
        double b0 = c0, b1 = c0 + c1 / 3, b2 = c0 + 2 * c1 / 3 + c2 / 3, b3 = c0 + c1 + c2 + c3;
        List<double> roots = Clipper.CubicRoots01(b0, b1, b2, b3);
        roots.Sort();

        Assert.Equal(3, roots.Count);
        Close.Equal(0.2, roots[0], 1e-10);
        Close.Equal(0.5, roots[1], 1e-10);
        Close.Equal(0.8, roots[2], 1e-10);
    }

    [Fact]
    public void Bezier_inside_is_unchanged()
    {
        var segment = new CubicBezier(new Pt(10, 10), new Pt(30, 60), new Pt(60, 60), new Pt(90, 20));

        List<CubicBezier> chain = Assert.Single(Clipper.ClipBezierChain(Rect, new[] { segment }));

        CubicBezier result = Assert.Single(chain);
        Assert.Equal(segment.P0, result.P0);
        Assert.Equal(segment.P1, result.P1);
        Assert.Equal(segment.P2, result.P2);
        Assert.Equal(segment.P3, result.P3);
    }

    [Fact]
    public void Straight_bezier_is_cut_like_a_segment()
    {
        CubicBezier segment = CubicBezier.FromLine(new Pt(-50, 50), new Pt(150, 50));

        List<CubicBezier> chain = Assert.Single(Clipper.ClipBezierChain(Rect, new[] { segment }));

        CubicBezier result = Assert.Single(chain);
        Assert.Equal(new Pt(0, 50), result.P0);
        Assert.Equal(new Pt(100, 50), result.P3);
        Close.Equal(new Pt(50, 50), result.Eval(0.5));
    }

    [Fact]
    public void Bezier_arch_above_the_frame_is_cut_into_two_parts_on_the_border()
    {
        // Арка поднимается до y = 162,5 — выше верхней границы рамки.
        var arch = new CubicBezier(new Pt(10, 50), new Pt(10, 200), new Pt(90, 200), new Pt(90, 50));

        List<List<CubicBezier>> chains = Clipper.ClipBezierChain(Rect, new[] { arch });

        Assert.Equal(2, chains.Count);
        Assert.Equal(new Pt(10, 50), chains[0][0].P0);
        Assert.Equal(100, chains[0].Last().P3.Y);
        Assert.Equal(100, chains[1][0].P0.Y);
        Assert.Equal(new Pt(90, 50), chains[1].Last().P3);

        // Оставшиеся части лежат на исходной кривой и не выходят за рамку.
        foreach (CubicBezier part in chains.SelectMany(c => c))
        {
            for (int i = 0; i <= 20; i++)
            {
                Pt p = part.Eval(i / 20.0);
                Assert.True(Rect.Contains(p, 1e-9), $"Точка {p} вне рамки");
                Assert.True(DistanceToCurve(arch, p) < 1e-6, $"Точка {p} не лежит на исходной кривой");
            }
        }
    }

    [Fact]
    public void Bezier_chain_stays_connected_across_segments_inside_the_frame()
    {
        var points = Enumerable.Range(0, 6).Select(i => new Pt(10 + i * 16, 50 + 30 * Math.Sin(i))).ToList();
        List<CubicBezier> segments = Interpolation.MonotoneCubic(points);

        List<CubicBezier> chain = Assert.Single(Clipper.ClipBezierChain(Rect, segments));

        Assert.Equal(segments.Count, chain.Count);
    }

    /// <summary>Расстояние от точки до кривой: грубый перебор по параметру и уточнение делением отрезка.</summary>
    private static double DistanceToCurve(CubicBezier curve, Pt p)
    {
        const int samples = 2000;
        int bestIndex = 0;
        double best = double.MaxValue;
        for (int i = 0; i <= samples; i++)
        {
            double d = Pt.Distance(curve.Eval((double)i / samples), p);
            if (d < best)
            {
                best = d;
                bestIndex = i;
            }
        }

        double lo = Math.Max(0, (bestIndex - 1.0) / samples), hi = Math.Min(1, (bestIndex + 1.0) / samples);
        for (int iter = 0; iter < 100; iter++)
        {
            double m1 = lo + (hi - lo) / 3, m2 = hi - (hi - lo) / 3;
            if (Pt.Distance(curve.Eval(m1), p) < Pt.Distance(curve.Eval(m2), p)) hi = m2;
            else lo = m1;
        }
        return Math.Min(best, Pt.Distance(curve.Eval((lo + hi) / 2), p));
    }
}

public class CubicBezierTests
{
    [Fact]
    public void Split_parts_join_at_the_split_point_and_follow_the_curve()
    {
        var curve = new CubicBezier(new Pt(0, 0), new Pt(10, 40), new Pt(50, -20), new Pt(60, 30));

        curve.Split(0.3, out CubicBezier left, out CubicBezier right);

        Close.Equal(curve.Eval(0.3), left.P3);
        Close.Equal(curve.Eval(0.3), right.P0);
        Close.Equal(curve.Eval(0.15), left.Eval(0.5));
        Close.Equal(curve.Eval(0.65), right.Eval(0.5));
    }

    [Fact]
    public void Sub_curve_matches_the_original_between_parameters()
    {
        var curve = new CubicBezier(new Pt(0, 0), new Pt(10, 40), new Pt(50, -20), new Pt(60, 30));

        CubicBezier part = curve.Sub(0.2, 0.7);

        Close.Equal(curve.Eval(0.2), part.P0);
        Close.Equal(curve.Eval(0.7), part.P3);
        Close.Equal(curve.Eval(0.45), part.Eval(0.5));
        Close.Equal(curve.Eval(0.325), part.Eval(0.25));
    }
}

public class InterpolationTests
{
    private static List<Pt> SqrtPoints(int count = 100) =>
        Enumerable.Range(0, count).Select(i =>
        {
            double x = 100.0 * i / (count - 1);
            return new Pt(x, Math.Sqrt(x));
        }).ToList();

    [Fact]
    public void Monotone_curve_passes_through_every_point()
    {
        List<Pt> points = SqrtPoints();

        List<CubicBezier> segments = Interpolation.MonotoneCubic(points);

        Assert.Equal(points.Count - 1, segments.Count);
        for (int i = 0; i < segments.Count; i++)
        {
            Assert.Equal(points[i], segments[i].P0);
            Assert.Equal(points[i + 1], segments[i].P3);
        }
    }

    [Fact]
    public void Monotone_curve_has_no_overshoot_on_step_data()
    {
        // Ступенька: обычный кубический сплайн дал бы «горбы» ниже 0 и выше 1.
        var points = new[]
        {
            new Pt(0, 0), new Pt(1, 0), new Pt(2, 0), new Pt(3, 1), new Pt(4, 1), new Pt(5, 1), new Pt(6, 0.2), new Pt(7, 0.2)
        };

        List<CubicBezier> segments = Interpolation.MonotoneCubic(points);

        // Управляющие точки лежат между концами участка, значит и вся кривая тоже (свойство выпуклой оболочки).
        foreach (CubicBezier s in segments)
        {
            double lo = Math.Min(s.P0.Y, s.P3.Y) - 1e-12, hi = Math.Max(s.P0.Y, s.P3.Y) + 1e-12;
            Assert.InRange(s.P1.Y, lo, hi);
            Assert.InRange(s.P2.Y, lo, hi);
            for (int i = 0; i <= 50; i++)
            {
                Assert.InRange(s.Eval(i / 50.0).Y, lo, hi);
            }
        }
    }

    [Fact]
    public void Monotone_data_gives_a_monotone_curve()
    {
        List<CubicBezier> segments = Interpolation.MonotoneCubic(SqrtPoints());

        Pt previous = segments[0].P0;
        foreach (CubicBezier s in segments)
        {
            for (int i = 1; i <= 20; i++)
            {
                Pt p = s.Eval(i / 20.0);
                Assert.True(p.X > previous.X, "X должен возрастать");
                Assert.True(p.Y >= previous.Y - 1e-12, "Y не должен убывать");
                previous = p;
            }
        }
    }

    [Fact]
    public void Smooth_curve_is_close_to_the_function_between_points()
    {
        List<CubicBezier> segments = Interpolation.MonotoneCubic(SqrtPoints());

        double MaxError(IEnumerable<CubicBezier> part)
        {
            double max = 0;
            foreach (CubicBezier s in part)
            {
                for (int i = 0; i <= 20; i++)
                {
                    Pt p = s.Eval(i / 20.0);
                    max = Math.Max(max, Math.Abs(p.Y - Math.Sqrt(p.X)));
                }
            }
            return max;
        }

        // Первый участок не проверяем: у √x в нуле бесконечная производная.
        // Диапазон Y равен 10, поэтому 0,02 — это 0,2 % высоты графика.
        double nearZero = MaxError(segments.Skip(1));
        double regular = MaxError(segments.Skip(5));

        Assert.True(nearZero < 0.02, $"Отклонение от √x у нуля: {nearZero}");
        Assert.True(regular < 0.002, $"Отклонение от √x на основном участке: {regular}");
    }

    [Fact]
    public void Decreasing_x_is_supported()
    {
        List<Pt> points = SqrtPoints(20);
        points.Reverse();

        List<CubicBezier> segments = Interpolation.MonotoneCubic(points);

        Assert.Equal(points[0], segments[0].P0);
        Assert.Equal(points[points.Count - 1], segments[segments.Count - 1].P3);
        for (int i = 0; i + 1 < segments.Count; i++)
        {
            Assert.Equal(segments[i].P3, segments[i + 1].P0);
        }
    }

    [Fact]
    public void Monotone_interpolation_requires_monotone_x()
    {
        var points = new[] { new Pt(0, 0), new Pt(2, 1), new Pt(1, 2) };

        Assert.Throws<ArgumentException>(() => Interpolation.MonotoneCubic(points));
    }

    [Fact]
    public void Curve_with_non_monotone_x_uses_catmull_rom_and_is_smooth_at_joints()
    {
        // Дуга окружности: X сначала растёт, потом убывает.
        List<Pt> points = Enumerable.Range(0, 13)
            .Select(i => new Pt(50 + 40 * Math.Cos(i * Math.PI / 8), 50 + 40 * Math.Sin(i * Math.PI / 8))).ToList();
        Assert.False(Interpolation.IsStrictlyMonotonicX(points));

        List<CubicBezier> segments = Interpolation.Smooth(points);

        Assert.Equal(points.Count - 1, segments.Count);
        for (int i = 0; i < segments.Count; i++)
        {
            Assert.Equal(points[i], segments[i].P0);
            Assert.Equal(points[i + 1], segments[i].P3);
        }

        // Касательные в стыках сонаправлены: излома нет.
        for (int i = 0; i + 1 < segments.Count; i++)
        {
            Pt incoming = segments[i].P3 - segments[i].P2;
            Pt outgoing = segments[i + 1].P1 - segments[i + 1].P0;
            double cross = incoming.X * outgoing.Y - incoming.Y * outgoing.X;
            double dot = incoming.X * outgoing.X + incoming.Y * outgoing.Y;
            Assert.True(Math.Abs(cross) < 1e-9 * incoming.Length * outgoing.Length + 1e-12);
            Assert.True(dot > 0);
        }

        // Кривая близка к окружности радиуса 40 (крайние участки не проверяем: там касательная задаётся хордой).
        foreach (CubicBezier s in segments.Skip(1).Take(segments.Count - 2))
        {
            for (int i = 0; i <= 10; i++)
            {
                Close.Equal(40, Pt.Distance(s.Eval(i / 10.0), new Pt(50, 50)), 0.3);
            }
        }
    }

    [Fact]
    public void Repeated_points_are_dropped_before_smoothing()
    {
        var points = new[] { new Pt(0, 0), new Pt(0, 0), new Pt(1, 1), new Pt(1, 1), new Pt(2, 0) };

        List<CubicBezier> segments = Interpolation.Smooth(points);

        Assert.Equal(2, segments.Count);
    }

    [Fact]
    public void Two_points_give_a_straight_segment()
    {
        CubicBezier segment = Assert.Single(Interpolation.Smooth(new[] { new Pt(0, 0), new Pt(30, 60) }));

        Close.Equal(new Pt(15, 30), segment.Eval(0.5));
    }
}

public class DecimatorTests
{
    [Fact]
    public void Collinear_points_collapse_to_the_end_points()
    {
        List<Pt> points = Enumerable.Range(0, 1000).Select(i => new Pt(i * 0.1, i * 0.05)).ToList();

        List<Pt> result = Decimator.Simplify(points, 0.01);

        Assert.Equal(new[] { points[0], points[999] }, result);
    }

    [Fact]
    public void Simplified_line_stays_within_tolerance_of_every_source_point()
    {
        const double tolerance = 0.02;
        List<Pt> points = Enumerable.Range(0, 20000)
            .Select(i => new Pt(i * 100.0 / 19999, 50 + 45 * Math.Sin(i * 4 * Math.PI / 19999))).ToList();

        List<int> kept = Decimator.SimplifyIndices(points, tolerance);

        Assert.True(kept.Count < 1500, $"Осталось точек: {kept.Count}");
        Assert.Equal(0, kept[0]);
        Assert.Equal(points.Count - 1, kept[kept.Count - 1]);

        for (int k = 0; k + 1 < kept.Count; k++)
        {
            Pt a = points[kept[k]], b = points[kept[k + 1]];
            for (int i = kept[k]; i <= kept[k + 1]; i++)
            {
                Assert.True(Pt.DistanceToSegment(points[i], a, b) <= tolerance + 1e-12);
            }
        }
    }

    [Fact]
    public void Sharp_peak_is_preserved()
    {
        List<Pt> points = Enumerable.Range(0, 2001).Select(i => new Pt(i * 0.05, i == 1000 ? 30 : 0)).ToList();

        List<Pt> result = Decimator.Simplify(points, 0.02);

        Assert.Contains(points[1000], result);
        Assert.True(result.Count <= 5);
    }

    [Fact]
    public void Zero_tolerance_keeps_everything()
    {
        List<Pt> points = Enumerable.Range(0, 50).Select(i => new Pt(i, i % 3)).ToList();

        Assert.Equal(points, Decimator.Simplify(points, 0));
    }
}
