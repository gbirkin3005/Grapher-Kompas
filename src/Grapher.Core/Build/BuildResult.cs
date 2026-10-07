using System.Collections.Generic;
using Grapher.Core.Numerics;
using Grapher.Core.Parsing;
using Grapher.Core.Scene;

namespace Grapher.Core.Build;

/// <summary>Результат построения графика: сцена, замечания и вычисленные параметры.</summary>
public sealed class BuildResult
{
    /// <summary>Сцена в локальных координатах (начало — левый нижний угол поля графика).</summary>
    public GraphScene Scene { get; set; }

    public List<DataIssue> Issues { get; } = new List<DataIssue>();

    public bool HasErrors
    {
        get
        {
            foreach (DataIssue issue in Issues)
            {
                if (issue.Severity == IssueSeverity.Error) return true;
            }
            return false;
        }
    }

    // Действующие пределы осей (заданные вручную или подобранные по данным).
    public double XMin { get; set; }
    public double XMax { get; set; }
    public double YMin { get; set; }
    public double YMax { get; set; }

    /// <summary>Габариты поля графика на листе, мм.</summary>
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }

    /// <summary>Масштабные коэффициенты: миллиметров на единицу оси.</summary>
    public double ScaleX { get; set; }
    public double ScaleY { get; set; }

    public AxisTicks TicksX { get; set; }
    public AxisTicks TicksY { get; set; }

    /// <summary>Сколько точек данных прочитано из таблицы.</summary>
    public int SourcePointCount { get; set; }

    /// <summary>Сколько вершин кривых попало на чертёж после прореживания и обрезки.</summary>
    public int CurveVertexCount { get; set; }

    /// <summary>Хотя бы одна кривая была прорежена.</summary>
    public bool Decimated { get; set; }
}
