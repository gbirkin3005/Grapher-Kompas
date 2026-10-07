using System;
using System.Collections.Generic;

namespace Grapher.Core.Numerics;

/// <summary>Подбор «красивых» шагов и пределов осей: 1, 2 или 5, умноженные на степень десяти.</summary>
public static class NiceScale
{
    /// <summary>Допуск при сравнении отношения «значение / шаг» с целым числом.</summary>
    private const double Fuzz = 1e-9;

    /// <summary>
    /// «Красивый» шаг, при котором диапазон делится примерно на <paramref name="targetCount"/> частей.
    /// </summary>
    public static double NiceStep(double range, int targetCount)
    {
        if (!(range > 0) || double.IsInfinity(range)) return 1;
        if (targetCount < 1) targetCount = 1;

        double raw = range / targetCount;
        int exponent = (int)Math.Floor(Math.Log10(raw));
        double pow = Pow10(exponent);
        double fraction = raw / pow;

        double nice;
        if (fraction <= 1 + Fuzz) nice = 1;
        else if (fraction <= 2 + Fuzz) nice = 2;
        else if (fraction <= 5 + Fuzz) nice = 5;
        else nice = 10;

        return nice * pow;
    }

    // Множители «точного» шага: 1, 2, 5 — обычные; 3 и 6 дают шаги 30° и 60° для углов 0…360.
    private static readonly double[] ExactMantissas = { 1, 2, 5, 3, 6 };
    private static readonly int[] ExactRanks = { 0, 0, 0, 1, 1 };

    /// <summary>
    /// Шаг сетки для заданных пределов. Если оба предела делятся нацело на удобный шаг
    /// (например, 0…360 на 30), берётся он, и сетка точно ложится на края поля.
    /// Иначе — обычный «красивый» шаг <see cref="NiceStep"/>.
    /// </summary>
    public static double AutoStep(double min, double max, int targetCount)
    {
        double range = max - min;
        if (!(range > 0) || double.IsInfinity(range)) return 1;
        if (targetCount < 1) targetCount = 1;

        double exact = ExactStep(min, max, targetCount);
        return exact > 0 ? exact : NiceStep(range, targetCount);
    }

    /// <summary>
    /// Удобный шаг, на который нацело делятся оба предела, с числом делений около целевого.
    /// Возвращает 0, если такого шага нет.
    /// </summary>
    private static double ExactStep(double min, double max, int targetCount)
    {
        double range = max - min;
        if (!(range > 0) || double.IsInfinity(range)) return 0;

        int exponent = (int)Math.Floor(Math.Log10(range / targetCount));
        double minCount = Math.Max(2, targetCount * 0.5), maxCount = targetCount * 1.3;
        double best = 0, bestScore = double.MaxValue;

        for (int k = exponent - 1; k <= exponent + 1; k++)
        {
            double pow = Pow10(k);
            for (int i = 0; i < ExactMantissas.Length; i++)
            {
                double step = ExactMantissas[i] * pow;
                double count = range / step;
                if (count < minCount - Fuzz || count > maxCount + Fuzz) continue;
                if (!IsMultiple(min, step) || !IsMultiple(max, step)) continue;

                // Сначала обычные шаги (1, 2, 5), затем 3 и 6; внутри группы — ближе к целевому числу делений.
                double score = ExactRanks[i] * 1000 + Math.Abs(count - targetCount);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = step;
                }
            }
        }
        return best;
    }

    private static bool IsMultiple(double value, double step)
    {
        double quotient = value / step;
        return Math.Abs(quotient - Math.Round(quotient)) <= 1e-9 * Math.Max(1, Math.Abs(quotient));
    }

    /// <summary>Степень десяти без накопления ошибки для отрицательных показателей.</summary>
    public static double Pow10(int exponent) =>
        exponent >= 0 ? Math.Pow(10, exponent) : 1.0 / Math.Pow(10, -exponent);

    /// <summary>
    /// Округляет пределы данных наружу до значений, кратных «красивому» шагу.
    /// </summary>
    public static void NiceLimits(double dataMin, double dataMax, int targetCount,
        out double min, out double max, out double step)
    {
        if (double.IsNaN(dataMin) || double.IsNaN(dataMax) || double.IsInfinity(dataMin) || double.IsInfinity(dataMax))
        {
            min = 0;
            max = 10;
            step = 1;
            return;
        }

        if (dataMin > dataMax)
        {
            (dataMin, dataMax) = (dataMax, dataMin);
        }

        if (dataMax - dataMin <= Math.Max(Math.Abs(dataMin), Math.Abs(dataMax)) * 1e-12)
        {
            // Все значения одинаковы: раздвигаем пределы вокруг значения.
            double pad = dataMin == 0 ? 1 : Math.Abs(dataMin) * 0.1;
            dataMin -= pad;
            dataMax += pad;
        }

        // Данные уже лежат в «круглых» границах (0…360, 0…24, −0,25…0,25): пределы не раздвигаем.
        double exact = ExactStep(dataMin, dataMax, targetCount);
        if (exact > 0)
        {
            step = exact;
            min = Snap(dataMin, step);
            max = Snap(dataMax, step);
            return;
        }

        step = NiceStep(dataMax - dataMin, targetCount);
        min = Snap(Math.Floor(dataMin / step + Fuzz) * step, step);
        max = Snap(Math.Ceiling(dataMax / step - Fuzz) * step, step);

        if (!(max > min))
        {
            max = Snap(min + step, step);
        }
    }

    /// <summary>
    /// Значения, кратные шагу, в пределах [min, max]. Концы включаются, если попадают на шаг.
    /// </summary>
    public static List<double> Multiples(double min, double max, double step, int maxCount)
    {
        var values = new List<double>();
        if (!(step > 0) || !(max >= min)) return values;

        double first = Math.Ceiling(min / step - Fuzz);
        double last = Math.Floor(max / step + Fuzz);
        if (last - first + 1 > maxCount) return values;

        for (double i = first; i <= last; i++)
        {
            values.Add(Snap(i * step, step));
        }
        return values;
    }

    /// <summary>Убирает «хвосты» двоичной арифметики вида 0.30000000000000004.</summary>
    public static double Snap(double value, double step)
    {
        if (!(step > 0) || double.IsInfinity(step)) return value;
        int decimals = (int)Math.Ceiling(-Math.Log10(step)) + 6;
        if (decimals < 0) decimals = 0;
        if (decimals > 15) return value;
        double snapped = Math.Round(value, decimals);
        return snapped == 0 ? 0 : snapped; // без «минус нуля»
    }
}
