using System.Collections.Generic;
using Grapher.Core.Geometry;
using Grapher.Core.Model;

namespace Grapher.Core.Scene;

/// <summary>Назначение примитива в составе графика.</summary>
public enum PrimitiveRole
{
    Grid,
    Frame,
    Axis,
    Arrow,
    Tick,
    TickLabel,
    AxisTitle,
    Curve,
    Marker,
    Legend
}

public enum TextAlign
{
    Left,
    Center,
    Right
}

/// <summary>
/// Абстрактный примитив чертежа. Координаты — миллиметры на листе.
/// Рендереры (КОМПАС и предпросмотр) получают один и тот же список примитивов.
/// </summary>
public abstract class Primitive
{
    public PrimitiveRole Role { get; set; }

    /// <summary>
    /// Свой цвет линии (0xRRGGBB); null — цвет стиля линии КОМПАС.
    /// Для текста не используется.
    /// </summary>
    public int? Color { get; set; }

    /// <summary>Возвращает копию примитива после размещения на листе.</summary>
    public abstract Primitive Transform(Transform2D transform);

    /// <summary>Добавляет характерные точки примитива для расчёта габаритов.</summary>
    public abstract void CollectBoundsPoints(List<Pt> points);
}

/// <summary>Отрезок.</summary>
public sealed class LinePrimitive : Primitive
{
    public Pt A { get; set; }
    public Pt B { get; set; }
    public LineStyleId Style { get; set; }

    public override Primitive Transform(Transform2D t) =>
        new LinePrimitive { Role = Role, Color = Color, A = t.Apply(A), B = t.Apply(B), Style = Style };

    public override void CollectBoundsPoints(List<Pt> points)
    {
        points.Add(A);
        points.Add(B);
    }
}

/// <summary>Ломаная; замкнутая ломаная может быть залита (стрелки осей).</summary>
public sealed class PolylinePrimitive : Primitive
{
    public List<Pt> Points { get; set; } = new List<Pt>();
    public bool Closed { get; set; }
    public bool Filled { get; set; }
    public LineStyleId Style { get; set; }

    public override Primitive Transform(Transform2D t)
    {
        var copy = new PolylinePrimitive { Role = Role, Color = Color, Closed = Closed, Filled = Filled, Style = Style };
        copy.Points.Capacity = Points.Count;
        foreach (Pt p in Points) copy.Points.Add(t.Apply(p));
        return copy;
    }

    public override void CollectBoundsPoints(List<Pt> points) => points.AddRange(Points);
}

/// <summary>Узел кривой Безье: точка на кривой и два управляющих «уса».</summary>
public readonly struct BezierNode
{
    public readonly Pt Point;
    /// <summary>Управляющая точка перед узлом.</summary>
    public readonly Pt In;
    /// <summary>Управляющая точка после узла.</summary>
    public readonly Pt Out;

    public BezierNode(Pt point, Pt @in, Pt @out)
    {
        Point = point;
        In = @in;
        Out = @out;
    }
}

/// <summary>Сплайн: цепочка кубических сегментов Безье, заданная узлами.</summary>
public sealed class BezierPrimitive : Primitive
{
    public List<BezierNode> Nodes { get; set; } = new List<BezierNode>();
    public LineStyleId Style { get; set; }

    public static BezierPrimitive FromSegments(IReadOnlyList<CubicBezier> segments, LineStyleId style, PrimitiveRole role)
    {
        var primitive = new BezierPrimitive { Style = style, Role = role };
        int n = segments.Count;
        if (n == 0) return primitive;

        primitive.Nodes.Capacity = n + 1;
        primitive.Nodes.Add(new BezierNode(segments[0].P0, segments[0].P0, segments[0].P1));
        for (int i = 1; i < n; i++)
        {
            primitive.Nodes.Add(new BezierNode(segments[i].P0, segments[i - 1].P2, segments[i].P1));
        }
        primitive.Nodes.Add(new BezierNode(segments[n - 1].P3, segments[n - 1].P2, segments[n - 1].P3));
        return primitive;
    }

    /// <summary>Сегменты Безье между соседними узлами.</summary>
    public List<CubicBezier> ToSegments()
    {
        var segments = new List<CubicBezier>(System.Math.Max(0, Nodes.Count - 1));
        for (int i = 0; i + 1 < Nodes.Count; i++)
        {
            segments.Add(new CubicBezier(Nodes[i].Point, Nodes[i].Out, Nodes[i + 1].In, Nodes[i + 1].Point));
        }
        return segments;
    }

    public override Primitive Transform(Transform2D t)
    {
        var copy = new BezierPrimitive { Role = Role, Color = Color, Style = Style };
        copy.Nodes.Capacity = Nodes.Count;
        foreach (BezierNode node in Nodes)
        {
            copy.Nodes.Add(new BezierNode(t.Apply(node.Point), t.Apply(node.In), t.Apply(node.Out)));
        }
        return copy;
    }

    public override void CollectBoundsPoints(List<Pt> points)
    {
        foreach (BezierNode node in Nodes)
        {
            points.Add(node.Point);
            points.Add(node.In);
            points.Add(node.Out);
        }
    }
}

/// <summary>Окружность (маркеры точек).</summary>
public sealed class CirclePrimitive : Primitive
{
    public Pt Center { get; set; }
    public double Radius { get; set; }
    public LineStyleId Style { get; set; }

    public override Primitive Transform(Transform2D t) =>
        new CirclePrimitive { Role = Role, Color = Color, Center = t.Apply(Center), Radius = Radius * t.Scale, Style = Style };

    public override void CollectBoundsPoints(List<Pt> points)
    {
        points.Add(new Pt(Center.X - Radius, Center.Y - Radius));
        points.Add(new Pt(Center.X + Radius, Center.Y + Radius));
    }
}

/// <summary>
/// Однострочный текст. Точка привязки лежит на базовой линии; положение по горизонтали задаёт <see cref="Align"/>.
/// Высота — высота прописных букв по ГОСТ 2.304 в мм на листе; масштаб вида на неё не влияет.
/// </summary>
public sealed class TextPrimitive : Primitive
{
    public Pt Anchor { get; set; }
    public string Text { get; set; } = "";
    public double HeightMm { get; set; }
    public double AngleDeg { get; set; }
    public TextAlign Align { get; set; }
    public string FontName { get; set; }
    public bool Italic { get; set; }

    public override Primitive Transform(Transform2D t) => new TextPrimitive
    {
        Role = Role,
        Anchor = t.Apply(Anchor),
        Text = Text,
        HeightMm = HeightMm,
        AngleDeg = AngleDeg + t.AngleDeg,
        Align = Align,
        FontName = FontName,
        Italic = Italic
    };

    public override void CollectBoundsPoints(List<Pt> points)
    {
        double width = TextMetrics.EstimateWidth(Text, HeightMm);
        double left = Align == TextAlign.Left ? 0 : Align == TextAlign.Center ? -width / 2 : -width;
        var box = new Transform2D(Anchor.X, Anchor.Y, AngleDeg);
        points.Add(box.Apply(new Pt(left, -0.3 * HeightMm)));
        points.Add(box.Apply(new Pt(left + width, -0.3 * HeightMm)));
        points.Add(box.Apply(new Pt(left + width, 1.1 * HeightMm)));
        points.Add(box.Apply(new Pt(left, 1.1 * HeightMm)));
    }
}
