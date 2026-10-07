using System;
using System.Collections.Generic;
using System.Globalization;

namespace Grapher.Core.Model;

/// <summary>Тестовые данные.</summary>
public static class SampleData
{
    /// <summary>Точки функции y = √x на отрезке [xMin, xMax], равномерно по X.</summary>
    public static List<string[]> SqrtRows(double xMin = 0, double xMax = 100, int count = 100)
    {
        if (count < 2) throw new ArgumentOutOfRangeException(nameof(count));
        var rows = new List<string[]>(count);
        for (int i = 0; i < count; i++)
        {
            double x = xMin + (xMax - xMin) * i / (count - 1);
            if (i == count - 1) x = xMax;
            double y = Math.Sqrt(x);
            rows.Add(new[]
            {
                Math.Round(x, 6).ToString("0.######", CultureInfo.InvariantCulture),
                Math.Round(y, 6).ToString("0.######", CultureInfo.InvariantCulture)
            });
        }
        return rows;
    }

    /// <summary>
    /// Контрольный пример: y = √x при x от 0 до 100 (100 точек), поле 100×100 мм,
    /// сетка с шагом 10 мм по обеим осям, плавная кривая.
    /// </summary>
    public static GraphProject SqrtProject()
    {
        var project = new GraphProject();
        project.Data.XName = "x";
        project.Data.Rows = SqrtRows();
        project.Curves.Add(new CurveSettings { Name = "√x", LineStyle = LineStyleId.Main, Mode = CurveMode.Smooth });
        project.XAxis.Title = "x";
        project.YAxis.Title = "y";
        project.Graph.WidthMm = 100;
        project.Graph.HeightMm = 100;
        project.Graph.GridMode = GridStepMode.Millimeters;
        project.XAxis.GridStep = 10;
        project.YAxis.GridStep = 10;
        return project;
    }
}
