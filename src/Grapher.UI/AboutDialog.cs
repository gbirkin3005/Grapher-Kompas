using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Grapher.UI;

/// <summary>Окно «О программе»: версия, назначение и ссылка на страницу программы на GitHub.</summary>
internal sealed class AboutDialog : Form
{
    /// <summary>Исходный код, новые версии (Releases) и инструкции.</summary>
    public const string RepositoryUrl = "https://github.com/gbirkin3005/Grapher-Kompas";

    public AboutDialog()
    {
        Version version = typeof(AboutDialog).Assembly.GetName().Version;
        Text = "О программе";
        ShowIcon = false;
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 12, 12, 0)
        };

        Icon icon = AppIcon.Get();
        if (icon != null)
        {
            var picture = new PictureBox
            {
                Image = new Icon(icon, 48, 48).ToBitmap(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(48, 48),
                Margin = new Padding(0, 2, 14, 0)
            };
            layout.Controls.Add(picture, 0, 0);
            layout.SetRowSpan(picture, 6);
        }

        Font big = new Font(SystemFonts.MessageBoxFont.FontFamily, SystemFonts.MessageBoxFont.Size * 1.3f, FontStyle.Bold);
        layout.Controls.Add(new Label
        {
            Text = "Grapher",
            AutoSize = true,
            Font = big,
            Margin = new Padding(0, 0, 0, 0)
        }, 1, 0);

        layout.Controls.Add(new Label
        {
            Text = "Версия " + version.ToString(3),
            AutoSize = true,
            Font = big,
            Margin = new Padding(0, 0, 0, 2)
        }, 1, 1);

        layout.Controls.Add(new Label
        {
            Text = BuildInfo(),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 8)
        }, 1, 2);

        layout.Controls.Add(new Label
        {
            Text =
                "Построение графиков по точкам в чертежах и фрагментах КОМПАС-3D.\n" +
                "Данные вводятся в таблицу, вставляются из буфера обмена (Mathcad, Excel)\n" +
                "или импортируются из файлов Excel и CSV.\n\n" +
                "Диаграммы в прямоугольных и полярных координатах и треугольные;\n" +
                "оформление — по рекомендациям Р 50-77-88 «Правила выполнения диаграмм».\n\n" +
                "График рисуется объектами КОМПАС и объединяется в макроэлемент.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        }, 1, 3);

        layout.Controls.Add(new Label
        {
            Text = "Исходный код, новые версии и инструкции:",
            AutoSize = true,
            Margin = Padding.Empty
        }, 1, 4);

        var link = new LinkLabel { Text = RepositoryUrl, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        link.LinkClicked += (s, e) => OpenRepository();
        layout.Controls.Add(link, 1, 5);

        Button ok = Ui.Button("OK");
        ok.DialogResult = DialogResult.OK;
        ok.Anchor = AnchorStyles.Right;
        ok.Margin = new Padding(3, 14, 0, 12);
        layout.Controls.Add(ok, 0, 6);
        layout.SetColumnSpan(ok, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = ok;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>Номер и время сборки: короткий хеш коммита из версии и метка файла.</summary>
    private static string BuildInfo()
    {
        var assembly = typeof(AboutDialog).Assembly;
        string hash = "";
        var attribute = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
            assembly, typeof(AssemblyInformationalVersionAttribute));
        string informational = attribute?.InformationalVersion ?? "";
        int plus = informational.IndexOf('+');
        if (plus >= 0 && plus + 1 < informational.Length)
        {
            hash = informational.Substring(plus + 1, Math.Min(7, informational.Length - plus - 1));
        }
        string time = File.GetLastWriteTime(assembly.Location).ToString("dd.MM.yyyy HH:mm");
        return hash.Length > 0 ? "Сборка " + hash + " от " + time : "Сборка от " + time;
    }

    private void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
        {
            MessageBox.Show(this, "Не удалось открыть браузер: " + ex.Message + "\n\n" + RepositoryUrl,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
