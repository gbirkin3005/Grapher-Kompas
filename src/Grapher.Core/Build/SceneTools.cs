using System;
using System.Collections.Generic;
using Grapher.Core.Geometry;
using Grapher.Core.Model;
using Grapher.Core.Numerics;
using Grapher.Core.Scene;

namespace Grapher.Core.Build;

/// <summary>Общие для всех видов диаграмм элементы оформления: тексты, стрелки, маркеры, дроби, разрывы сетки.</summary>
internal static class SceneTools
{
    /// <summary>Просвет между элементами оформления (засечка — подпись и т. п.), мм.</summary>
    public const double GapMm = 1.0;

    /// <summary>Тангенс половины угла раствора стрелки (угол стрелки 20°, как у размерных стрелок).</summary>
    public const double ArrowHalfWidthRatio = 0.176;

    public static TextPrimitive Text(GraphSettings g, PrimitiveRole role, string text, Pt anchor, double height,
        TextAlign align, double angleDeg = 0) => new TextPrimitive
    {
        Role = role,
        Anchor = anchor,
        Text = text,
        HeightMm = height,
        Align = align,
        AngleDeg = angleDeg,
        FontName = DrawingFonts.ForText(g.FontName, TextMarkup.Plain(text)),
        Italic = g.Italic
    };

    // ------------------------------------------------------------------ стрелки

    /// <summary>
    /// Линия со стрелкой на конце. Линия доходит до середины стрелки: остриё остаётся тонким.
    /// </summary>
    /// <param name="native">
    /// Стрелка — объект «Линия-выноска» КОМПАС. Тонкая стрелка целиком строится выноской; у основной линии
    /// (ось) выноска даёт только наконечник, а сама линия остаётся отрезком основной линии.
    /// </param>
    public static void Arrow(GraphScene scene, Pt start, Pt end, double arrowLength, LineStyleId style, PrimitiveRole role, bool native)
    {
        double length = Pt.Distance(start, end);
        if (!(length > 0)) return;

        Pt dir = (end - start) / length;
        if (native && arrowLength > 0)
        {
            double head = LeaderArrowPrimitive.HeadLengthMm;
            if (style == LineStyleId.Thin || length <= 1.5 * head)
            {
                scene.Add(new LeaderArrowPrimitive { Role = role, Tail = start, Tip = end });
                return;
            }
            scene.Add(new LinePrimitive { Role = role, A = start, B = end - dir * (head * 0.5), Style = style });
            scene.Add(new LeaderArrowPrimitive { Role = PrimitiveRole.Arrow, Tail = end - dir * (1.5 * head), Tip = end });
            return;
        }

        if (arrowLength <= 0 || arrowLength >= length)
        {
            scene.Add(new LinePrimitive { Role = role, A = start, B = end, Style = style });
            return;
        }

        scene.Add(new LinePrimitive { Role = role, A = start, B = end - dir * (arrowLength * 0.5), Style = style });
        ArrowHead(scene, end, dir, arrowLength);
    }

    /// <summary>Залитый наконечник стрелки с остриём в точке <paramref name="tip"/>.</summary>
    public static void ArrowHead(GraphScene scene, Pt tip, Pt dir, double length)
    {
        var normal = new Pt(-dir.Y, dir.X);
        Pt back = tip - dir * length;
        double half = length * ArrowHalfWidthRatio;
        scene.Add(new PolylinePrimitive
        {
            Role = PrimitiveRole.Arrow,
            Points = new List<Pt> { tip, back + normal * half, back - normal * half },
            Closed = true,
            Filled = true,
            Style = LineStyleId.Thin
        });
    }

    // ------------------------------------------------------------------ маркеры

