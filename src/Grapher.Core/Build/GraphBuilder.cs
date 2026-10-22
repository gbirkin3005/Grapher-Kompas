using System;
using System.Collections.Generic;
using Grapher.Core.Geometry;
using Grapher.Core.Model;
using Grapher.Core.Parsing;
using Grapher.Core.Scene;
using Grapher.Core.Serialization;

namespace Grapher.Core.Build;

/// <summary>
/// Превращает проект графика (данные и настройки) в список абстрактных примитивов.
/// Здесь сосредоточена вся геометрия; рендереры только переносят примитивы на чертёж или экран.
/// Оформление следует рекомендациям Р 50-77-88 «ЕСКД. Правила выполнения диаграмм».
/// </summary>
public static class GraphBuilder
{
    public static BuildResult Build(GraphProject project)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        ProjectSerializer.Normalize(project);

        var result = new BuildResult { Kind = project.Graph.Kind };

        ValidatedData data = DataValidator.Validate(project.Data, project.Curves);
        result.Issues.AddRange(data.Issues);
        foreach (SeriesPoints series in data.Series)
        {
            result.SourcePointCount += series.Points.Count;
        }

        var context = new BuildContext(project, result, data);
        switch (project.Graph.Kind)
        {
            case DiagramKind.Polar:
                PolarBuilder.Build(context);
                break;
            case DiagramKind.Ternary:
                TernaryBuilder.Build(context);
                break;
            default:
                CartesianBuilder.Build(context);
                break;
        }

        if (project.Graph.BreakGridUnderLabels)
        {
            SceneTools.BreakGridUnderBoxes(context.Scene, context.KeepOut);
        }

        result.Scene = context.Scene;
        return result;
    }
}

/// <summary>Состояние построения, общее для всех видов диаграмм.</summary>
internal sealed class BuildContext
{
    public BuildContext(GraphProject project, BuildResult result, ValidatedData data)
    {
        Project = project;
        Result = result;
        Data = data;
    }

    public GraphProject Project { get; }
    public BuildResult Result { get; }
    public ValidatedData Data { get; }
    public GraphScene Scene { get; } = new GraphScene();

    /// <summary>Надписи на поле диаграммы: под ними прерываются линии сетки.</summary>
    public List<RectD> KeepOut { get; } = new List<RectD>();

    public GraphSettings G => Project.Graph;

    public void Warn(string message) => Result.Issues.Add(new DataIssue(IssueSeverity.Warning, message));

    public void Error(string message) => Result.Issues.Add(new DataIssue(IssueSeverity.Error, message));
}
