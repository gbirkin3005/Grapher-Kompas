using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
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
            layout.SetRowSpan(picture, 4);
        }

        layout.Controls.Add(new Label
        {
            Text = "Grapher " + version.ToString(3),
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, SystemFonts.MessageBoxFont.Size * 1.3f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        }, 1, 0);

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
        }, 1, 1);

        layout.Controls.Add(new Label
        {
            Text = "Исходный код, новые версии и инструкции:",
            AutoSize = true,
            Margin = Padding.Empty
        }, 1, 2);

        var link = new LinkLabel { Text = RepositoryUrl, AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        link.LinkClicked += (s, e) => OpenRepository();
        layout.Controls.Add(link, 1, 3);

        Button ok = Ui.Button("OK");
        ok.DialogResult = DialogResult.OK;
        ok.Anchor = AnchorStyles.Right;
        ok.Margin = new Padding(3, 14, 0, 12);
        layout.Controls.Add(ok, 0, 4);
        layout.SetColumnSpan(ok, 2);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = ok;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
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
