using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Grapher.Core.Build;
using Grapher.Core.Geometry;
using Grapher.Core.Import;
using Grapher.Core.Model;
using Grapher.Core.Parsing;
using Grapher.Core.Serialization;
using Grapher.Kompas;
using Grapher.UI;
using Grapher.UI.Rendering;

namespace Grapher.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => ShowUnexpectedError(e.Exception);

        try
        {
            if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
            {
                return RunCommand(args);
            }

            using (var form = new MainForm())
            {
                if (args.Length > 0 && File.Exists(args[0]))
                {
                    form.Shown += (s, e) => OpenFile(form, args[0]);
                }
                Application.Run(form);
            }
            return 0;
        }
        catch (Exception ex)
        {
            ShowUnexpectedError(ex);
            return 1;
        }
    }

    /// <summary>Файл проекта открывается как проект, таблица Excel или CSV — через диалог импорта.</summary>
    private static void OpenFile(MainForm form, string path)
    {
        string name = path.ToLowerInvariant();
        if (name.EndsWith(".json", StringComparison.Ordinal)) form.OpenProjectFile(path);
        else form.ImportFromFile(path);
    }

    private static void ShowUnexpectedError(Exception ex)
    {
        MessageBox.Show("Непредвиденная ошибка:\n\n" + ex.Message + "\n\n" + ex.GetType().FullName + "\n" + ex.StackTrace,
            "Grapher", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    // ------------------------------------------------------------------ служебные команды

    /// <summary>
    /// Команды без окна — для проверки и автоматизации:
    ///   --draw &lt;проект|sample&gt; [--x N] [--y N] [--angle N] [--new-fragment|--new-drawing] [--verify] [--png файл [--color]]
    ///   --preview-png &lt;проект|sample&gt; &lt;файл.png&gt; [ширина высота]
    ///   --window-png &lt;файл.png&gt; [проект|sample] [номер вкладки]
    ///   --make-samples &lt;папка&gt;
    ///   --ui-smoke &lt;папка снимков&gt; &lt;папка примеров&gt;   проверка вставки и импорта в окне приложения
    ///   --gui-build &lt;проект|sample&gt; [--new-drawing] [--png файл]   «Построить в КОМПАС» через окно приложения
    ///   --pick            запросить точку в окне КОМПАС
    ///   --stop-process    прервать интерактивную команду КОМПАС
    /// Ход выполнения пишется в файл журнала, если указан ключ --log файл.
    /// </summary>
    private static int RunCommand(string[] args)
    {
        var log = new List<string>();
        string logPath = Option(args, "--log");
        int code;
        try
        {
            switch (args[0])
            {
                case "--draw":
                    code = Draw(args, log);
                    break;
                case "--preview-png":
                    code = PreviewPng(args, log);
                    break;
                case "--window-png":
                    code = WindowPng(args, log);
                    break;
                case "--make-samples":
                    code = MakeSamples(args, log);
                    break;
                case "--ui-smoke":
                    code = UiSmoke(args, log);
                    break;
                case "--gui-build":
                    code = GuiBuild(args, log);
                    break;
                case "--import-check":
                    code = ImportCheck(args, log);
                    break;
                case "--pick":
                    code = Pick(log);
                    break;
                case "--stop-process":
                    code = StopProcess(log);
                    break;
                default:
                    log.Add("Неизвестная команда: " + args[0]);
                    code = 2;
                    break;
            }
        }
        catch (Exception ex)
        {
            log.Add("ОШИБКА: " + ex.Message);
            log.Add(ex.ToString());
            code = 1;
        }

        if (logPath != null)
        {
            File.WriteAllLines(logPath, log);
        }
        return code;
    }

    private static string Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static double NumberOption(string[] args, string name, double fallback)
    {
        string text = Option(args, name);
        return text != null && NumberParser.TryParse(text, out double value) ? value : fallback;
    }

    private static GraphProject LoadProject(string source)
    {
        return string.Equals(source, "sample", StringComparison.OrdinalIgnoreCase)
            ? SampleData.SqrtProject()
            : ProjectSerializer.Load(source);
    }

    private static int Draw(string[] args, List<string> log)
    {
        GraphProject project = LoadProject(args[1]);
        BuildResult result = GraphBuilder.Build(project);
        foreach (DataIssue issue in result.Issues) log.Add(issue.Severity + ": " + issue.Message);
        if (result.HasErrors) return 3;

        KompasSession session = KompasSession.TryAttach();
        if (session == null)
        {
            log.Add("КОМПАС-3D не запущен.");
            return 4;
        }
        log.Add(session.VersionText);

        if (Array.IndexOf(args, "--new-fragment") >= 0) session.CreateDocument(true);
        if (Array.IndexOf(args, "--new-drawing") >= 0) session.CreateDocument(false);

        // Новый вид с заданным масштабом: проверка, что размер графика на бумаге не зависит от масштаба вида.
        double viewScale = NumberOption(args, "--view-scale", 0);
        if (viewScale > 0) session.CreateView("График", viewScale, 30, 60);

        double x = NumberOption(args, "--x", project.Placement.X);
        double y = NumberOption(args, "--y", project.Placement.Y);
        double angle = NumberOption(args, "--angle", project.Placement.AngleDeg);

        DateTime started = DateTime.Now;
        RenderResult rendered = KompasRenderer.Render(session, result.Scene, x, y, angle, "График");
        log.Add(string.Format(CultureInfo.InvariantCulture,
            "document={0}; objects={1}; grouped={2}; viewScale={3}; time={4:0.00}s",
            rendered.DocumentName, rendered.ObjectCount, rendered.Grouped, rendered.ViewScale, (DateTime.Now - started).TotalSeconds));
        log.AddRange(rendered.Warnings);

        int code = 0;
        if (Array.IndexOf(args, "--verify") >= 0)
        {
            // Читаем объекты обратно из КОМПАС и сравниваем с примитивами, из которых строится и предпросмотр.
            VerificationReport report = KompasVerifier.CompareLastMacro(session, result.Scene, x, y, angle);
            log.Add("verify: " + report);
            log.AddRange(report.Mismatches);
            if (!report.Ok) code = 5;
        }

        string png = Option(args, "--png");
        if (png != null)
        {
            if (File.Exists(png)) File.Delete(png);
            // --color: снимок в цветах объектов (иначе чёрно-белый, как на печати).
            log.Add("png=" + session.ExportActiveDocumentToPng(png, 150, Array.IndexOf(args, "--color") >= 0));
        }
        return code;
    }

    /// <summary>
    /// Сквозная проверка главного сценария: открывает окно приложения, загружает проект и выполняет
    /// «Построить в КОМПАС» так же, как по нажатию кнопки (с запросом точки курсором, если он включён).
    /// После построения сверяет объекты чертежа со сценой.
    /// </summary>
    private static int GuiBuild(string[] args, List<string> log)
    {
        GraphProject project = LoadProject(args[1]);
        if (Array.IndexOf(args, "--new-drawing") >= 0)
        {
            KompasSession.TryAttach()?.CreateDocument(false);
        }

        int code = 0;
        using (var form = new MainForm { SaveSettingsOnClose = false })
        {
            form.Shown += (s, e) => form.BeginInvoke((Action)(() =>
            {
                form.LoadProject(project);
                form.BuildNow();
                Application.DoEvents();
                log.Add("status: " + form.StatusText);
                log.Add("windowState: " + form.WindowState + "; visible=" + form.Visible + "; active=" + (Form.ActiveForm == form));
                log.Add(string.Format(CultureInfo.InvariantCulture, "placement: x={0:0.###}; y={1:0.###}; angle={2}",
                    form.Project.Placement.X, form.Project.Placement.Y, form.Project.Placement.AngleDeg));

                KompasSession session = KompasSession.TryAttach();
                if (session != null && form.StatusText.StartsWith("График построен", StringComparison.Ordinal))
                {
                    BuildResult result = form.Rebuild();
                    VerificationReport report = KompasVerifier.CompareLastMacro(session, result.Scene,
                        form.Project.Placement.X, form.Project.Placement.Y, form.Project.Placement.AngleDeg);
                    log.Add("verify: " + report);
                    log.AddRange(report.Mismatches);
                    if (!report.Ok) code = 5;

                    string png = Option(args, "--png");
                    if (png != null)
                    {
                        if (File.Exists(png)) File.Delete(png);
                        log.Add("png=" + session.ExportActiveDocumentToPng(png, 150));
                    }
                }
                else
                {
                    code = 6;
                }
                form.Close();
            }));
            Application.Run(form);
        }
        return code;
    }

    /// <summary>Читает таблицу из файла Excel или CSV и строит по ней график (без окна): проверка импорта.</summary>
    private static int ImportCheck(string[] args, List<string> log)
    {
        string path = args[1];
        List<SheetData> sheets = SpreadsheetImporter.IsExcelFile(path)
            ? SpreadsheetImporter.ReadWorkbook(path)
            : new List<SheetData> { SpreadsheetImporter.ReadDelimitedFile(path) };

        foreach (SheetData sheet in sheets)
        {
            log.Add(string.Format(CultureInfo.InvariantCulture, "sheet «{0}»: rows={1}; columns={2}", sheet.Name, sheet.Rows.Count, sheet.ColumnCount));
        }

        ImportedTable table = ImportedTable.FromCells(sheets[0].GetRange(null));
        var project = new GraphProject();
        table.ApplyTo(project);
        BuildResult result = GraphBuilder.Build(project);
        log.Add(string.Format(CultureInfo.InvariantCulture, "table: x=«{0}»; y=«{1}»; rows={2}; first={3}; last={4}",
            table.XName, string.Join("», «", table.YNames), table.Rows.Count,
            string.Join(" | ", table.Rows[0]), string.Join(" | ", table.Rows[table.Rows.Count - 1])));
        log.Add(string.Format(CultureInfo.InvariantCulture, "graph: X={0}..{1} step {6}; Y={2}..{3} step {7}; points={4}; vertices={8}; errors={5}",
            result.XMin, result.XMax, result.YMin, result.YMax, result.SourcePointCount, result.HasErrors,
            result.TicksX.Step, result.TicksY.Step, result.CurveVertexCount));
        foreach (DataIssue issue in result.Issues) log.Add(issue.Severity + ": " + issue.Message);

        string png = Option(args, "--png");
        if (png != null)
        {
            using (Bitmap bitmap = new PreviewRenderer { ShowInsertionPoint = false }.RenderToBitmap(result.Scene, 1100, 800))
            {
                bitmap.Save(png, ImageFormat.Png);
            }
        }

        // --save: сохранить импортированные данные как проект графика.
        string save = Option(args, "--save");
        if (save != null) ProjectSerializer.Save(project, save);
        return result.HasErrors ? 3 : 0;
    }

    /// <summary>Запрашивает точку в окне КОМПАС и пишет её координаты в журнал.</summary>
    private static int Pick(List<string> log)
    {
        KompasSession session = KompasSession.TryAttach();
        if (session == null)
        {
            log.Add("КОМПАС-3D не запущен.");
            return 4;
        }

        session.Activate();
        bool picked = session.PickPoint("Укажите точку вставки графика", out double x, out double y);
        log.Add(string.Format(CultureInfo.InvariantCulture, "picked={0}; x={1:0.###}; y={2:0.###}", picked, x, y));
        return picked ? 0 : 6;
    }

    /// <summary>Сообщает, выполняется ли в КОМПАС интерактивная команда, и прерывает её.</summary>
    private static int StopProcess(List<string> log)
    {
        KompasSession session = KompasSession.TryAttach();
        if (session == null)
        {
            log.Add("КОМПАС-3D не запущен.");
            return 4;
        }

        bool running = session.IsProcessRunning();
        log.Add("processRunning=" + running);
        session.StopCurrentProcess();
        return running ? 0 : 7;
    }

    private static int PreviewPng(string[] args, List<string> log)
    {
        GraphProject project = LoadProject(args[1]);
        BuildResult result = GraphBuilder.Build(project);
        foreach (DataIssue issue in result.Issues) log.Add(issue.Severity + ": " + issue.Message);

        int width = args.Length > 4 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 900;
        int height = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 900;
        var renderer = new PreviewRenderer { ShowInsertionPoint = false };
        using (Bitmap bitmap = renderer.RenderToBitmap(result.Scene.Transform(new Transform2D(0, 0, project.Placement.AngleDeg)), width, height))
        {
            bitmap.Save(args[2], ImageFormat.Png);
        }
        log.Add(string.Format(CultureInfo.InvariantCulture, "primitives={0}; scale={1:0.###} px/mm", result.Scene.Primitives.Count, renderer.LastScale));
        return 0;
    }

    /// <summary>Снимок главного окна в файл: проверка раскладки без ручного запуска.</summary>
    private static int WindowPng(string[] args, List<string> log)
    {
        string output = args[1];
        // Снимок не должен менять сохранённые настройки пользователя.
        using (var form = new MainForm { SaveSettingsOnClose = false })
        {
            form.Shown += (s, e) =>
            {
                if (args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal))
                {
                    form.LoadProject(LoadProject(args[2]));
                }
                if (args.Length > 3 && int.TryParse(args[3], out int tab))
                {
                    form.SelectSettingsTab(tab);
                }

                var timer = new System.Windows.Forms.Timer { Interval = 700 };
                timer.Tick += (s2, e2) =>
                {
                    timer.Stop();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                        bitmap.Save(output, ImageFormat.Png);
                    }
                    log.Add("window=" + form.Width + "x" + form.Height + "; dpi=" + form.DeviceDpi);
                    form.Close();
                };
                timer.Start();
            };
            Application.Run(form);
        }
        return 0;
    }

    /// <summary>
    /// Проверка интерфейса без участия человека: вставка текста «как из буфера обмена» (табуляция, десятичная запятая),
    /// импорт из книги Excel и из CSV; для каждого источника — сравнение с контрольным примером.
    /// Дополнительно сохраняются снимки окна и диалога импорта.
    /// </summary>
    private static int UiSmoke(string[] args, List<string> log)
    {
        string folder = args[1];
        string samples = args[2];
        Directory.CreateDirectory(folder);
        int failures = 0;

        BuildResult reference = GraphBuilder.Build(SampleData.SqrtProject());
        int referencePrimitives = reference.Scene.Primitives.Count;

        using (var form = new MainForm { SaveSettingsOnClose = false })
        {
            form.Show();
            Application.DoEvents();

            void Check(string name)
            {
                // Оформление контрольного примера: поле 100×100 мм, сетка 10 мм.
                form.Project.Graph.GridMode = GridStepMode.Millimeters;
                form.Project.XAxis.GridStep = 10;
                form.Project.YAxis.GridStep = 10;
                form.Project.Graph.WidthMm = 100;
                form.Project.Graph.HeightMm = 100;
                BuildResult result = form.Rebuild();
                Application.DoEvents();

                int curves = 0, nodes = 0;
                foreach (var primitive in result.Scene.Primitives)
                {
                    if (primitive is Grapher.Core.Scene.BezierPrimitive bezier)
                    {
                        curves++;
                        nodes += bezier.Nodes.Count;
                    }
                }
                bool ok = !result.HasErrors && form.Project.Data.Rows.Count == 100 && curves == 1 && nodes == 100 &&
                          result.XMax == 100 && result.YMax == 10 && result.TicksX.Values.Count == 11 && result.TicksY.Values.Count == 11;
                if (!ok) failures++;
                log.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0}: {1}; rows={2}; curves={3}; nodes={4}; X={5}..{6}; Y={7}..{8}; primitives={9} (образец {10}); issues={11}",
                    name, ok ? "OK" : "FAIL", form.Project.Data.Rows.Count, curves, nodes, result.XMin, result.XMax, result.YMin, result.YMax,
                    result.Scene.Primitives.Count, referencePrimitives, result.Issues.Count));

                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(Path.Combine(folder, "smoke_" + name + ".png"), ImageFormat.Png);
                }
            }

            form.PasteTableText(File.ReadAllText(Path.Combine(samples, "sqrt.txt")));
            Check("clipboard");

            SheetData sheet = SpreadsheetImporter.ReadWorkbook(Path.Combine(samples, "sqrt.xlsx"))[0];
            form.LoadTable(ImportedTable.FromCells(sheet.GetRange(null)));
            Check("xlsx");

            SheetData csv = SpreadsheetImporter.ReadDelimitedFile(Path.Combine(samples, "sqrt.csv"));
            form.LoadTable(ImportedTable.FromCells(csv.GetRange(null)));
            Check("csv");

            // Ошибка в данных: должна появиться в списке замечаний с номером строки.
            form.PasteTableText("x\ty\r\n0\t0\r\n1\tabc\r\n2\t4\r\n3\t\r\n4\t16\r\n");
            BuildResult broken = form.Rebuild();
            Application.DoEvents();
            bool reported = broken.HasErrors && broken.Issues.Exists(i => i.Row == 2 && i.Message.Contains("abc"));
            if (!reported) failures++;
            log.Add("invalid cell: " + (reported ? "OK" : "FAIL") + "; " + string.Join(" / ", broken.Issues.ConvertAll(i => i.Message)));
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(Path.Combine(folder, "smoke_invalid.png"), ImageFormat.Png);
            }
        }

        using (Bitmap dialog = ImportDialog.CaptureImage(Path.Combine(samples, "sqrt.xlsx")))
        {
            dialog.Save(Path.Combine(folder, "smoke_import_xlsx.png"), ImageFormat.Png);
        }
        using (Bitmap dialog = ImportDialog.CaptureImage(Path.Combine(samples, "sqrt.csv")))
        {
            dialog.Save(Path.Combine(folder, "smoke_import_csv.png"), ImageFormat.Png);
        }

        log.Add("failures=" + failures);
        return failures == 0 ? 0 : 8;
    }

    /// <summary>Создаёт файлы контрольного примера: текст для буфера обмена, CSV, книгу Excel и проект.</summary>
    private static int MakeSamples(string[] args, List<string> log)
    {
        string folder = args[1];
        Directory.CreateDirectory(folder);
        List<string[]> rows = SampleData.SqrtRows();

        // Текст с табуляцией и десятичной запятой — в таком виде данные приходят из Mathcad и русского Excel.
        var tabbed = new List<string>();
        var csv = new List<string> { "x;y" };
        foreach (string[] row in rows)
        {
            string x = row[0].Replace('.', ','), y = row[1].Replace('.', ',');
            tabbed.Add(x + "\t" + y);
            csv.Add(x + ";" + y);
        }
        File.WriteAllLines(Path.Combine(folder, "sqrt.txt"), tabbed, new System.Text.UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(folder, "sqrt.csv"), csv, new System.Text.UTF8Encoding(true));

        var sheet = new List<string[]> { new[] { "x", "y" } };
        sheet.AddRange(rows);
        SimpleXlsxWriter.Write(Path.Combine(folder, "sqrt.xlsx"), "Данные", sheet);

        ProjectSerializer.Save(SampleData.SqrtProject(), Path.Combine(folder, "sqrt" + ProjectSerializer.FileExtension));
        log.Add("Созданы файлы примера в папке " + Path.GetFullPath(folder));
        return 0;
    }
}
