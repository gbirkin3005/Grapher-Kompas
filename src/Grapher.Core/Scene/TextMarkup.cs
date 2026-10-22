using System.Collections.Generic;
using System.Text;

namespace Grapher.Core.Scene;

public enum TextRunKind
{
    Normal,
    /// <summary>Нижний индекс.</summary>
    Subscript,
    /// <summary>Верхний индекс.</summary>
    Superscript,
    /// <summary>Числитель дроби; сразу за ним идёт знаменатель. Текст может содержать индексы.</summary>
    Numerator,
    /// <summary>Знаменатель дроби. Текст может содержать индексы.</summary>
    Denominator
}

/// <summary>Фрагмент строки: обычный текст, индекс или часть дроби.</summary>
public readonly struct TextRun
{
    public readonly string Text;
    public readonly TextRunKind Kind;

    public TextRun(string text, TextRunKind kind)
    {
        Text = text;
        Kind = kind;
    }

    public override string ToString()
    {
        switch (Kind)
        {
            case TextRunKind.Subscript: return "_{" + Text + "}";
            case TextRunKind.Superscript: return "^{" + Text + "}";
            case TextRunKind.Numerator: return "\\frac{" + Text + "}";
            case TextRunKind.Denominator: return "{" + Text + "}";
            default: return Text;
        }
    }
}

/// <summary>
/// Индексы и дроби в подписях. На чертеже они выполняются штатными средствами текста КОМПАС
/// (вставка «Индекс» и «Дробь»), в предпросмотре рисуются так же, как их размещает КОМПАС.
/// <para>
/// Знак «_» начинает нижний индекс, «^» — верхний: <c>V_q</c>, <c>ω_q2</c>, <c>м^2</c>, <c>с^-1</c>.
/// Индекс продолжается, пока идут буквы и цифры (у верхнего индекса впереди допускается знак);
/// несколько слов берут в фигурные скобки: <c>M_{с пр}</c>. Дробь записывается как <c>\frac{мм}{м}</c>;
/// в числителе и знаменателе допускаются индексы: <c>\frac{V_q}{м}</c>, <c>\frac{м}{с^2}</c>.
/// Обратная косая черта отменяет особый смысл следующего знака: <c>a\_b</c>.
/// </para>
/// </summary>
public static class TextMarkup
{
    /// <summary>
    /// Высота индекса в долях высоты текста: «индекс средней высоты» КОМПАС (2/3).
    /// При тексте 5 мм индекс получается 3,3 мм, при 3,5 мм — 2,3 мм.
    /// </summary>
    public const double IndexScale = 2.0 / 3.0;

    /// <summary>Высота числителя и знаменателя в долях высоты текста: «дробь нормальной высоты» КОМПАС.</summary>
    public const double FractionScale = 1.0;

    /// <summary>Высота черты дроби над базовой линией строки, в долях высоты текста.</summary>
    public const double FractionBarRise = 0.5;

    // Положение строк индекса и дроби измерено на КОМПАС-3D v25 (шрифт GOST Type AU):
    // верхняя строка стоит на (0,552 + 0,339·k)·h выше базовой линии, нижняя — на (0,379 − 1,137·k)·h,
    // где k — отношение высоты индекса (части дроби) к высоте текста h.

    /// <summary>Подъём базовой линии верхнего индекса или числителя, в долях высоты текста.</summary>
    public static double UpperRise(double scale) => 0.552 + 0.339 * scale;

    /// <summary>Подъём базовой линии нижнего индекса или знаменателя (отрицательный — ниже строки).</summary>
    public static double LowerRise(double scale) => 0.379 - 1.137 * scale;

    /// <summary>Высота фрагмента в долях высоты текста.</summary>
    public static double Scale(TextRunKind kind)
    {
        switch (kind)
        {
            case TextRunKind.Subscript:
            case TextRunKind.Superscript:
                return IndexScale;
            case TextRunKind.Numerator:
            case TextRunKind.Denominator:
                return FractionScale;
            default:
                return 1;
        }
    }

    /// <summary>Подъём базовой линии фрагмента относительно строки, в долях высоты текста.</summary>
    public static double BaselineRise(TextRunKind kind)
    {
        switch (kind)
        {
            case TextRunKind.Superscript: return UpperRise(IndexScale);
            case TextRunKind.Subscript: return LowerRise(IndexScale);
            case TextRunKind.Numerator: return UpperRise(FractionScale);
            case TextRunKind.Denominator: return LowerRise(FractionScale);
            default: return 0;
        }
    }

