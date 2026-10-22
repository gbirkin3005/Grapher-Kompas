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
    Legend,
    /// <summary>Линия-выноска с полкой у подписи кривой.</summary>
    Leader,
    /// <summary>Подпись кривой на поле диаграммы.</summary>
    CurveLabel,
    /// <summary>Единица измерения у шкалы.</summary>
    Unit,
    /// <summary>Пояснительная надпись (масштаб шкалы).</summary>
    Note,
    /// <summary>Штриховая линия разрыва функции.</summary>
    Jump
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

/// <summary>Окружность (маркеры точек, линии полярной сетки); может быть залита.</summary>
public sealed class CirclePrimitive : Primitive
{
    public Pt Center { get; set; }
    public double Radius { get; set; }
    public LineStyleId Style { get; set; }

    /// <summary>Залить круг (маркер «точка»).</summary>
    public bool Filled { get; set; }

    public override Primitive Transform(Transform2D t) =>
        new CirclePrimitive { Role = Role, Color = Color, Center = t.Apply(Center), Radius = Radius * t.Scale, Style = Style, Filled = Filled };

    public override void CollectBoundsPoints(List<Pt> points)
    {
        points.Add(new Pt(Center.X - Radius, Center.Y - Radius));
        points.Add(new Pt(Center.X + Radius, Center.Y + Radius));
    }
}

/// <summary>Дуга окружности: от начального угла против часовой стрелки на заданный угол.</summary>
public sealed class ArcPrimitive : Primitive
{
    public Pt Center { get; set; }
    public double Radius { get; set; }

    /// <summary>Начальный угол, градусы от оси X против часовой стрелки.</summary>
    public double StartDeg { get; set; }

    /// <summary>Угловая длина дуги, градусы (больше нуля, меньше 360).</summary>
    public double SweepDeg { get; set; }

    public LineStyleId Style { get; set; }

    public Pt PointAt(double angleDeg)
    {
        double a = angleDeg * System.Math.PI / 180.0;
        return new Pt(Center.X + Radius * System.Math.Cos(a), Center.Y + Radius * System.Math.Sin(a));
    }

    public Pt Start => PointAt(StartDeg);
    public Pt End => PointAt(StartDeg + SweepDeg);

    public override Primitive Transform(Transform2D t) => new ArcPrimitive
    {
        Role = Role,
        Color = Color,
        Center = t.Apply(Center),
        Radius = Radius * t.Scale,
        StartDeg = StartDeg + t.AngleDeg,
        SweepDeg = SweepDeg,
        Style = Style
    };

    public override void CollectBoundsPoints(List<Pt> points)
    {
        int steps = System.Math.Max(2, (int)System.Math.Ceiling(SweepDeg / 15));
        for (int i = 0; i <= steps; i++)
        {
            points.Add(PointAt(StartDeg + SweepDeg * i / steps));
        }
    }
}

/// <summary>
/// Объект «Точка» КОМПАС. Вид задаёт номер стиля точки (1 — точка, 2 — крестик, 3 — x-точка, 4 — квадрат,
/// 5 — треугольник, 6 — окружность, 7 — звезда, 8 — перечёркнутый квадрат, 9 — утолщённый крестик),
/// размер на листе — сам КОМПАС.
/// </summary>
public sealed class PointPrimitive : Primitive
{
    /// <summary>Размер значка точки на листе, мм (измерено на КОМПАС-3D v25).</summary>
    public const double SymbolSizeMm = 3.13;

    /// <summary>Диаметр точки стиля «точка», мм.</summary>
    public const double DotSizeMm = 1.6;

    /// <summary>Размер утолщённого крестика, мм.</summary>
    public const double BoldCrossSizeMm = 3.56;

    public Pt Position { get; set; }
    public int Style { get; set; } = 1;

    /// <summary>Размер значка на листе, мм.</summary>
    public double SizeMm => Style == 1 ? DotSizeMm : Style == 9 ? BoldCrossSizeMm : SymbolSizeMm;

    public override Primitive Transform(Transform2D t) => new PointPrimitive { Role = Role, Color = Color, Position = t.Apply(Position), Style = Style };

    public override void CollectBoundsPoints(List<Pt> points)
    {
        double r = SizeMm / 2;
        points.Add(new Pt(Position.X - r, Position.Y - r));
        points.Add(new Pt(Position.X + r, Position.Y + r));
    }
}

/// <summary>
/// Стрелка — объект «Линия-выноска» КОМПАС без полки и текста: тонкая линия от <see cref="Tail"/>
/// до <see cref="Tip"/> со стрелкой на конце. Размер стрелки задаёт КОМПАС.
/// </summary>
public sealed class LeaderArrowPrimitive : Primitive
{
    /// <summary>Длина стрелки линии-выноски на листе, мм (измерено на КОМПАС-3D v25).</summary>
    public const double HeadLengthMm = 5.0;

    /// <summary>Ширина стрелки у основания, мм.</summary>
    public const double HeadWidthMm = 1.4;

    /// <summary>Начало линии (точка полки выноски).</summary>
    public Pt Tail { get; set; }

    /// <summary>Остриё стрелки.</summary>
    public Pt Tip { get; set; }

    public override Primitive Transform(Transform2D t) => new LeaderArrowPrimitive { Role = Role, Color = Color, Tail = t.Apply(Tail), Tip = t.Apply(Tip) };

    public override void CollectBoundsPoints(List<Pt> points)
    {
        points.Add(Tail);
        points.Add(Tip);
    }
}

/// <summary>
/// Однострочный текст. Точка привязки лежит на базовой линии; положение по горизонтали задаёт <see cref="Align"/>.
/// В тексте допустимы индексы в записи <see cref="TextMarkup"/> («V_q», «м^2»).
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
