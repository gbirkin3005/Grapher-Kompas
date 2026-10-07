namespace Grapher.Core.Scene;

/// <summary>
/// Приближённая ширина текста чертёжным шрифтом ГОСТ 2.304 (тип А).
/// Нужна только для расстановки подписей (отступы, прореживание) и расчёта габаритов;
/// точная ширина определяется шрифтом в КОМПАС и в предпросмотре.
/// </summary>
public static class TextMetrics
{
    /// <summary>Ширина строки в мм при высоте прописных букв <paramref name="heightMm"/>.</summary>
    public static double EstimateWidth(string text, double heightMm)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        double units = 0;
        foreach (char c in text)
        {
            units += CharWidth(c);
        }
        return units * heightMm;
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
                return 0.32;
            case ' ':
                return 0.45;
            case '-':
            case '+':
            case '=':
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
                return 0.85;
        }
        return char.IsUpper(c) ? 0.72 : 0.62;
    }
}