    // Строка числителя с нижним индексом выше обычной, а строка знаменателя с верхним индексом — ниже:
    // КОМПАС отодвигает их от черты дроби. Измерено на КОМПАС-3D v25 для индекса средней высоты.

    /// <summary>На сколько поднят числитель, в котором есть нижний индекс, в долях высоты текста.</summary>
    public const double NumeratorLift = 0.26;

    /// <summary>На сколько опущен знаменатель, в котором есть верхний индекс, в долях высоты текста.</summary>
    public const double DenominatorDrop = 0.34;

    /// <summary>Подъём базовой линии числителя или знаменателя с учётом индексов внутри него, в долях высоты текста.</summary>
    public static double PartRise(TextRunKind kind, string part)
    {
        double rise = BaselineRise(kind);
        if (string.IsNullOrEmpty(part) || !MayHaveMarkup(part)) return rise;

        foreach (TextRun run in ParsePart(part))
        {
            if (kind == TextRunKind.Numerator && run.Kind == TextRunKind.Subscript) return rise + NumeratorLift * FractionScale;
            if (kind == TextRunKind.Denominator && run.Kind == TextRunKind.Superscript) return rise - DenominatorDrop * FractionScale;
        }
        return rise;
    }

    /// <summary>
    /// Запись дроби. Индексы в числителе и знаменателе сохраняются; вложенная дробь записывается через косую черту.
    /// </summary>
    public static string Fraction(string numerator, string denominator) =>
        "\\frac{" + Part(numerator) + "}{" + Part(denominator) + "}";

    private static string Braced(string text) => (text ?? "").Replace("{", "(").Replace("}", ")");

    /// <summary>Разметка части дроби: обычный текст и индексы, без вложенных дробей и лишних фигурных скобок.</summary>
    private static string Part(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (!MayHaveMarkup(text)) return Braced(text);

        var result = new StringBuilder(text.Length + 8);
        foreach (TextRun run in ParsePart(text))
        {
            switch (run.Kind)
            {
                case TextRunKind.Subscript:
                    result.Append("_{").Append(Braced(run.Text)).Append('}');
                    break;
                case TextRunKind.Superscript:
                    result.Append("^{").Append(Braced(run.Text)).Append('}');
                    break;
                default:
                    result.Append(Escape(Braced(run.Text)));
                    break;
            }
        }
        return result.ToString();
    }

    /// <summary>
    /// Разбор числителя или знаменателя: обычный текст и индексы. Дробь внутри дроби читается как «a/b».
    /// </summary>
    public static List<TextRun> ParsePart(string text)
    {
        List<TextRun> runs = Parse(text);
        bool nested = false;
        foreach (TextRun run in runs)
        {
            if (run.Kind == TextRunKind.Numerator || run.Kind == TextRunKind.Denominator) nested = true;
        }
        if (!nested) return runs;

        var flat = new List<TextRun>(runs.Count);
        foreach (TextRun run in runs)
        {
            switch (run.Kind)
            {
                case TextRunKind.Numerator:
                    flat.Add(new TextRun(Plain(run.Text), TextRunKind.Normal));
                    break;
                case TextRunKind.Denominator:
                    flat.Add(new TextRun("/" + Plain(run.Text), TextRunKind.Normal));
                    break;
                default:
                    flat.Add(run);
                    break;
            }
        }
        return flat;
    }

