using System;

namespace Grapher.Core.Geometry;

/// <summary>
/// Преобразование подобия: масштаб, поворот вокруг начала координат, затем перенос.
/// Используется для размещения графика на листе (точка вставки, угол, масштаб вида).
/// </summary>
public readonly struct Transform2D
{
    public readonly double Dx;
    public readonly double Dy;
    public readonly double AngleDeg;
    public readonly double Scale;
    private readonly double _cos;
    private readonly double _sin;

    public Transform2D(double dx, double dy, double angleDeg, double scale = 1.0)
    {
        Dx = dx;
        Dy = dy;
        AngleDeg = angleDeg;
        Scale = scale;
        double rad = angleDeg * Math.PI / 180.0;
        _cos = Math.Cos(rad);
        _sin = Math.Sin(rad);
    }

    public static Transform2D Identity => new Transform2D(0, 0, 0, 1);

    public bool IsIdentity => Dx == 0 && Dy == 0 && AngleDeg == 0 && Scale == 1;

    public Pt Apply(Pt p) =>
        new Pt(Dx + (p.X * _cos - p.Y * _sin) * Scale, Dy + (p.X * _sin + p.Y * _cos) * Scale);
}
