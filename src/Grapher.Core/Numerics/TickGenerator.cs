using System;
using System.Collections.Generic;
using Grapher.Core.Model;

namespace Grapher.Core.Numerics;

/// <summary>Линии сетки и засечки одной оси.</summary>
public sealed class AxisTicks
{
    /// <summary>Значения в единицах оси, по возрастанию.</summary>
    public List<double> Values { get; } = new List<double>();

    /// <summary>Шаг в единицах оси.</summary>
    public double Step { get; set; }

    /// <summary>Число знаков после запятой, достаточное для точной записи значений.</summary>
    public int Decimals { get; set; }

    /// <summary>
    /// Линии отсчитываются от нижнего предела оси (режимы «в мм» и «число делений»),
    /// а не от нуля (значения, кратные шагу).
    /// </summary>
    public bool FromMin { get; set; }

    /// <summary>Пояснение, если заданный шаг пришлось заменить автоматическим.</summary>
    public string Warning { get; set; }
}

/// <summary>Расчёт положений линий сетки по одному из способов задания шага.</summary>
public static class TickGenerator
{
    /// <summary>Больше линий по одной оси не строим: это почти наверняка ошибка ввода.</summary>
    public const int MaxLines = 400;

    /// <summary>Целевое расстояние между линиями сетки на листе при автоматическом шаге, мм.</summary>
    public const double AutoSpacingMm = 10;

    /// <summary>Сколько делений подобрать автоматически для оси заданной длины.</summary>
    public static int AutoDivisions(double sizeMm)
    {
        int n = (int)Math.Round(sizeMm / AutoSpacingMm);
        if (n < 2) n = 2;
        if (n > 20) n = 20;
        return n;
    }

    public static AxisTicks Generate(GridStepMode mode, double stepValue, double min, double max, double sizeMm, string axisName)
    {
        var ticks = new AxisTicks();
        double range = max - min;
        if (!(range > 0) || !(sizeMm > 0))
        {
            return ticks;
        }

        switch (mode)
        {
            case GridStepMode.Units:
                if (!(stepValue > 0))
                {
                    ticks.Warning = "Шаг сетки по оси " + axisName + " должен быть больше нуля — шаг подобран автоматически.";
                    break;
                }
                if (range / stepValue > MaxLines)
                {
                    ticks.Warning = "Шаг сетки по оси " + axisName + " слишком мелкий — шаг подобран автоматически.";
                    break;
                }
                ticks.Step = stepValue;
                ticks.Values.AddRange(NiceScale.Multiples(min, max, stepValue, MaxLines + 1));
                break;

            case GridStepMode.Millimeters:
                if (!(stepValue > 0))
                {
                    ticks.Warning = "Шаг сетки по оси " + axisName + " должен быть больше нуля — шаг подобран автоматически.";
                    break;
                }
                if (sizeMm / stepValue > MaxLines)
                {
                    ticks.Warning = "Шаг сетки по оси " + axisName + " слишком мелкий — шаг подобран автоматически.";
                    break;
                }
                FillFromMin(ticks, min, max, stepValue * range / sizeMm);
                break;

            case GridStepMode.Divisions:
                int divisions = (int)Math.Round(stepValue);
                if (divisions < 1)
                {
                    ticks.Warning = "Число делений по оси " + axisName + " должно быть не меньше единицы — шаг подобран автоматически.";
                    break;
                }
                if (divisions > MaxLines)
                {
                    ticks.Warning = "Слишком много делений по оси " + axisName + " — шаг подобран автоматически.";
                    break;
                }
                FillFromMin(ticks, min, max, range / divisions);
                break;
        }

        if (ticks.Values.Count == 0)
        {
            // Автоматический режим либо некорректный ввод.
            double step = NiceScale.AutoStep(min, max, AutoDivisions(sizeMm));
            ticks.Step = step;
            ticks.Values.AddRange(NiceScale.Multiples(min, max, step, MaxLines + 1));
        }

        ticks.Decimals = DecimalsFor(ticks.Values, ticks.Step);
        return ticks;
    }

    /// <summary>Линии от нижнего предела с заданным шагом (режимы «в мм» и «число делений»).</summary>
    private static void FillFromMin(AxisTicks ticks, double min, double max, double step)
    {
        ticks.Step = step;
        ticks.FromMin = true;
        int count = (int)Math.Floor((max - min) / step + 1e-9);
        for (int i = 0; i <= count; i++)
        {
            ticks.Values.Add(NiceScale.Snap(min + i * step, step));
        }
    }

    /// <summary>
    /// Наименьшее число знаков после запятой, при котором все значения записываются точно.
    /// Для «некруглых» шагов (например, 100/3) оставляем три значащие цифры шага.
    /// </summary>
    public static int DecimalsFor(IReadOnlyList<double> values, double step)
    {
        if (!(step > 0)) return 0;
        double tolerance = step * 1e-6;
        int magnitude = (int)Math.Floor(Math.Log10(step));

        // Точную запись ищем в пределах четырёх значащих цифр шага: дальше начинается шум округления.
        int maxExact = Math.Min(6, Math.Max(0, 3 - magnitude));
        for (int d = 0; d <= maxExact; d++)
        {
            bool exact = true;
            foreach (double v in values)
            {
                if (Math.Abs(Math.Round(v, d) - v) > tolerance)
                {
                    exact = false;
                    break;
                }
            }
            if (exact) return d;
        }

        int significant = 2 - magnitude;
        if (significant < 0) significant = 0;
        if (significant > 6) significant = 6;
        return significant;
    }
}
