using System;
using System.Drawing;
using System.IO;
using Grapher.Core.Model;
using Grapher.Core.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Grapher.UI;

/// <summary>
/// Последние настройки пользователя: оформление графика, папка, положение окна.
/// Хранятся в %AppData%\Grapher\settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Настройки графика без данных.</summary>
    public GraphProject Project { get; set; }

    public string LastDirectory { get; set; }

    /// <summary>Положение и размер окна; Rectangle.Empty — не задано.</summary>
    public Rectangle WindowBounds { get; set; }

    public bool WindowMaximized { get; set; }

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Grapher", "settings.json");

    /// <summary>Файл настроек прежних версий (до переименования программы в Grapher).</summary>
    private static string LegacyFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GrapherKompas", "settings.json");

    /// <summary>Читает настройки; при любой ошибке возвращает настройки по умолчанию.</summary>
    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            // Настройки, сохранённые под старым названием программы, подхватываются один раз.
            string path = File.Exists(FilePath) ? FilePath : LegacyFilePath;
            if (!File.Exists(path)) return settings;

            JObject root = JObject.Parse(File.ReadAllText(path));
            settings.LastDirectory = (string)root["LastDirectory"];
            settings.WindowMaximized = (bool?)root["WindowMaximized"] ?? false;

            if (root["Window"] is JArray w && w.Count == 4)
            {
                settings.WindowBounds = new Rectangle((int)w[0], (int)w[1], (int)w[2], (int)w[3]);
            }
            if (root["Project"] is JObject project)
            {
                settings.Project = ProjectSerializer.CloneSettingsOnly(ProjectSerializer.FromJson(project.ToString()));
            }
        }
        catch (Exception ex) when (ex is IOException || ex is JsonException || ex is InvalidDataException ||
                                   ex is UnauthorizedAccessException || ex is InvalidCastException || ex is FormatException)
        {
            // Испорченный файл настроек не должен мешать запуску.
            return new AppSettings();
        }
        return settings;
    }

    public void Save()
    {
        try
        {
            var root = new JObject
            {
                ["LastDirectory"] = LastDirectory,
                ["WindowMaximized"] = WindowMaximized,
                ["Window"] = new JArray(WindowBounds.X, WindowBounds.Y, WindowBounds.Width, WindowBounds.Height)
            };
            if (Project != null)
            {
                root["Project"] = JObject.Parse(ProjectSerializer.ToJson(ProjectSerializer.CloneSettingsOnly(Project)));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, root.ToString(Formatting.Indented));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            // Настройки не критичны: если записать не удалось, работаем дальше.
        }
    }
}