    public static List<TextRun> Parse(string text)
    {
        var runs = new List<TextRun>();
        if (string.IsNullOrEmpty(text)) return runs;

        var normal = new StringBuilder();
        void FlushNormal()
        {
            if (normal.Length == 0) return;
            runs.Add(new TextRun(normal.ToString(), TextRunKind.Normal));
            normal.Clear();
        }

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\\')
            {
                if (TryReadFraction(text, i, out string numerator, out string denominator, out int afterFraction))
                {
                    FlushNormal();
                    runs.Add(new TextRun(numerator, TextRunKind.Numerator));
                    runs.Add(new TextRun(denominator, TextRunKind.Denominator));
                    i = afterFraction;
                    continue;
                }
                if (i + 1 < text.Length)
                {
                    normal.Append(text[i + 1]);
                    i += 2;
                    continue;
                }
            }

            if ((c == '_' || c == '^') && i + 1 < text.Length && TryReadIndex(text, i + 1, c == '^', out string index, out int next))
            {
                FlushNormal();
                runs.Add(new TextRun(index, c == '_' ? TextRunKind.Subscript : TextRunKind.Superscript));
                i = next;
                continue;
            }

            normal.Append(c);
            i++;
        }
        FlushNormal();
        return runs;
    }

    private const string FractionTag = "\\frac{";

    private static bool TryReadFraction(string text, int start, out string numerator, out string denominator, out int next)
    {
        numerator = denominator = null;
        next = start;
        if (string.CompareOrdinal(text, start, FractionTag, 0, FractionTag.Length) != 0) return false;

        // Числитель и знаменатель — в фигурных скобках; внутри могут быть индексы со своими скобками.
        if (!TryReadBraced(text, start + FractionTag.Length - 1, out numerator, out int afterNumerator)) return false;
        return TryReadBraced(text, afterNumerator, out denominator, out next);
    }

    /// <summary>Читает содержимое парных фигурных скобок, начиная с открывающей.</summary>
    private static bool TryReadBraced(string text, int open, out string content, out int next)
    {
        content = null;
        next = open;
        if (open >= text.Length || text[open] != '{') return false;

        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\')
            {
                i++;
                continue;
            }
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                content = text.Substring(open + 1, i - open - 1);
                next = i + 1;
                return true;
            }
        }
        return false;
    }

    private static bool TryReadIndex(string text, int start, bool allowSign, out string index, out int next)
    {
        index = null;
        next = start;

        if (text[start] == '{')
        {
            int close = text.IndexOf('}', start + 1);
            if (close < 0 || close == start + 1) return false;
            index = text.Substring(start + 1, close - start - 1);
            next = close + 1;
            return true;
        }

        int end = start;
        if (allowSign && (text[end] == '-' || text[end] == '+' || text[end] == '−')) end++;
        int firstLetter = end;
        while (end < text.Length && char.IsLetterOrDigit(text[end])) end++;
        if (end == firstLetter) return false;

        index = text.Substring(start, end - start);
        next = end;
        return true;
    }

    private static bool MayHaveMarkup(string text) =>
        text.IndexOf('_') >= 0 || text.IndexOf('^') >= 0 || text.IndexOf('\\') >= 0;

    /// <summary>Есть ли в строке индексы.</summary>
    public static bool HasIndices(string text) => Has(text, TextRunKind.Subscript, TextRunKind.Superscript);

    /// <summary>Есть ли в строке дроби.</summary>
    public static bool HasFractions(string text) => Has(text, TextRunKind.Numerator, TextRunKind.Denominator);

    /// <summary>Есть ли в строке что-либо, кроме обычного текста.</summary>
    public static bool HasStructure(string text)
    {
        if (string.IsNullOrEmpty(text) || !MayHaveMarkup(text)) return false;
        foreach (TextRun run in Parse(text))
        {
            if (run.Kind != TextRunKind.Normal) return true;
        }
        return false;
    }

    private static bool Has(string text, TextRunKind first, TextRunKind second)
    {
        if (string.IsNullOrEmpty(text) || !MayHaveMarkup(text)) return false;
        foreach (TextRun run in Parse(text))
        {
            if (run.Kind == first || run.Kind == second) return true;
        }
        return false;
    }

    /// <summary>Текст без разметки: все знаки подряд, как их прочтёт человек (дробь — через косую черту).</summary>
    public static string Plain(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (!MayHaveMarkup(text)) return text;

        var result = new StringBuilder(text.Length);
        foreach (TextRun run in Parse(text))
        {
            if (run.Kind == TextRunKind.Denominator) result.Append('/');
            bool part = run.Kind == TextRunKind.Numerator || run.Kind == TextRunKind.Denominator;
            result.Append(part ? Plain(run.Text) : run.Text);
        }
        return result.ToString();
    }

    /// <summary>Заключает знаки разметки в обычном тексте, чтобы он был показан как есть.</summary>
    public static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("\\", "\\\\").Replace("_", "\\_").Replace("^", "\\^");
    }
}
