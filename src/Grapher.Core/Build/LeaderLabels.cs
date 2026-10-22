using System;
using System.Collections.Generic;
using Grapher.Core.Geometry;
using Grapher.Core.Model;
using Grapher.Core.Scene;

namespace Grapher.Core.Build;

/// <summary>
/// Подписи кривых на полках линий-выносок. Место подписи подбирается так, чтобы надпись
/// осталась на поле диаграммы и легла как можно дальше от линий графика.
/// </summary>
internal static class LeaderLabels
{
    /// <summary>Вынос линии-выноски от кривой по горизонтали и вертикали, мм.</summary>
    private const double LeaderDx = 5, LeaderDy = 8;

    private static readonly double[] Positions = { 0.5, 0.35, 0.65, 0.22, 0.78, 0.1, 0.9 };

    private static readonly LabelDirection[] Directions =
    {
        LabelDirection.UpRight, LabelDirection.UpLeft, LabelDirection.DownRight, LabelDirection.DownLeft
    };

    /// <param name="inside">Лежит ли прямоугольник надписи на поле диаграммы.</param>
    /// <returns>Прямоугольники надписей: под ними прерывается сетка.</returns>
    public static List<RectD> Place(BuildContext c, IReadOnlyList<BuiltCurve> curves, Func<RectD, bool> inside)
    {
        GraphSettings g = c.G;
        var boxes = new List<RectD>();
        double height = g.TitleHeightMm;
        if (!(height > 0)) return boxes;

        var labeled = new List<BuiltCurve>();
        foreach (BuiltCurve curve in curves)
        {
            if (curve.Settings.Label == CurveLabelMode.Leader && curve.Pieces.Count > 0) labeled.Add(curve);
        }

        for (int index = 0; index < labeled.Count; index++)
        {
            BuiltCurve curve = labeled[index];
            string text = string.IsNullOrWhiteSpace(curve.Settings.LabelText) ? curve.Settings.Name : curve.Settings.LabelText;
            text = (text ?? "").Trim();
            if (text.Length == 0) continue;

            // Подпись ставится на самом длинном видимом участке кривой.
            List<Pt> piece = curve.Pieces[0];
            foreach (List<Pt> candidate in curve.Pieces)
            {
                if (CurveTools.Length(candidate) > CurveTools.Length(piece)) piece = candidate;
            }
            if (piece.Count < 2) continue;

            double width = TextMetrics.EstimateWidth(text, height);
            Candidate best = null;

            IEnumerable<double> positions = curve.Settings.LabelPosition >= 0
                ? new[] { Math.Min(100, curve.Settings.LabelPosition) / 100.0 }
                : SpreadPositions(index, labeled.Count);
            IEnumerable<LabelDirection> directions = curve.Settings.LabelDirection != LabelDirection.Auto
                ? new[] { curve.Settings.LabelDirection }
                : Directions;

            foreach (double position in positions)
            {
                Pt anchor = CurveTools.PointAt(piece, position, out _);
                foreach (LabelDirection direction in directions)
                {
                    Candidate candidate = Build(anchor, direction, text, width, height);
                    candidate.Score = Score(candidate, curves, boxes, inside);
                    if (best == null || candidate.Score > best.Score) best = candidate;
                }
            }
            if (best == null) continue;

            c.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Leader, A = best.Anchor, B = best.Elbow, Style = LineStyleId.Thin });
            c.Scene.Add(new LinePrimitive { Role = PrimitiveRole.Leader, A = best.Elbow, B = best.ShelfEnd, Style = LineStyleId.Thin });
            c.Scene.Add(SceneTools.Text(g, PrimitiveRole.CurveLabel, text, best.TextAnchor, height, TextAlign.Left));
            boxes.Add(best.Box);
        }
        return boxes;
    }

    /// <summary>Положения вдоль кривой: сначала своё для каждой кривой (чтобы подписи не сбивались в кучу), затем запасные.</summary>
    private static IEnumerable<double> SpreadPositions(int index, int count)
    {
        yield return (index + 1.0) / (count + 1.0);
        foreach (double position in Positions)
        {
            yield return position;
        }
    }

    private sealed class Candidate
    {
        public Pt Anchor, Elbow, ShelfEnd, TextAnchor;
        public RectD Box;
        public double Score;
    }

    private static Candidate Build(Pt anchor, LabelDirection direction, string text, double width, double height)
    {
        double sx = direction == LabelDirection.UpRight || direction == LabelDirection.DownRight ? 1 : -1;
        double sy = direction == LabelDirection.UpRight || direction == LabelDirection.UpLeft ? 1 : -1;

        var elbow = new Pt(anchor.X + sx * LeaderDx, anchor.Y + sy * LeaderDy);
        double shelf = width + 2 * SceneTools.GapMm;
        var shelfEnd = new Pt(elbow.X + sx * shelf, elbow.Y);
        double left = Math.Min(elbow.X, shelfEnd.X);

        // Текст стоит над полкой. Просвет выбран так, чтобы текст (и его нижние индексы) не стёр полку
        // на чертеже: в КОМПАС надпись очищает линии под собой.
        RectD probe = TextMetrics.ClearedBox(text, height, new Pt(0, 0), TextAlign.Left);
        double lift = -probe.YMin + 0.3;
        var textAnchor = new Pt(left + SceneTools.GapMm, elbow.Y + lift);
        RectD textBox = TextMetrics.ClearedBox(text, height, textAnchor, TextAlign.Left);
        RectD box = new RectD(left, elbow.Y - 0.3, Math.Max(left + shelf, textBox.XMax), textBox.YMax);

        return new Candidate { Anchor = anchor, Elbow = elbow, ShelfEnd = shelfEnd, TextAnchor = textAnchor, Box = box };
    }

    private static double Score(Candidate candidate, IReadOnlyList<BuiltCurve> curves, List<RectD> placed, Func<RectD, bool> inside)
    {
        double score = 0;
        if (!inside(candidate.Box)) score -= 1000;

        foreach (RectD other in placed)
        {
            if (Overlap(candidate.Box, other)) score -= 500;
        }

        // Расстояние от надписи до ближайшей линии графика: чем больше, тем лучше (но не бесконечно).
        double clearance = double.MaxValue;
        foreach (BuiltCurve curve in curves)
        {
            foreach (List<Pt> piece in curve.Pieces)
            {
                for (int i = 0; i + 1 < piece.Count; i++)
                {
                    double d = DistanceSegmentToBox(piece[i], piece[i + 1], candidate.Box);
                    if (d < clearance) clearance = d;
                    if (clearance <= 0) break;
                }
            }
        }
        if (clearance == double.MaxValue) clearance = 20;
        score += clearance <= 0 ? -200 : Math.Min(clearance, 20);
        return score;
    }

    private static bool Overlap(RectD a, RectD b) =>
        a.XMin < b.XMax && b.XMin < a.XMax && a.YMin < b.YMax && b.YMin < a.YMax;

    /// <summary>Расстояние от отрезка до прямоугольника; 0, если отрезок его задевает.</summary>
    private static double DistanceSegmentToBox(Pt a, Pt b, RectD box)
    {
        if (Numerics.Clipper.ClipSegment(box, a, b, out _, out _)) return 0;

        double best = double.MaxValue;
        var corners = new[]
        {
            new Pt(box.XMin, box.YMin), new Pt(box.XMax, box.YMin), new Pt(box.XMax, box.YMax), new Pt(box.XMin, box.YMax)
        };
        for (int i = 0; i < 4; i++)
        {
            best = Math.Min(best, Pt.DistanceToSegment(corners[i], a, b));
            // Концы отрезка — до сторон прямоугольника.
            best = Math.Min(best, Pt.DistanceToSegment(a, corners[i], corners[(i + 1) % 4]));
            best = Math.Min(best, Pt.DistanceToSegment(b, corners[i], corners[(i + 1) % 4]));
        }
        return best;
    }
}
