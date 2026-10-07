using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Grapher.Core.Model;
using Grapher.Core.Parsing;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Grapher.Core.Serialization;

/// <summary>Сохранение и загрузка проекта графика в JSON.</summary>
public static class ProjectSerializer
{
    public const string FileExtension = ".graph.json";
    public const string FileFilter = "Проект графика (*.graph.json)|*.graph.json|Файлы JSON (*.json)|*.json|Все файлы (*.*)|*.*";

    private static JsonSerializerSettings CreateSettings() => new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        Culture = CultureInfo.InvariantCulture,
        NullValueHandling = NullValueHandling.Include,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Converters = { new StringEnumConverter() }
    };

    public static string ToJson(GraphProject project)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        return JsonConvert.SerializeObject(project, CreateSettings());
    }

    public static GraphProject FromJson(string json)
    {
        GraphProject project;
        try
        {
            project = JsonConvert.DeserializeObject<GraphProject>(json, CreateSettings());
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Файл не является проектом графика: " + ex.Message, ex);
        }

        if (project == null)
        {
            throw new InvalidDataException("Файл проекта пуст.");
        }

        Normalize(project);
        return project;
    }

    public static void Save(GraphProject project, string path)
    {
        File.WriteAllText(path, ToJson(project), new UTF8Encoding(false));
    }

    public static GraphProject Load(string path)
    {
        return FromJson(File.ReadAllText(path, Encoding.UTF8));
    }

    /// <summary>Глубокая копия проекта.</summary>
    public static GraphProject Clone(GraphProject project) => FromJson(ToJson(project));

    /// <summary>Копия проекта без данных: только настройки (для «последних настроек»).</summary>
    public static GraphProject CloneSettingsOnly(GraphProject project)
    {
        GraphProject copy = Clone(project);
        copy.Data = new GraphData();
        copy.Curves = new List<CurveSettings>();
        return copy;
    }

    /// <summary>Заполняет пропущенные в файле разделы значениями по умолчанию.</summary>
    public static void Normalize(GraphProject project)
    {
        project.Data ??= new GraphData();
        project.Data.Rows ??= new List<string[]>();
        project.Data.XName ??= "X";
        project.Curves ??= new List<CurveSettings>();
        project.XAxis ??= new AxisSettings();
        project.YAxis ??= new AxisSettings();
        project.Graph ??= new GraphSettings();
        project.Placement ??= new PlacementSettings();
        project.XAxis.Title ??= "";
        project.YAxis.Title ??= "";
        if (string.IsNullOrWhiteSpace(project.Graph.FontName)) project.Graph.FontName = DrawingFonts.Default;

        for (int i = 0; i < project.Curves.Count; i++)
        {
            project.Curves[i] ??= new CurveSettings();
            project.Curves[i].Name ??= "Y" + (i + 1).ToString(CultureInfo.InvariantCulture);
        }

        for (int i = 0; i < project.Data.Rows.Count; i++)
        {
            project.Data.Rows[i] ??= new string[0];
        }
    }
}

/// <summary>
/// Пишет ячейки таблицы числами JSON, если текст ячейки — корректное число,
/// иначе строкой (или null для пустой ячейки). Так файл проекта удобно читать другим программам.
/// </summary>
internal sealed class DataRowsConverter : JsonConverter<List<string[]>>
{
    public override void WriteJson(JsonWriter writer, List<string[]> value, JsonSerializer serializer)
    {
        writer.WriteStartArray();
        if (value != null)
        {
            foreach (string[] row in value)
            {
                Formatting saved = writer.Formatting;
                writer.WriteStartArray();
                // Строка таблицы — одной строкой файла.
                writer.Formatting = Formatting.None;
                if (row != null)
                {
                    foreach (string cell in row)
                    {
                        NumberParseStatus status = NumberParser.Parse(cell, out double number);
                        if (status == NumberParseStatus.Ok) writer.WriteValue(number);
                        else if (status == NumberParseStatus.Empty) writer.WriteNull();
                        else writer.WriteValue(cell);
                    }
                }
                writer.WriteEndArray();
                writer.Formatting = saved;
            }
        }
        writer.WriteEndArray();
    }

    public override List<string[]> ReadJson(JsonReader reader, Type objectType, List<string[]> existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var rows = new List<string[]>();
        if (reader.TokenType == JsonToken.Null) return rows;

        JArray array = JArray.Load(reader);
        foreach (JToken rowToken in array)
        {
            if (rowToken is not JArray rowArray)
            {
                continue;
            }

            var cells = new string[rowArray.Count];
            for (int i = 0; i < rowArray.Count; i++)
            {
                JToken cell = rowArray[i];
                switch (cell.Type)
                {
                    case JTokenType.Float:
                    case JTokenType.Integer:
                        cells[i] = cell.Value<double>().ToString("R", CultureInfo.InvariantCulture);
                        break;
                    case JTokenType.Null:
                    case JTokenType.Undefined:
                        cells[i] = "";
                        break;
                    default:
                        cells[i] = cell.ToString();
                        break;
                }
            }
            rows.Add(cells);
        }
        return rows;
    }
}
