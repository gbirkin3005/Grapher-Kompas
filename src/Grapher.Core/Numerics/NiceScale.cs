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

    // ------------------------------------------------------------------ углы в долях π

    /// <summary>Шаги в долях π: π/12, π/6, π/4, π/3, π/2, π и кратные π.</summary>
    private static readonly double[] PiSteps = { 1.0 / 12, 1.0 / 6, 1.0 / 4, 1.0 / 3, 1.0 / 2, 1, 2, 3, 4, 5, 6, 10, 12 };

    /// <summary>Допуск «попадания на шаг» для углов: данные обычно записаны с округлением (6,283 вместо 2π).</summary>
    private const double PiFuzz = 0.01;

    /// <summary>Шаг сетки для оси углов в радианах, кратный удобной доле π.</summary>
    public static double AutoStepPi(double min, double max, int targetCount)
    {
        double range = (max - min) / Math.PI;
        if (!(range > 0) || double.IsInfinity(range)) return Math.PI;
        if (targetCount < 1) targetCount = 1;

        double best = 0, bestScore = double.MaxValue;
        double maxCount = Math.Max(2, targetCount * 1.3);
        foreach (double step in PiSteps)
        {
            double count = range / step;
            if (count < 1 - Fuzz || count > maxCount + Fuzz) continue;

            bool exact = IsNear(min / Math.PI / step) && IsNear(max / Math.PI / step);
            double score = Math.Abs(count - targetCount) + (exact ? 0 : 1000);
            if (score < bestScore)
            {
                bestScore = score;
                best = step;
            }
        }

        // Очень большой диапазон: обычный «красивый» шаг в единицах π.
        if (!(best > 0)) best = NiceStep(range, targetCount);
        return best * Math.PI;
    }

    /// <summary>Пределы оси углов в радианах, округлённые наружу до долей π.</summary>
    public static void NiceLimitsPi(double dataMin, double dataMax, int targetCount, out double min, out double max, out double step)
    {
        if (double.IsNaN(dataMin) || double.IsNaN(dataMax) || double.IsInfinity(dataMin) || double.IsInfinity(dataMax) || !(dataMax > dataMin))
        {
            min = 0;
            max = 2 * Math.PI;
            step = Math.PI / 6;
            return;
        }

        step = AutoStepPi(dataMin, dataMax, targetCount);
        min = Math.Floor(dataMin / step + PiFuzz) * step;
        max = Math.Ceiling(dataMax / step - PiFuzz) * step;
        if (!(max > min)) max = min + step;

        // После округления пределов шаг мог стать неточным (0…2π вместо 0…6,28): подбираем заново.
        step = AutoStepPi(min, max, targetCount);
    }

    private static bool IsNear(double quotient) => Math.Abs(quotient - Math.Round(quotient)) <= PiFuzz;

    private static readonly int[] PiSnapDenominators = { 1, 2, 3, 4, 6, 12 };

    /// <summary>
    /// Угол, записанный с округлением (0,5236 или 6,28), заменяет точной долей π, если отличается от неё
    /// меньше чем на 0,1 %. Иначе возвращает значение без изменений.
    /// </summary>
    public static double SnapToPi(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value == 0) return value;

        double q = value / Math.PI;
        foreach (int d in PiSnapDenominators)
        {
            double scaled = q * d, rounded = Math.Round(scaled);
            if (rounded != 0 && Math.Abs(scaled - rounded) <= 1e-3 * Math.Abs(rounded)) return rounded / d * Math.PI;
        }
        return value;
    }

    // ------------------------------------------------------------------ дополнительная шкала

    private static readonly double[] AlignedMantissas = { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8 };

    /// <summary>
    /// Пределы дополнительной шкалы, при которых её деления совпадают с линиями сетки основной шкалы:
    /// диапазон делится на то же число интервалов «красивым» шагом. Если у основной шкалы есть нулевая линия,
    /// ноль дополнительной шкалы по возможности ставится на неё.
    /// </summary>
    /// <param name="intervals">Число интервалов сетки основной шкалы.</param>
    /// <param name="zeroIndex">Номер линии сетки, на которой у основной шкалы ноль; −1 — нуля нет.</param>
    /// <returns>false, если подобрать пределы не удалось.</returns>
    public static bool AlignedLimits(double dataMin, double dataMax, int intervals, int zeroIndex,
        out double min, out double max, out double step)
    {
        min = max = step = 0;
        if (intervals < 1 || double.IsNaN(dataMin) || double.IsNaN(dataMax) || double.IsInfinity(dataMin) || double.IsInfinity(dataMax))
        {
            return false;
        }
        if (dataMin > dataMax) (dataMin, dataMax) = (dataMax, dataMin);
        if (dataMax - dataMin <= Math.Max(Math.Abs(dataMin), Math.Abs(dataMax)) * 1e-12)
        {
            double pad = dataMin == 0 ? 1 : Math.Abs(dataMin) * 0.1;
            dataMin -= pad;
            dataMax += pad;
        }

        // Нули шкал совмещаем, когда это естественно: данные сами проходят через ноль
        // либо ноль основной шкалы стоит на краю поля.
        bool wantZero = zeroIndex >= 0 &&
                        ((dataMin < 0 && dataMax > 0) || zeroIndex == 0 || zeroIndex == intervals);

        double raw = (dataMax - dataMin) / intervals;
        int exponent = (int)Math.Floor(Math.Log10(raw)) - 1;

        double fallbackStep = 0;
        long fallbackFirst = 0;

        for (int k = exponent; k <= exponent + 4; k++)
        {
            double pow = Pow10(k);
            foreach (double mantissa in AlignedMantissas)
            {
                double candidate = mantissa * pow;
                // Номера первой линии, при которых диапазон данных помещается в «intervals» шагов.
                long highest = (long)Math.Floor(dataMin / candidate + Fuzz);
                long lowest = (long)Math.Ceiling(dataMax / candidate - Fuzz) - intervals;
                if (lowest > highest) continue;

                if (fallbackStep == 0)
                {
                    fallbackStep = candidate;
                    // Данные по центру шкалы; неотрицательные данные не опускаем ниже нуля.
                    fallbackFirst = (long)Math.Floor((lowest + highest) / 2.0 + 0.5);
                    if (dataMin >= 0 && fallbackFirst < 0) fallbackFirst = Math.Min(0, highest);
                }

                // Ради совмещения нулей шаг увеличиваем не более чем в 2,5 раза: иначе кривая сожмётся.
                if (!wantZero || candidate > fallbackStep * 2.5 + Fuzz) goto done;

                if (-zeroIndex >= lowest && -zeroIndex <= highest)
                {
                    step = candidate;
                    min = Snap(-zeroIndex * candidate, candidate);
                    max = Snap((intervals - zeroIndex) * candidate, candidate);
                    return true;
                }
            }
        }

        done:
        if (!(fallbackStep > 0)) return false;
        step = fallbackStep;
        min = Snap(fallbackFirst * step, step);
        max = Snap((fallbackFirst + intervals) * step, step);
        return true;
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