    public static void Marker(GraphScene scene, MarkerShape shape, Pt center, double size, PrimitiveRole role, int? color)
    {
        double r = size / 2;
        switch (shape)
        {
            case MarkerShape.Circle:
                scene.Add(new CirclePrimitive { Role = role, Color = color, Center = center, Radius = r, Style = LineStyleId.Thin });
                break;

            case MarkerShape.Dot:
                scene.Add(new CirclePrimitive { Role = role, Color = color, Center = center, Radius = r, Style = LineStyleId.Thin, Filled = true });
                break;

            case MarkerShape.Square:
                scene.Add(ClosedShape(role, color,
                    new Pt(center.X - r, center.Y - r), new Pt(center.X + r, center.Y - r),
                    new Pt(center.X + r, center.Y + r), new Pt(center.X - r, center.Y + r)));
                break;

            case MarkerShape.Triangle:
                double tr = r * 1.2;
                scene.Add(ClosedShape(role, color,
                    new Pt(center.X, center.Y + tr),
                    new Pt(center.X - tr * 0.866, center.Y - tr * 0.5),
                    new Pt(center.X + tr * 0.866, center.Y - tr * 0.5)));
                break;

            case MarkerShape.Diamond:
                double dr = r * 1.2;
                scene.Add(ClosedShape(role, color,
                    new Pt(center.X, center.Y + dr), new Pt(center.X - dr, center.Y),
                    new Pt(center.X, center.Y - dr), new Pt(center.X + dr, center.Y)));
                break;

            case MarkerShape.Cross:
                scene.Add(new LinePrimitive { Role = role, Color = color, A = new Pt(center.X - r, center.Y - r), B = new Pt(center.X + r, center.Y + r), Style = LineStyleId.Thin });
                scene.Add(new LinePrimitive { Role = role, Color = color, A = new Pt(center.X - r, center.Y + r), B = new Pt(center.X + r, center.Y - r), Style = LineStyleId.Thin });
                break;

            default:
                // Объект «Точка» КОМПАС: вид — стилем точки, размер задаёт КОМПАС.
                int style = MarkerShapes.KompasPointStyle(shape);
                if (style > 0) scene.Add(new PointPrimitive { Role = role, Color = color, Position = center, Style = style });
                break;
        }
    }

    private static PolylinePrimitive ClosedShape(PrimitiveRole role, int? color, params Pt[] points) => new PolylinePrimitive
    {
        Role = role,
        Color = color,
        Points = new List<Pt>(points),
        Closed = true,
        Style = LineStyleId.Thin
    };

    // ------------------------------------------------------------------ дроби

    // Дробь — часть текста (штатная вставка «Дробь» КОМПАС): «π» над «6», «мм» над единицей измерения.
    // Здесь только её размеры для расстановки подписей.

    /// <summary>Сколько места строка с дробью занимает над базовой линией.</summary>
    public static double FractionAbove(double height) => (TextMarkup.UpperRise(TextMarkup.FractionScale) + 1.1 * TextMarkup.FractionScale) * height;

    /// <summary>Сколько места строка с дробью занимает под базовой линией.</summary>
    public static double FractionBelow(double height) => (0.3 * TextMarkup.FractionScale - TextMarkup.LowerRise(TextMarkup.FractionScale)) * height;

    /// <summary>Высота черты дроби над базовой линией.</summary>
    public static double FractionBar(double height) => TextMarkup.FractionBarRise * height;

    // ------------------------------------------------------------------ разрывы линий сетки под надписями

