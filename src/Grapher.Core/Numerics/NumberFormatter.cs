using System;
using System.Globalization;

namespace Grapher.Core.Numerics;

/// <summary>Форматирование числовых подписей осей.</summary>
public static class NumberFormatter
{
    /// <param name="decimals">Число знаков после запятой (0…15).</param>
    /// <param name="decimalComma">true — десятичная запятая, false — точка.</param>
    public static string Format(double value, int decimals, bool decimalComma)
    {
        if (decimals < 0) decimals = 0;
        if (decimals > 15) decimals = 15;

        double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        if (rounded == 0) rounded = 0; // «-0» превращаем в «0»

        string text = rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return decimalComma ? text.Replace('.', ',') : text;
    }

    /// <summary>Число для поля ввода: без лишних нулей, с разделителем текущей культуры.</summary>
    public static string ForInput(double value, int maxDecimals = 6)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "";
        double rounded = Math.Round(value, maxDecimals);
        if (rounded == 0) rounded = 0;
        return rounded.ToString("0." + new string('#', maxDecimals), CultureInfo.CurrentCulture);
    }
}
