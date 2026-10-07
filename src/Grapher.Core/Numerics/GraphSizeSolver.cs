using System;

namespace Grapher.Core.Numerics;

/// <summary>Какая величина была изменена пользователем последней.</summary>
public enum SizeDriver
{
    Width,
    Height,
    ScaleX,
    ScaleY
}

/// <summary>
/// Связь «габарит на листе ↔ масштабный коэффициент».
/// Коэффициент — это миллиметры на листе, приходящиеся на единицу оси: k = габарит / (max − min).
/// </summary>
public static class GraphSizeSolver
{
    public const double MinSizeMm = 1;
    public const double MaxSizeMm = 5000;

    public static double Coefficient(double sizeMm, double min, double max)
    {
        double range = max - min;
        return range > 0 ? sizeMm / range : 0;
    }

    public static double Size(double coefficient, double min, double max) => coefficient * (max - min);

    /// <summary>
    /// Пересчитывает габариты после изменения одной из величин.
    /// При одинаковом масштабе вторая ось подстраивается под изменённую.
    /// </summary>
    /// <param name="driver">Что изменил пользователь.</param>
    /// <param name="value">Новое значение: габарит в мм либо коэффициент в мм на единицу.</param>
    public static void Solve(SizeDriver driver, double value, double rangeX, double rangeY, bool equalScale,
        ref double widthMm, ref double heightMm)
    {
        if (!(value > 0) || !(rangeX > 0) || !(rangeY > 0)) return;

        switch (driver)
        {
            case SizeDriver.Width:
                widthMm = value;
                if (equalScale) heightMm = value / rangeX * rangeY;
                break;
            case SizeDriver.Height:
                heightMm = value;
                if (equalScale) widthMm = value / rangeY * rangeX;
                break;
            case SizeDriver.ScaleX:
                widthMm = value * rangeX;
                if (equalScale) heightMm = value * rangeY;
                break;
            case SizeDriver.ScaleY:
                heightMm = value * rangeY;
                if (equalScale) widthMm = value * rangeX;
                break;
        }
    }

    /// <summary>Высота поля графика, при которой масштаб по Y равен масштабу по X.</summary>
    public static double HeightForEqualScale(double widthMm, double rangeX, double rangeY) =>
        rangeX > 0 ? widthMm / rangeX * rangeY : widthMm;

    public static double Clamp(double sizeMm)
    {
        if (double.IsNaN(sizeMm)) return 100;
        return Math.Max(MinSizeMm, Math.Min(MaxSizeMm, sizeMm));
    }
}
