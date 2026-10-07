using System;

namespace Grapher.Core.Geometry;

/// <summary>Прямоугольник со сторонами, параллельными осям.</summary>
public readonly struct RectD
{
    public readonly double XMin;
    public readonly double YMin;
    public readonly double XMax;
    public readonly double YMax;

    public RectD(double xMin, double yMin, double xMax, double yMax)
    {
        XMin = Math.Min(xMin, xMax);
        YMin = Math.Min(yMin, yMax);
        XMax = Math.Max(xMin, xMax);
        YMax = Math.Max(yMin, yMax);
    }

    public double Width => XMax - XMin;
    public double Height => YMax - YMin;
    public Pt Center => new Pt((XMin + XMax) / 2, (YMin + YMax) / 2);

    public bool Contains(Pt p, double eps = 0) =>
        p.X >= XMin - eps && p.X <= XMax + eps && p.Y >= YMin - eps && p.Y <= YMax + eps;

    public RectD Inflate(double d) => new RectD(XMin - d, YMin - d, XMax + d, YMax + d);

    public RectD Union(Pt p) =>
        new RectD(Math.Min(XMin, p.X), Math.Min(YMin, p.Y), Math.Max(XMax, p.X), Math.Max(YMax, p.Y));

    public RectD Union(RectD r) =>
        new RectD(Math.Min(XMin, r.XMin), Math.Min(YMin, r.YMin), Math.Max(XMax, r.XMax), Math.Max(YMax, r.YMax));
}
