using System;
using System.Collections.Generic;
using System.Globalization;

namespace Grapher.Core.Numerics;

/// <summary>
/// Логарифмическая шкала (десятичные логарифмы): пределы по целым декадам, линии сетки у 1…9 единиц
/// каждой декады, подписи у декад, а при короткой шкале — ещё у 2 и 5 единиц.
/// </summary>
public static class LogScale
{
    /// <summary>Сколько декад на шкале, чтобы кроме 1, 10, 100 подписывать ещё 2 и 5 единиц декады.</summary>
    public const int FewDecades = 2;

    /// <summary>Пределы, округлённые наружу до целых декад: 0,37…4200 → 0,1…10 000.</summary>
    public static void Limits(double dataMin, double dataMax, out double min, out double max)
    {
        if (!(dataMin > 0) || !(dataMax > 0) || double.IsInfinity(dataMin) || double.IsInfinity(dataMax))
        {
            min = 1;
            max = 10;
            return;
        }
        if (dataMin > dataMax) (dataMin, dataMax) = (dataMax, dataMin);

        // Значения вида 9,9999999 или 100,0000001 считаем лежащими на декаде.
        double low = Math.Floor(Math.Log10(dataMin) + 1e-9);
        double high = Math.Ceiling(Math.Log10(dataMax) - 1e-9);
        if (high <= low) high = low + 1;
        min = NiceScale.Pow10((int)low);
        max = NiceScale.Pow10((int)high);
    }

    /// <summary>Логарифмическая «длина» шкалы — число декад между пределами.</summary>
    public static double Decades(double min, double max) => Math.Log10(max / min);

    /// <summary>
    /// Линии сетки. <paramref name="major"/> — линии, у которых могут стоять числа: декады
    /// (и 2, 5 единиц декады, если декад не больше <see cref="FewDecades"/>); <paramref name="minor"/> — остальные.
    /// </summary>
    public static void Lines(double min, double max, List<double> major, List<double> minor)
    {
        major.Clear();
        minor.Clear();
        if (!(min > 0) || !(max > min)) return;

        bool few = Decades(min, max) <= FewDecades + 1e-9;
        int first = (int)Math.Floor(Math.Log10(min) + 1e-9), last = (int)Math.Ceiling(Math.Log10(max) - 1e-9);
        for (int power = first; power <= last && major.Count + minor.Count < 2000; power++)
        {
            double decade = NiceScale.Pow10(power);
            for (int m = 1; m <= 9; m++)
            {
                double value = Snap(m * decade);
                if (value < min * (1 - 1e-9) || value > max * (1 + 1e-9)) continue;
                bool labeled = m == 1 || few && (m == 2 || m == 5);
                (labeled ? major : minor).Add(value);
            }
        }
    }

    /// <summary>Номер декады значения, если оно — степень десяти, иначе null.</summary>
    public static int? DecadeOf(double value)
    {
        if (!(value > 0)) return null;
        double power = Math.Log10(value);
        double rounded = Math.Round(power);
        return Math.Abs(power - rounded) < 1e-9 ? (int)rounded : (int?)null;
    }

    /// <summary>
    /// Подпись числа на логарифмической шкале: 0,01; 1; 1000 — десятичной записью,
    /// очень малые и очень большие степени десяти — как 10 в степени (в записи <see cref="Scene.TextMarkup"/>: <c>10^{6}</c>).
    /// </summary>
    public static string Label(double value, bool decimalComma)
    {
        if (!(value > 0)) return NumberFormatter.Format(value, 0, decimalComma);

        int exponent = (int)Math.Floor(Math.Log10(value) + 1e-9);
        double mantissa = Math.Round(value / NiceScale.Pow10(exponent), 6);
        if (exponent >= -3 && exponent <= 4)
        {
            int decimals = Math.Max(0, -exponent);
            return NumberFormatter.Format(value, decimals, decimalComma);
        }

        string power = "10^{" + (exponent < 0 ? "−" + (-exponent).ToString(CultureInfo.InvariantCulture) : exponent.ToString(CultureInfo.InvariantCulture)) + "}";
        if (Math.Abs(mantissa - 1) < 1e-9) return power;
        return NumberFormatter.Format(mantissa, mantissa == Math.Round(mantissa) ? 0 : 1, decimalComma) + "·" + power;
    }

    private static double Snap(double value)
    {
        // 3·0,001 = 0,0030000000000000001 → 0,003
        int exponent = (int)Math.Floor(Math.Log10(value));
        double unit = NiceScale.Pow10(exponent - 9);
        return Math.Round(value / unit) * unit;
    }
}