    /// <summary>
    /// Прерывает линии сетки под надписями на поле диаграммы (Р 50-77-88, п. 6.2:
    /// «Не допускается пересечение надписей и линий. При недостатке места следует прерывать линию»).
    /// </summary>
    public static void BreakGridUnderBoxes(GraphScene scene, IReadOnlyList<RectD> boxes)
    {
        if (boxes == null || boxes.Count == 0) return;

        var result = new List<Primitive>(scene.Primitives.Count + 8);
        foreach (Primitive primitive in scene.Primitives)
        {
            if (primitive.Role != PrimitiveRole.Grid)
            {
                result.Add(primitive);
                continue;
            }

            switch (primitive)
            {
                case LinePrimitive line:
                    result.AddRange(CutLine(line, boxes));
                    break;
                case CirclePrimitive circle when !circle.Filled:
                    result.AddRange(CutArc(circle.Center, circle.Radius, 0, 360, circle.Style, circle.Role, circle.Color, circle, boxes));
                    break;
                case ArcPrimitive arc:
                    result.AddRange(CutArc(arc.Center, arc.Radius, arc.StartDeg, arc.SweepDeg, arc.Style, arc.Role, arc.Color, arc, boxes));
                    break;
                default:
                    result.Add(primitive);
                    break;
            }
        }

        scene.Primitives.Clear();
        scene.Primitives.AddRange(result);
    }

    /// <summary>Куски короче этого значения после вырезания не строятся, мм.</summary>
    private const double MinPieceMm = 0.3;

    /// <summary>
    /// Кусок линии, оставшийся между двумя надписями, не строится, если он короче этого значения, мм:
    /// короткий обрывок рядом с числами читается как лишний штрих.
    /// </summary>
    private const double MinIslandMm = 3;

    /// <summary>Части отрезка, не попавшие в прямоугольники.</summary>
    public static List<LinePrimitive> CutLine(LinePrimitive line, IReadOnlyList<RectD> boxes)
    {
        // Оставшиеся участки в параметре отрезка.
        var kept = new List<double[]> { new[] { 0.0, 1.0 } };
        foreach (RectD box in boxes)
        {
            if (!Clipper.ClipSegment(box, line.A, line.B, out double t0, out double t1) || t1 - t0 <= 0) continue;

            var next = new List<double[]>(kept.Count + 1);
            foreach (double[] interval in kept)
            {
                if (t1 <= interval[0] || t0 >= interval[1])
                {
                    next.Add(interval);
                    continue;
                }
                if (t0 > interval[0]) next.Add(new[] { interval[0], t0 });
                if (t1 < interval[1]) next.Add(new[] { t1, interval[1] });
            }
            kept = next;
        }

        var pieces = new List<LinePrimitive>(kept.Count);
        if (kept.Count == 1 && kept[0][0] == 0 && kept[0][1] == 1)
        {
            pieces.Add(line);
            return pieces;
        }

        double length = Pt.Distance(line.A, line.B);
        foreach (double[] interval in kept)
        {
            double pieceLength = (interval[1] - interval[0]) * length;
            bool island = interval[0] > 0 && interval[1] < 1;
            if (pieceLength < (island ? MinIslandMm : MinPieceMm)) continue;
            pieces.Add(new LinePrimitive
            {
                Role = line.Role,
                Color = line.Color,
                Style = line.Style,
                A = Pt.Lerp(line.A, line.B, interval[0]),
                B = Pt.Lerp(line.A, line.B, interval[1])
            });
        }
        return pieces;
    }

