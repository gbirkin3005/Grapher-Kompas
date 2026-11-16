using System;
using System.Collections.Generic;
using Grapher.Core.Geometry;
using Grapher.Core.Model;
using Grapher.Core.Numerics;
using Grapher.Core.Scene;

namespace Grapher.Core.Build;

/// <summary>
/// Подписи значений у точек кривых: число над каждой N-й точкой (как «1,875» или «0,5»).
/// Подпись ставится над точкой; если она не помещается на поле или наезжает на уже поставленную
/// подпись, она пропускается. Под поставленными подписями прерываются линии сетки.
/// </summary>
internal static class PointLabels
{
    /// <summary>Значащих цифр в числе подписи: 1,875 и 0,3333, без лишних нулей.</summary>
    private const int SignificantDigits = 4;

    /// <summary>
    /// Ставит подписи у точек одной кривой.
    /// </summary>
    /// <param name="source">Исходные точки (значения в единицах таблицы); <paramref name="source"/>[i].Y — подписываемое значение.</param>
    /// <param name="mapped">Те же точки в миллиметрах на листе (соответствуют <paramref name="source"/> по индексу).</param>
    /// <param name="lift">Отступ подписи от точки, мм (радиус маркера, если он ставится в точках таблицы).</param>
    /// <param name="placed">Прямоугольники уже поставленных подписей (общие для всех кривых).</param>
    /// <param name="plot">Границы поля диаграммы: подпись прижимается к ним по горизонтали,
    /// чтобы подписывать и крайние точки у боковых сторон поля.</param>
    /// <param name="inside">Лежит ли прямоугольник подписи на поле диаграммы.</param>
    public static void Place(BuildContext c, CurveSettings curve, IReadOnlyList<Pt> source, IReadOnlyList<Pt> mapped,
        double lift, List<RectD> placed, RectD plot, Func<RectD, bool> inside)
    {
        if (curve.PointLabels == PointLabelMode.None || !(c.G.LabelHeightMm > 0)) return;
        if (source.Count == 0 || source.Count != mapped.Count) return;

        double h = curve.PointLabelHeightMm > 0 ? curve.PointLabelHeightMm : c.G.LabelHeightMm;
        string font = string.IsNullOrWhiteSpace(curve.PointLabelFont) ? c.G.FontName : curve.PointLabelFont;
        int every = Math.Max(1, curve.PointLabelEvery);
        double gap = SceneTools.GapMm;

        for (int i = 0; i < mapped.Count; i += every)
        {
            double value = source[i].Y;
            string text = NumberFormatter.IsExponential(value, curve.PointLabelFormat, curve.PointLabelExpThreshold)
                ? NumberFormatter.Scientific(value, SignificantDigits, c.G.DecimalComma)
                : NumberFormatter.Significant(value, SignificantDigits, c.G.DecimalComma);
            var anchor = new Pt(mapped[i].X, mapped[i].Y + lift + gap + TextMetrics.ClearedBelow(text, h));
            RectD box = TextMetrics.ClearedBox(text, h, anchor, TextAlign.Center);

            // Крайние точки у боковых сторон поля: подпись прижимается к рамке,
            // чтобы не закрывать её (КОМПАС стирает линию под текстом) и не пропускать точку.
            if (box.XMin < plot.XMin)
            {
                anchor = new Pt(plot.XMin - box.XMin + anchor.X, anchor.Y);
                box = TextMetrics.ClearedBox(text, h, anchor, TextAlign.Center);
            }
            else if (box.XMax > plot.XMax)
            {
                anchor = new Pt(anchor.X - (box.XMax - plot.XMax), anchor.Y);
                box = TextMetrics.ClearedBox(text, h, anchor, TextAlign.Center);
            }
            // Точки у верхней/нижней сторон поля (крупный шрифт подписи): прижим по вертикали.
            if (box.YMax > plot.YMax)
            {
                anchor = new Pt(anchor.X, anchor.Y - (box.YMax - plot.YMax));
                box = TextMetrics.ClearedBox(text, h, anchor, TextAlign.Center);
            }
            else if (box.YMin < plot.YMin)
            {
                anchor = new Pt(anchor.X, plot.YMin - box.YMin + anchor.Y);
                box = TextMetrics.ClearedBox(text, h, anchor, TextAlign.Center);
            }

            // Подпись должна целиком остаться на поле и не пересечься с уже поставленными.
            if (!inside(box)) continue;
            bool overlaps = false;
            foreach (RectD other in placed)
            {
                if (Overlap(box, other)) { overlaps = true; break; }
            }
            if (overlaps) continue;

            c.Scene.Add(new TextPrimitive
            {
                Role = PrimitiveRole.PointLabel,
                Anchor = anchor,
                Text = text,
                HeightMm = h,
                Align = TextAlign.Center,
                FontName = DrawingFonts.ForText(font, TextMarkup.Plain(text)),
                Italic = c.G.Italic
            });
            placed.Add(box);
            c.KeepOut.Add(box);
        }
    }

    private static bool Overlap(RectD a, RectD b) =>
        a.XMin < b.XMax && b.XMin < a.XMax && a.YMin < b.YMax && b.YMin < a.YMax;
}
