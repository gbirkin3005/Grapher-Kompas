using System;

namespace Grapher.Core.Model;

/// <summary>
/// Чертёжные шрифты КОМПАС по ГОСТ 2.304. Вместе с КОМПАС ставятся две пары:
/// «GOST type A» / «GOST type B» — старые, без греческих букв, и
/// «GOST Type AU» / «GOST Type BU» — те же начертания с полным набором знаков (шрифт КОМПАС по умолчанию).
/// </summary>
public static class DrawingFonts
{
    /// <summary>Шрифт типа А с греческими буквами — как у текста КОМПАС по умолчанию.</summary>
    public const string Default = "GOST Type AU";

    public static readonly string[] All = { "GOST Type AU", "GOST Type BU", "GOST type A", "GOST type B" };

    /// <summary>
    /// Шрифт, которым нужно набрать конкретный текст. Если выбран старый шрифт, а в тексте есть знаки,
    /// которых в нём нет (φ, σ, Δ…), берётся его полная версия: иначе на чертеже вместо буквы будет квадратик.
    /// Правило применяется при сборке сцены, поэтому чертёж и предпросмотр используют один и тот же шрифт.
    /// </summary>
    public static string ForText(string fontName, string text)
    {
        if (string.IsNullOrWhiteSpace(fontName)) return Default;
        if (string.IsNullOrEmpty(text)) return fontName;

        string full = FullVersion(fontName);
        if (full == null) return fontName;

        foreach (char c in text)
        {
            if (!IsInLegacyFont(c)) return full;
        }
        return fontName;
    }

    private static string FullVersion(string fontName)
    {
        if (string.Equals(fontName, "GOST type A", StringComparison.OrdinalIgnoreCase)) return "GOST Type AU";
        if (string.Equals(fontName, "GOST type B", StringComparison.OrdinalIgnoreCase)) return "GOST Type BU";
        return null;
    }

    /// <summary>Знаки, которые есть в старых шрифтах: латиница, кириллица, цифры и основная пунктуация.</summary>
    private static bool IsInLegacyFont(char c)
    {
        if (c >= ' ' && c <= '~') return true;                 // ASCII
        if (c >= 'А' && c <= 'я') return true;       // А–я
        switch (c)
        {
            case 'Ё':
            case 'ё':
            case '№':
            case '°':
            case '±':
            case '×':
            case '·':
            case '«':
            case '»':
            case ' ':
                return true;
            default:
                return false;
        }
    }
}