    /// <summary>Части окружности или дуги, не попавшие в прямоугольники.</summary>
    private static List<Primitive> CutArc(Pt center, double radius, double startDeg, double sweepDeg, LineStyleId style,
        PrimitiveRole role, int? color, Primitive original, IReadOnlyList<RectD> boxes)
    {
        bool fullCircle = sweepDeg >= 360 - 1e-9;
        // Оставшиеся участки: начало и длина в градусах, отсчёт от startDeg.
        var kept = new List<double[]> { new[] { 0.0, sweepDeg } };
        bool cut = false;

        foreach (RectD box in boxes)
        {
            List<double> crossings = CircleBoxCrossings(center, radius, box, startDeg, sweepDeg);
            if (crossings.Count == 0)
            {
                // Пересечений нет: окружность либо целиком вне надписи, либо (для очень маленьких) целиком внутри.
                if (!box.Contains(PointOn(center, radius, startDeg + sweepDeg / 2))) continue;
                return new List<Primitive>();
            }

            crossings.Add(0);
            crossings.Add(sweepDeg);
            crossings.Sort();

            var next = new List<double[]>();
            foreach (double[] interval in kept)
            {
                double from = interval[0], to = interval[0] + interval[1];
                double cursor = from;
                foreach (double crossing in crossings)
                {
                    if (crossing <= cursor) continue;
                    double end = Math.Min(crossing, to);
                    if (end > cursor)
                    {
                        bool inside = box.Contains(PointOn(center, radius, startDeg + (cursor + end) / 2));
                        if (inside) cut = true;
                        else AppendInterval(next, cursor, end - cursor);
                        cursor = end;
                    }
                    if (cursor >= to) break;
                }
            }
            kept = next;
        }

        var result = new List<Primitive>();
        if (!cut)
        {
            result.Add(original);
            return result;
        }

        // У окружности участок может переходить через начало отсчёта: склеиваем первый и последний.
        if (fullCircle && kept.Count > 1 && kept[0][0] <= 1e-9 && Math.Abs(kept[kept.Count - 1][0] + kept[kept.Count - 1][1] - 360) <= 1e-9)
        {
            double[] last = kept[kept.Count - 1];
            kept[0] = new[] { last[0], last[1] + kept[0][1] };
            kept.RemoveAt(kept.Count - 1);
        }

        foreach (double[] interval in kept)
        {
            bool island = fullCircle || (interval[0] > 1e-9 && interval[0] + interval[1] < sweepDeg - 1e-9);
            if (interval[1] * Math.PI / 180 * radius < (island ? MinIslandMm : MinPieceMm)) continue;
            result.Add(new ArcPrimitive
            {
                Role = role,
                Color = color,
                Style = style,
                Center = center,
                Radius = radius,
                StartDeg = startDeg + interval[0],
                SweepDeg = interval[1]
            });
        }
        return result;
    }

    private static void AppendInterval(List<double[]> intervals, double start, double sweep)
    {
        if (intervals.Count > 0)
        {
            double[] last = intervals[intervals.Count - 1];
            if (Math.Abs(last[0] + last[1] - start) <= 1e-9)
            {
                last[1] += sweep;
                return;
            }
        }
        intervals.Add(new[] { start, sweep });
    }

    private static Pt PointOn(Pt center, double radius, double angleDeg)
    {
        double a = angleDeg * Math.PI / 180;
        return new Pt(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a));
    }

    /// <summary>Углы (в градусах от начала дуги), под которыми дуга пересекает стороны прямоугольника.</summary>
    private static List<double> CircleBoxCrossings(Pt center, double radius, RectD box, double startDeg, double sweepDeg)
    {
        var angles = new List<double>();

        void Add(double x, double y)
        {
            double angle = Math.Atan2(y - center.Y, x - center.X) * 180 / Math.PI;
            double relative = (angle - startDeg) % 360;
            if (relative < 0) relative += 360;
            if (relative > 0 && relative < sweepDeg) angles.Add(relative);
        }

        // Вертикальные стороны: x задан, ищем y на окружности.
        foreach (double x in new[] { box.XMin, box.XMax })
        {
            double dx = x - center.X;
            double under = radius * radius - dx * dx;
            if (under <= 0) continue;
            double dy = Math.Sqrt(under);
            foreach (double y in new[] { center.Y - dy, center.Y + dy })
            {
                if (y >= box.YMin && y <= box.YMax) Add(x, y);
            }
        }

        // Горизонтальные стороны.
        foreach (double y in new[] { box.YMin, box.YMax })
        {
            double dy = y - center.Y;
            double under = radius * radius - dy * dy;
            if (under <= 0) continue;
            double dx = Math.Sqrt(under);
            foreach (double x in new[] { center.X - dx, center.X + dx })
            {
                if (x >= box.XMin && x <= box.XMax) Add(x, y);
            }
        }
        return angles;
    }
}
