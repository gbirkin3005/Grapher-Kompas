using System.Collections.Generic;
using System.Text;
using Grapher.Core.Scene;
using Kompas6Constants;
using KompasAPI7;

namespace Grapher.Kompas;

/// <summary>
/// Тексты графика в КОМПАС. Каждая подпись — один объект «текст». Индексы и дроби выполняются
/// штатными средствами текста КОМПАС (в панели ввода текста — «Вставка → Индекс» и «Вставка → Дробь»):
/// в строке это элементы «верхнее/нижнее отклонение» и «числитель/знаменатель».
/// Индекс — средней высоты (2/3 высоты текста), дробь — нормальной высоты.
/// Индекс может стоять и внутри числителя или знаменателя дроби.
/// </summary>
internal static class TextWriter
{
    /// <returns>Число созданных объектов; 0 — текст пуст; −1 — КОМПАС отказался создать объект.</returns>
    public static int Draw(IDrawingContainer target, TextPrimitive primitive)
    {
        if (string.IsNullOrEmpty(primitive.Text) || !(primitive.HeightMm > 0)) return 0;

        bool structured = TextMarkup.HasStructure(primitive.Text);
        string plain = structured ? null : TextMarkup.Plain(primitive.Text);
        if (!structured && plain.Length == 0) return 0;

        // Точка привязки текста КОМПАС — на базовой линии; Allocation задаёт, какой конец строки в ней находится.
        IDrawingText drawingText = target.DrawingTexts.Add();
        drawingText.X = primitive.Anchor.X;
        drawingText.Y = primitive.Anchor.Y;
        drawingText.Angle = primitive.AngleDeg;
        drawingText.Allocation = ToAllocation(primitive.Align);

        var text = (IText)drawingText;
        if (structured)
        {
            WriteItems(text.Add(), primitive);
        }
        else
        {
            text.Str = plain;
            // Шрифт и высота задаются для каждого фрагмента строки.
            for (int i = 0; i < text.Count; i++)
            {
                ITextLine line = text.get_TextLine(i);
                for (int j = 0; j < line.Count; j++)
                {
                    ITextItem item = line.get_TextItem(j);
                    SetFont(item, primitive.FontName, primitive.HeightMm, primitive.Italic);
                    item.Update();
                }
            }
        }

        return drawingText.Update() ? 1 : -1;
    }

    /// <summary>Строка из обычного текста, индексов и дробей — элементами текста КОМПАС.</summary>
    private static void WriteItems(ITextLine line, TextPrimitive primitive)
    {
        List<TextRun> runs = TextMarkup.Parse(primitive.Text);
        double height = primitive.HeightMm;
        double fractionHeight = height * TextMarkup.FractionScale;

        void Add(ksTextItemEnum type, string str, ksTextSizeEnum size, double itemHeight)
        {
            ITextItem item = line.Add();
            item.ItemType = type;
            item.Str = str ?? "";
            if (size != ksTextSizeEnum.ksTextDefault) item.SizeFactor = size;
            SetFont(item, primitive.FontName, itemHeight, primitive.Italic);
            item.Update();
        }

        // Обычный текст и индексы подряд; baseHeight — высота строки, в которой они стоят.
        void AddRuns(List<TextRun> items, int from, double baseHeight)
        {
            double indexHeight = baseHeight * TextMarkup.IndexScale;
            for (int i = from; i < items.Count; i++)
            {
                TextRun run = items[i];
                if (run.Kind == TextRunKind.Normal)
                {
                    Add(ksTextItemEnum.ksTItString, run.Text, ksTextSizeEnum.ksTextDefault, baseHeight);
                    continue;
                }

                // Верхний и нижний индексы у одного основания — одна вставка «Индекс».
                ReadIndexPair(items, ref i, out string upper, out string lower);
                Add(ksTextItemEnum.ksTItUpperDeviation, upper, IndexSize, indexHeight);
                if (lower.Length > 0) Add(ksTextItemEnum.ksTItLowerDeviation, lower, ksTextSizeEnum.ksTextDefault, indexHeight);
                Add(ksTextItemEnum.ksTItDeviationEnd, "", ksTextSizeEnum.ksTextDefault, baseHeight);
            }
        }

        // Числитель или знаменатель. Текст до первого индекса записывается в сам элемент части дроби,
        // индексы и текст после них — следующими элементами (так же их хранит КОМПАС).
        void AddPart(ksTextItemEnum type, string part, ksTextSizeEnum size)
        {
            List<TextRun> inner = TextMarkup.ParsePart(part ?? "");
            var lead = new StringBuilder();
            int next = 0;
            while (next < inner.Count && inner[next].Kind == TextRunKind.Normal)
            {
                lead.Append(inner[next++].Text);
            }
            Add(type, lead.ToString(), size, fractionHeight);
            AddRuns(inner, next, fractionHeight);
        }

        // Строка, которая начинается с дроби или индекса, должна начинаться с пустого фрагмента высотой текста.
        // Иначе КОМПАС сам вставит пустой фрагмент высотой 5 мм (умолчание стиля) и разместит дробь
        // относительно него: при тексте 3,5 мм дробь окажется выше, чем нужно, при 7 мм — ниже.
        if (runs.Count > 0 && runs[0].Kind != TextRunKind.Normal)
        {
            Add(ksTextItemEnum.ksTItString, "", ksTextSizeEnum.ksTextDefault, height);
        }

        var plainAndIndices = new List<TextRun>();
        void FlushPlain()
        {
            if (plainAndIndices.Count == 0) return;
            AddRuns(plainAndIndices, 0, height);
            plainAndIndices.Clear();
        }

        for (int i = 0; i < runs.Count; i++)
        {
            TextRun run = runs[i];
            if (run.Kind != TextRunKind.Numerator && run.Kind != TextRunKind.Denominator)
            {
                plainAndIndices.Add(run);
                continue;
            }

            FlushPlain();
            ReadFraction(runs, ref i, out string numerator, out string denominator);
            AddPart(ksTextItemEnum.ksTItNumerator, numerator, FractionSize);
            AddPart(ksTextItemEnum.ksTItDenominator, denominator, ksTextSizeEnum.ksTextDefault);
            Add(ksTextItemEnum.ksTItFractionEnd, "", ksTextSizeEnum.ksTextDefault, height);
        }
        FlushPlain();
    }

