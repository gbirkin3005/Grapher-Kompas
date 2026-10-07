using System;
using System.Globalization;

namespace Grapher.Core.Geometry;

/// <summary>Точка или вектор на плоскости (мм либо единицы данных).</summary>
public readonly struct Pt : IEquatable<Pt>
{
    public readonly double X;
    public readonly double Y;

    public Pt(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double Length => Math.Sqrt(X * X + Y * Y);

    public static Pt operator +(Pt a, Pt b) => new Pt(a.X + b.X, a.Y + b.Y);
    public static Pt operator -(Pt a, Pt b) => new Pt(a.X - b.X, a.Y - b.Y);
    public static Pt operator *(Pt a, double k) => new Pt(a.X * k, a.Y * k);
    public static Pt operator *(double k, Pt a) => new Pt(a.X * k, a.Y * k);
    public static Pt operator /(Pt a, double k) => new Pt(a.X / k, a.Y / k);

    public static double Distance(Pt a, Pt b) => (a - b).Length;

    public static Pt Lerp(Pt a, Pt b, double t) => new Pt(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    /// <summary>Расстояние от точки до отрезка [a, b].</summary>
    public static double DistanceToSegment(Pt p, Pt a, Pt b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        if (len2 <= 0) return Distance(p, a);
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2;
        if (t <= 0) return Distance(p, a);
        if (t >= 1) return Distance(p, b);
        return Distance(p, new Pt(a.X + t * dx, a.Y + t * dy));
    }

    public bool Equals(Pt other) => X == other.X && Y == other.Y;
    public override bool Equals(object obj) => obj is Pt p && Equals(p);
    public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
    public static bool operator ==(Pt a, Pt b) => a.Equals(b);
    public static bool operator !=(Pt a, Pt b) => !a.Equals(b);

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "({0:0.######}; {1:0.######})", X, Y);
}
