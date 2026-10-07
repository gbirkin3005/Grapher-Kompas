using System.Globalization;

namespace Grapher.Core.Model;

/// <summary>
/// Цвет линии в виде числа 0xRRGGBB и его текстовая запись «#RRGGBB» для файла проекта.
/// Ядро не зависит от графических библиотек, поэтому цвет хранится числом.
/// </summary>
public static class ColorValue
{
    /// <summary>Разбирает запись «#RRGGBB» (решётка необязательна). Пустая строка — «цвета нет».</summary>
    public static bool TryParse(string text, out int rgb)
    {
        rgb = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string hex = text.Trim().TrimStart('#');
        if (hex.Length != 6) return false;
        return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
    }

    /// <summary>Цвет из текста; null, если цвет не задан или записан неверно.</summary>
    public static int? Parse(string text) => TryParse(text, out int rgb) ? rgb : (int?)null;

    public static string Format(int rgb) =>
        "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

    public static int FromRgb(int red, int green, int blue) =>
        ((red & 0xFF) << 16) | ((green & 0xFF) << 8) | (blue & 0xFF);

    public static int Red(int rgb) => (rgb >> 16) & 0xFF;
    public static int Green(int rgb) => (rgb >> 8) & 0xFF;
    public static int Blue(int rgb) => rgb & 0xFF;

    /// <summary>Тот же цвет в порядке байтов Windows и КОМПАС (0xBBGGRR).</summary>
    public static int ToBgr(int rgb) => (Blue(rgb) << 16) | (Green(rgb) << 8) | Red(rgb);
}
