using System;
using System.Collections.Generic;
using Grapher.Core.Geometry;

namespace Grapher.Core.Scene;

/// <summary>
/// Приближённые размеры текста чертёжным шрифтом ГОСТ 2.304 (тип А).
/// Нужны только для расстановки подписей (отступы, прореживание) и расчёта габаритов;
/// точные размеры определяются шрифтом в КОМПАС и в предпросмотре.
/// </summary>
public static class TextMetrics
{
    /// <summary>Просвет между дробью и соседним текстом с каждой стороны, в долях высоты шрифта.</summary>
    public const double FractionSideGap = 0.15;

    /// <summary>
    /// Ширина строки в мм при высоте прописных букв <paramref name="heightMm"/>.
    /// Индексы и дроби (см. <see cref="TextMarkup"/>) учитываются.
    /// </summary>
    public static double EstimateWidth(string text, double heightMm)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (text.IndexOf('_') < 0 && text.IndexOf('^') < 0 && text.IndexOf('\\') < 0)
        {
            return PlainUnits(text) * heightMm;
        }

        return Units(TextMarkup.Parse(text)) * heightMm;
    }

    /// <summary>Ширина числителя или знаменателя (с индексами) в долях высоты шрифта.</summary>
    private static double PartUnits(string part) =>
        string.IsNullOrEmpty(part) ? 0 : Units(TextMarkup.ParsePart(part));

    private static double Units(List<TextRun> runs)
    {
        double units = 0;
        for (int i = 0; i < runs.Count; i++)
        {
            TextRun run = runs[i];
            switch (run.Kind)
            {
                case TextRunKind.Normal:
                    units += PlainUnits(run.Text);
                    break;

                case TextRunKind.Numerator:
                    double fraction = PartUnits(run.Text);
                    if (i + 1 < runs.Count && runs[i + 1].Kind == TextRunKind.Denominator)
                    {
                        fraction = Math.Max(fraction, PartUnits(runs[++i].Text));
                    }
                    units += fraction * TextMarkup.FractionScale + 2 * FractionSideGap;
                    break;

                case TextRunKind.Denominator:
                    units += PartUnits(run.Text) * TextMarkup.FractionScale;
                    break;

                default:
                    double width = PlainUnits(run.Text);
                    // Верхний и нижний индексы у одного основания стоят друг над другом.
                    if (i + 1 < runs.Count && IsIndex(runs[i + 1].Kind) && runs[i + 1].Kind != run.Kind)
                    {
                        width = Math.Max(width, PlainUnits(runs[++i].Text));
                    }
                    units += width * TextMarkup.IndexScale;
                    break;
            }
        }
        return units;
    }

    private static bool IsIndex(TextRunKind kind) => kind == TextRunKind.Subscript || kind == TextRunKind.Superscript;

    /// <summary>Сколько места строка занимает ниже и выше базовой линии, в долях высоты шрифта.</summary>
    /// <param name="descent">Выступ букв ниже базовой линии для строки высотой 1.</param>
    /// <param name="ascent">Высота строки над базовой линией для строки высотой 1.</param>
    private static void VerticalExtent(string text, double descent, double ascent, out double below, out double above)
    {
        below = descent;
        above = ascent;
        if (string.IsNullOrEmpty(text) || (text.IndexOf('_') < 0 && text.IndexOf('^') < 0 && text.IndexOf('\\') < 0)) return;

        foreach (TextRun run in TextMarkup.Parse(text))
        {
            if (run.Kind == TextRunKind.Normal) continue;
            double scale = TextMarkup.Scale(run.Kind), rise = TextMarkup.BaselineRise(run.Kind);
            double runBelow = descent, runAbove = ascent;

            if (run.Kind == TextRunKind.Numerator || run.Kind == TextRunKind.Denominator)
            {
                // Индексы внутри числителя или знаменателя увеличивают его строку и сдвигают её от черты дроби.
                rise = TextMarkup.PartRise(run.Kind, run.Text);
                foreach (TextRun inner in TextMarkup.ParsePart(run.Text))
                {
                    if (inner.Kind == TextRunKind.Normal) continue;
                    double innerScale = TextMarkup.Scale(inner.Kind), innerRise = TextMarkup.BaselineRise(inner.Kind);
                    runBelow = Math.Max(runBelow, descent * innerScale - innerRise);
                    runAbove = Math.Max(runAbove, ascent * innerScale + innerRise);
                }
            }

            below = Math.Max(below, runBelow * scale - rise);
            above = Math.Max(above, runAbove * scale + rise);
        }
    }

    /// <summary>Место под базовой линией, которое занимают буквы строки (с индексами и дробями), мм.</summary>
    public static double Below(string text, double heightMm)
    {
        VerticalExtent(text, 0.3, 1.1, out double below, out _);
        return below * heightMm;
    }

    /// <summary>Место над базовой линией, которое занимают буквы строки (с индексами и дробями), мм.</summary>
    public static double Above(string text, double heightMm)
    {
        VerticalExtent(text, 0.3, 1.1, out _, out double above);
        return above * heightMm;
    }

    /// <summary>
    /// Прямоугольник, который занимают буквы горизонтальной строки с базовой линией в точке <paramref name="anchor"/>.
    /// </summary>
    public static RectD EstimateBox(string text, double heightMm, Pt anchor, TextAlign align)
    {
        double width = EstimateWidth(text, heightMm);
        double left = align == TextAlign.Left ? 0 : align == TextAlign.Center ? -width / 2 : -width;
        VerticalExtent(text, 0.3, 1.1, out double below, out double above);
        return new RectD(anchor.X + left, anchor.Y - below * heightMm, anchor.X + left + width, anchor.Y + above * heightMm);
    }

    // В КОМПАС текст закрывает то, что лежит под ним, — и линии, и другие надписи (настройка документа
    // «Перекрывающиеся объекты», по умолчанию включена). Закрываемая область измерена на КОМПАС-3D v25
    // для чертёжного шрифта с наклоном при высоте 3,5; 5 и 10 мм — она пропорциональна высоте шрифта:
    // строка занимает от 0,32 высоты под базовой линией до 1,11 над ней, по ширине — от точки привязки
    // до конца строки плюс 0,35 высоты; у индексов и дробей — то же для каждой их строки.
    // Дробь, кроме того, выступает на 0,15 высоты влево от своего начала и на 0,43 вправо от конца.

    /// <summary>Закрываемая область ниже базовой линии строки, в долях высоты шрифта (без запаса).</summary>
    public const double ClearBelow = 0.32;

    /// <summary>Закрываемая область выше базовой линии строки, в долях высоты шрифта (без запаса).</summary>
    public const double ClearAbove = 1.15;

    /// <summary>Добавка к ширине строки справа (наклон букв), в долях высоты шрифта.</summary>
    public const double ClearPad = 0.37;

    /// <summary>На сколько закрываемая область выступает влево, если строка начинается с дроби, в долях высоты шрифта.</summary>
    public const double FractionClearBefore = 0.15;

    /// <summary>Дополнительный выступ вправо, если строка заканчивается дробью, в долях высоты шрифта.</summary>
    public const double FractionClearAfter = 0.11;

    /// <summary>
    /// Запас вокруг закрываемой области, мм: линия имеет толщину (половина основной линии — до 0,5 мм),
    /// а ширина строки оценивается приближённо.
    /// </summary>
    public const double ClearGapMm = 0.5;

    /// <summary>
    /// Область, которую горизонтальный текст очищает под собой на чертеже КОМПАС.
    /// Линии, которые должны остаться целыми (полка выноски, рамка, оси), располагают вне её.
    /// </summary>
    public static RectD ClearedBox(string text, double heightMm, Pt anchor, TextAlign align)
    {
        double advance = EstimateWidth(text, heightMm);
        FractionOverhang(text, out double before, out double after);
        double padBefore = before * heightMm, padAfter = (ClearPad + after) * heightMm;
        double width = padBefore + advance + padAfter;
        // КОМПАС выравнивает строку вместе с добавками: по центру — середину области, вправо — её правый край.
        double left = align == TextAlign.Left ? -padBefore : align == TextAlign.Center ? -width / 2 : -width;
        VerticalExtent(text, ClearBelow, ClearAbove, out double below, out double above);

        return new RectD(anchor.X + left - ClearGapMm, anchor.Y - below * heightMm - ClearGapMm,
            anchor.X + left + width + ClearGapMm, anchor.Y + above * heightMm + ClearGapMm);
    }

    /// <summary>Выступы закрываемой области у строки, которая начинается или заканчивается дробью, в долях высоты шрифта.</summary>
    private static void FractionOverhang(string text, out double before, out double after)
    {
        before = after = 0;
        if (string.IsNullOrEmpty(text) || text.IndexOf('\\') < 0) return;

        List<TextRun> runs = TextMarkup.Parse(text);
        if (runs.Count == 0) return;
        if (IsFractionPart(runs[0].Kind)) before = FractionClearBefore;
        if (IsFractionPart(runs[runs.Count - 1].Kind)) after = FractionClearAfter;
    }

    private static bool IsFractionPart(TextRunKind kind) => kind == TextRunKind.Numerator || kind == TextRunKind.Denominator;

    /// <summary>На сколько закрываемая область строки выступает над базовой линией, мм.</summary>
    public static double ClearedAbove(string text, double heightMm)
    {
        VerticalExtent(text, ClearBelow, ClearAbove, out _, out double above);
        return above * heightMm + ClearGapMm;
    }

    /// <summary>На сколько закрываемая область строки выступает под базовую линию, мм.</summary>
    public static double ClearedBelow(string text, double heightMm)
    {
        VerticalExtent(text, ClearBelow, ClearAbove, out double below, out _);
        return below * heightMm + ClearGapMm;
    }

    private static double PlainUnits(string text)
    {
        double units = 0;
        foreach (char c in text)
        {
            units += CharWidth(c);
        }
        return units;
    }

    /// <summary>Ширина символа вместе с межбуквенным просветом, в долях высоты шрифта.</summary>
    private static double CharWidth(char c)
    {
        if (c >= '0' && c <= '9') return c == '1' ? 0.45 : 0.62;
        switch (c)
        {
            case '.':
            case ',':
            case ':':
            case ';':
            case '\'':
            case '!':
            case 'i':
            case 'l':
            case 'I':
            case '°':
                return 0.32;
            case ' ':
                return 0.45;
            case '-':
            case '+':
            case '=':
            case '−':
            case '–':
                return 0.62;
            case 'М':
            case 'Ш':
            case 'Щ':
            case 'Ж':
            case 'Ю':
            case 'Ы':
            case 'M':
            case 'W':
            case 'м':
            case 'ш':
            case 'щ':
            case 'ж':
            case 'ю':
            case 'ы':
            case 'm':
            case 'w':
            case 'ω':
                return 0.85;
        }
        return char.IsUpper(c) ? 0.72 : 0.62;
    }
}
