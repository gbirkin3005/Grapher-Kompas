using System;
using System.Globalization;
using System.Text;

namespace Grapher.Core.Parsing;

public enum NumberParseStatus
{
    /// <summary>Обычное конечное число.</summary>
    Ok,
    /// <summary>Пустая ячейка.</summary>
    Empty,
    /// <summary>Текст, который не является числом.</summary>
    NotANumber,
    /// <summary>NaN или бесконечность.</summary>
    NonFinite
}

/// <summary>
/// Разбор чисел из Mathcad, Excel и ручного ввода.
/// Десятичным разделителем может быть и точка, и запятая; пробелы между разрядами допускаются.
/// </summary>
public static class NumberParser
{
    public static bool TryParse(string text, out double value) => Parse(text, out value) == NumberParseStatus.Ok;

    public static NumberParseStatus Parse(string text, out double value)
    {
        value = double.NaN;
        if (text == null) return NumberParseStatus.Empty;

        string s = Normalize(text);
        if (s.Length == 0) return NumberParseStatus.Empty;

        string lower = s.ToLowerInvariant();
        string unsigned = lower.TrimStart('+', '-');
        if (unsigned == "nan" || unsigned == "inf" || unsigned == "infinity" || unsigned == "∞")
        {
            return NumberParseStatus.NonFinite;
        }

        if (!SplitPowerOfTen(lower, out string mantissa, out string exponent))
        {
            return NumberParseStatus.NotANumber;
        }

        if (!NormalizeSeparators(mantissa, out string plain))
        {
            return NumberParseStatus.NotANumber;
        }

        string candidate = exponent == null ? plain : plain + "e" + exponent;
        const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
        if (!double.TryParse(candidate, styles, CultureInfo.InvariantCulture, out double parsed))
        {
            return NumberParseStatus.NotANumber;
        }

        if (double.IsNaN(parsed) || double.IsInfinity(parsed))
        {
            return NumberParseStatus.NonFinite;
        }

        value = parsed;
        return NumberParseStatus.Ok;
    }

    /// <summary>Убирает пробелы всех видов и приводит типографские минусы к обычному.</summary>
    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case ' ':
                case '\t':
                case '\r':
                case '\n':
                case ' ': // неразрывный пробел
                case ' ': // узкий неразрывный пробел
                case ' ': // тонкий пробел
                case ' ': // цифровой пробел
                    break;
                case '−': // минус
                case '–': // короткое тире
                case '‒':
                    sb.Append('-');
                    break;
                case 'Е': // кириллические «Е», которые иногда попадают в экспоненту
                case 'е':
                    sb.Append('e');
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Выделяет мантиссу и порядок. Понимает «1.5e3», «1,5E-3» и запись Mathcad «1.5·10^3».
    /// </summary>
    private static bool SplitPowerOfTen(string s, out string mantissa, out string exponent)
    {
        mantissa = s;
        exponent = null;

        int pow = s.IndexOf("10^", StringComparison.Ordinal);
        if (pow >= 0)
        {
            string head = s.Substring(0, pow);
            exponent = s.Substring(pow + 3).Trim('(', ')');
            if (head.Length == 0 || head == "+" || head == "-")
            {
                mantissa = head + "1";
            }
            else
            {
                char mul = head[head.Length - 1];
                if (mul != '·' && mul != '*' && mul != '×' && mul != 'x' && mul != '⋅' && mul != 'х')
                {
                    return false;
                }
                mantissa = head.Substring(0, head.Length - 1);
            }
            return IsInteger(exponent) && mantissa.Length > 0;
        }

        int e = s.LastIndexOfAny(new[] { 'e', 'd' });
        if (e > 0)
        {
            string tail = s.Substring(e + 1);
            if (!IsInteger(tail)) return false;
            mantissa = s.Substring(0, e);
            exponent = tail;
        }
        return true;
    }

    private static bool IsInteger(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        int start = s[0] == '+' || s[0] == '-' ? 1 : 0;
        if (start == s.Length) return false;
        for (int i = start; i < s.Length; i++)
        {
            if (s[i] < '0' || s[i] > '9') return false;
        }
        return true;
    }

    /// <summary>Приводит мантиссу к виду с десятичной точкой без разделителей разрядов.</summary>
    private static bool NormalizeSeparators(string s, out string result)
    {
        result = s;
        int commas = Count(s, ','), dots = Count(s, '.');

        if (commas > 0 && dots > 0)
        {
            // Оба знака: последний по порядку — десятичный, другой разделяет разряды.
            char dec = s.LastIndexOf(',') > s.LastIndexOf('.') ? ',' : '.';
            char group = dec == ',' ? '.' : ',';
            if (Count(s, dec) != 1) return false;
            result = s.Replace(group.ToString(), string.Empty).Replace(dec, '.');
        }
        else if (commas == 1)
        {
            result = s.Replace(',', '.');
        }
        else if (commas > 1)
        {
            if (!IsGrouped(s, ',')) return false;
            result = s.Replace(",", string.Empty);
        }
        else if (dots > 1)
        {
            if (!IsGrouped(s, '.')) return false;
            result = s.Replace(".", string.Empty);
        }

        return HasDigit(result);
    }

    /// <summary>Проверяет запись вида «1,234,567»: группы строго по три цифры.</summary>
    private static bool IsGrouped(string s, char sep)
    {
        string body = s.TrimStart('+', '-');
        string[] parts = body.Split(sep);
        if (parts[0].Length < 1 || parts[0].Length > 3) return false;
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0 && parts[i].Length != 3) return false;
            foreach (char c in parts[i])
            {
                if (c < '0' || c > '9') return false;
            }
        }
        return true;
    }

    private static int Count(string s, char c)
    {
        int n = 0;
        foreach (char ch in s)
        {
            if (ch == c) n++;
        }
        return n;
    }

    private static bool HasDigit(string s)
    {
        foreach (char c in s)
        {
            if (c >= '0' && c <= '9') return true;
        }
        return false;
    }
}
