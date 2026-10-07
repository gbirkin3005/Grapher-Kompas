using System;

namespace Grapher.Core.Geometry;

/// <summary>Кубический сегмент Безье: P0 и P3 — концы, P1 и P2 — управляющие точки.</summary>
public readonly struct CubicBezier
{
    public readonly Pt P0;
    public readonly Pt P1;
    public readonly Pt P2;
    public readonly Pt P3;

    public CubicBezier(Pt p0, Pt p1, Pt p2, Pt p3)
    {
        P0 = p0;
        P1 = p1;
        P2 = p2;
        P3 = p3;
    }

    /// <summary>Сегмент Безье, совпадающий с отрезком [a, b].</summary>
    public static CubicBezier FromLine(Pt a, Pt b) =>
        new CubicBezier(a, Pt.Lerp(a, b, 1.0 / 3.0), Pt.Lerp(a, b, 2.0 / 3.0), b);

    public Pt Eval(double t)
    {
        double u = 1 - t;
        double b0 = u * u * u, b1 = 3 * u * u * t, b2 = 3 * u * t * t, b3 = t * t * t;
        return new Pt(
            b0 * P0.X + b1 * P1.X + b2 * P2.X + b3 * P3.X,
            b0 * P0.Y + b1 * P1.Y + b2 * P2.Y + b3 * P3.Y);
    }

    /// <summary>Делит сегмент в точке t (алгоритм де Кастельжо).</summary>
    public void Split(double t, out CubicBezier left, out CubicBezier right)
    {
        Pt p01 = Pt.Lerp(P0, P1, t), p12 = Pt.Lerp(P1, P2, t), p23 = Pt.Lerp(P2, P3, t);
        Pt p012 = Pt.Lerp(p01, p12, t), p123 = Pt.Lerp(p12, p23, t);
        Pt mid = Pt.Lerp(p012, p123, t);
        left = new CubicBezier(P0, p01, p012, mid);
        right = new CubicBezier(mid, p123, p23, P3);
    }

    /// <summary>Часть сегмента между параметрами t0 и t1.</summary>
    public CubicBezier Sub(double t0, double t1)
    {
        if (t0 <= 0 && t1 >= 1) return this;
        CubicBezier head = this;
        if (t1 < 1)
        {
            Split(t1, out head, out _);
        }
        if (t0 <= 0) return head;

        // После отсечения хвоста параметр t0 пересчитывается на укороченный сегмент.
        double local = t1 > 0 ? t0 / Math.Min(t1, 1.0) : 0;
        CubicBezier source = head;
        source.Split(local, out _, out CubicBezier tail);
        return tail;
    }

    public CubicBezier Reversed() => new CubicBezier(P3, P2, P1, P0);

    public CubicBezier Transform(Transform2D t) =>
        new CubicBezier(t.Apply(P0), t.Apply(P1), t.Apply(P2), t.Apply(P3));
}