    /// <summary>Индекс в позиции <paramref name="i"/> и, если следом идёт парный ему, оба сразу.</summary>
    private static void ReadIndexPair(List<TextRun> runs, ref int i, out string upper, out string lower)
    {
        TextRun run = runs[i];
        upper = run.Kind == TextRunKind.Superscript ? run.Text : "";
        lower = run.Kind == TextRunKind.Subscript ? run.Text : "";
        if (i + 1 < runs.Count && runs[i + 1].Kind != run.Kind &&
            (runs[i + 1].Kind == TextRunKind.Subscript || runs[i + 1].Kind == TextRunKind.Superscript))
        {
            TextRun second = runs[++i];
            if (second.Kind == TextRunKind.Superscript) upper = second.Text;
            else lower = second.Text;
        }
    }

    /// <summary>Числитель в позиции <paramref name="i"/> вместе со знаменателем, идущим следом.</summary>
    private static void ReadFraction(List<TextRun> runs, ref int i, out string numerator, out string denominator)
    {
        TextRun run = runs[i];
        numerator = run.Kind == TextRunKind.Numerator ? run.Text : "";
        denominator = run.Kind == TextRunKind.Denominator ? run.Text : "";
        if (run.Kind == TextRunKind.Numerator && i + 1 < runs.Count && runs[i + 1].Kind == TextRunKind.Denominator)
        {
            denominator = runs[++i].Text;
        }
    }

    /// <summary>Индекс средней высоты: 2/3 высоты текста.</summary>
    private const ksTextSizeEnum IndexSize = ksTextSizeEnum.ksTextMiddle;

    /// <summary>Дробь нормальной высоты: числитель и знаменатель той же высоты, что и текст.</summary>
    private const ksTextSizeEnum FractionSize = ksTextSizeEnum.ksTextNormal;

    private static void SetFont(ITextItem item, string fontName, double height, bool italic)
    {
        var font = (ITextFont)item;
        if (!string.IsNullOrWhiteSpace(fontName)) font.FontName = fontName;
        font.Height = height;
        font.Italic = italic;
    }

    /// <summary>
    /// Строка в том виде, в каком её возвращает КОМПАС (свойство Str текста): индекс средней высоты —
    /// <c>$m верхний ; нижний $</c>, дробь нормальной высоты — <c>$d числитель ; знаменатель $</c>.
    /// Нужна для сверки чертежа со сценой.
    /// </summary>
    public static string KompasString(string markup)
    {
        if (!TextMarkup.HasStructure(markup)) return TextMarkup.Plain(markup);

        var result = new StringBuilder();
        AppendCoded(result, TextMarkup.Parse(markup));
        return result.ToString();
    }

    private static void AppendCoded(StringBuilder result, List<TextRun> runs)
    {
        for (int i = 0; i < runs.Count; i++)
        {
            TextRun run = runs[i];
            switch (run.Kind)
            {
                case TextRunKind.Normal:
                    result.Append(run.Text);
                    break;

                case TextRunKind.Numerator:
                case TextRunKind.Denominator:
                    ReadFraction(runs, ref i, out string numerator, out string denominator);
                    result.Append("$d");
                    AppendCoded(result, TextMarkup.ParsePart(numerator));
                    result.Append(';');
                    AppendCoded(result, TextMarkup.ParsePart(denominator));
                    result.Append('$');
                    break;

                default:
                    ReadIndexPair(runs, ref i, out string upper, out string lower);
                    result.Append("$m").Append(upper).Append(';').Append(lower).Append('$');
                    break;
            }
        }
    }

    private static ksAllocationEnum ToAllocation(TextAlign align)
    {
        switch (align)
        {
            case TextAlign.Center:
                return ksAllocationEnum.ksAlCentre;
            case TextAlign.Right:
                return ksAllocationEnum.ksAlRight;
            default:
                return ksAllocationEnum.ksAlLeft;
        }
    }
}
