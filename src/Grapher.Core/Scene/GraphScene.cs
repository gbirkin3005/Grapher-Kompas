using System.Collections.Generic;
using Grapher.Core.Geometry;

namespace Grapher.Core.Scene;

/// <summary>
/// Готовый график в виде списка примитивов. Начало координат — левый нижний угол поля графика,
/// ось X направлена вправо, ось Y — вверх, единицы — миллиметры на листе.
/// </summary>
public sealed class GraphScene
{
    public List<Primitive> Primitives { get; } = new List<Primitive>();

    /// <summary>Поле графика (рамка), по которому обрезаются кривые.</summary>
    public RectD PlotRect { get; set; }

    /// <summary>Четыре угла поля графика после размещения (для поворота это не прямоугольник по осям).</summary>
    public Pt[] PlotCorners { get; set; }

    public void Add(Primitive primitive) => Primitives.Add(primitive);

    /// <summary>
    /// Размещает сцену на листе: масштаб (для видов с масштабом, отличным от 1:1), поворот, перенос.
    /// Высота текста не масштабируется: в КОМПАС она задаётся в миллиметрах на бумаге.
    /// </summary>
    public GraphScene Transform(Transform2D transform)
    {
        var scene = new GraphScene { PlotRect = PlotRect };
        scene.Primitives.Capacity = Primitives.Count;
        foreach (Primitive primitive in Primitives)
        {
            scene.Primitives.Add(primitive.Transform(transform));
        }

        Pt[] corners = PlotCorners ?? new[]
        {
            new Pt(PlotRect.XMin, PlotRect.YMin),
            new Pt(PlotRect.XMax, PlotRect.YMin),
            new Pt(PlotRect.XMax, PlotRect.YMax),
            new Pt(PlotRect.XMin, PlotRect.YMax)
        };
        scene.PlotCorners = new Pt[corners.Length];
        for (int i = 0; i < corners.Length; i++)
        {
            scene.PlotCorners[i] = transform.Apply(corners[i]);
        }
        return scene;
    }

    /// <summary>Габариты всех примитивов. Для текста используется приближённая ширина.</summary>
    public RectD GetBounds()
    {
        var points = new List<Pt>();
        foreach (Primitive primitive in Primitives)
        {
            primitive.CollectBoundsPoints(points);
        }

        if (points.Count == 0) return PlotRect;

        double xMin = double.MaxValue, yMin = double.MaxValue, xMax = double.MinValue, yMax = double.MinValue;
        foreach (Pt p in points)
        {
            if (p.X < xMin) xMin = p.X;
            if (p.X > xMax) xMax = p.X;
            if (p.Y < yMin) yMin = p.Y;
            if (p.Y > yMax) yMax = p.Y;
        }
        return new RectD(xMin, yMin, xMax, yMax);
    }
}
