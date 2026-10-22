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
        // Ноль пишется без дробной части: «0», а не «0,0» (как на шкалах в Р 50-77-88).
        if (rounded == 0) return "0";

        string text = rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return decimalComma ? text.Replace('.', ',') : text;
    }

    /// <summary>Число с заданным количеством значащих цифр, без лишних нулей в конце.</summary>
    public static string Significant(double value, int digits, bool decimalComma)
    {
        if (value == 0 || double.IsNaN(value) || double.IsInfinity(value)) return "0";
        int magnitude = (int)Math.Floor(Math.Log10(Math.Abs(value)));
        // Вес последней значащей цифры: 1234,5 при трёх цифрах округляется до десятков.
        int exponent = magnitude - digits + 1;
        double unit = NiceScale.Pow10(exponent);
        double rounded = Math.Round(value / unit, MidpointRounding.AwayFromZero) * unit;
        int decimals = Math.Max(0, Math.Min(15, -exponent));
        rounded = Math.Round(rounded, decimals);
        string text = rounded.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);
        return decimalComma ? text.Replace('.', ',') : text;
    }

    /// <summary>
    /// Представляет угол в радианах как долю π: значение = числитель·π / знаменатель.
    /// </summary>
    /// <returns>false, если угол не кратен π/12 и подобным долям.</returns>
    public static bool TryPiFraction(double value, out int numerator, out int denominator)
    {
        numerator = 0;
        denominator = 1;
        double q = value / Math.PI;
        foreach (int d in PiDenominators)
        {
            double scaled = q * d;
            double rounded = Math.Round(scaled);
            if (Math.Abs(scaled - rounded) > 1e-6 * Math.Max(1, Math.Abs(scaled)) || Math.Abs(rounded) > 100000) continue;

            int n = (int)rounded;
            int gcd = Gcd(Math.Abs(n), d);
            numerator = gcd == 0 ? 0 : n / gcd;
            denominator = gcd == 0 ? 1 : d / gcd;
            return true;
        }
        return false;
    }

    private static readonly int[] PiDenominators = { 1, 2, 3, 4, 6, 12, 5, 8, 10 };

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }
        return a;
    }

    /// <summary>Числитель подписи в долях π: «0», «π», «−π», «5π».</summary>
    public static string PiNumerator(int numerator)
    {
        if (numerator == 0) return "0";
        if (numerator == 1) return "π";
        if (numerator == -1) return "−π";
        return (numerator < 0 ? "−" : "") + Math.Abs(numerator).ToString(CultureInfo.InvariantCulture) + "π";
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
