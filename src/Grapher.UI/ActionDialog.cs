using System;
using System.Drawing;
using System.Windows.Forms;

namespace Grapher.UI;

/// <summary>
/// Диалог выбора из двух-трёх действий с заданными названиями кнопок:
/// стандартный MessageBox позволяет лишь «Да»/«Нет»/«Отмена».
/// Возвращает номер нажатой кнопки (0 — первая, 1 — вторая, 2 — отмена).
/// Раскладка строится вручную с учётом DPI: автомасштабирование формы отключено,
/// иначе при масштабе экрана 125–150 % элементы растут, а окно — нет, и кнопки обрезаются.
/// </summary>
internal sealed class ActionDialog : Form
{
    private ActionDialog(string text, string button1, string button2, string button3)
    {
        Text = "Grapher";
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.None;

        float dpi;
        using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) dpi = graphics.DpiX / 96f;
        int S(double value) => (int)Math.Round(value * dpi);

        ClientSize = new Size(S(520), S(230));

        var iconBox = new PictureBox
        {
            Location = new Point(S(18), S(18)),
            Size = new Size(S(32), S(32)),
            Image = SystemIcons.Question.ToBitmap()
        };

        var message = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(S(430), 0),
            Location = new Point(S(64), S(22))
        };

        var first = new Button { Text = button1 };
        first.Click += (s, e) => { Tag = 0; DialogResult = DialogResult.OK; };
        var second = new Button { Text = button2 };
        second.Click += (s, e) => { Tag = 1; DialogResult = DialogResult.OK; };
        var cancel = new Button { Text = button3, DialogResult = DialogResult.Cancel };

        int width = 0;
        foreach (string title in new[] { button1, button2, button3 })
        {
            width = Math.Max(width, TextRenderer.MeasureText(title, Font).Width);
        }
        width += S(24);
        int gap = S(8);
        int total = width * 3 + gap * 2;
        int x = ClientSize.Width - S(18) - total;
        first.Size = second.Size = cancel.Size = new Size(width, S(32));
        first.Location = new Point(x, ClientSize.Height - S(18) - S(32));
        second.Location = new Point(x + width + gap, first.Location.Y);
        cancel.Location = new Point(x + (width + gap) * 2, first.Location.Y);

        Controls.Add(iconBox);
        Controls.Add(message);
        Controls.Add(first);
        Controls.Add(second);
        Controls.Add(cancel);
        AcceptButton = first;
        CancelButton = cancel;
    }

    /// <summary>Показывает диалог; возвращает 0, 1 или 2 (отмена — кнопкой, Escape или закрытием окна).</summary>
    public static int Show(IWin32Window owner, string text, string button1, string button2, string button3)
    {
        using (var dialog = new ActionDialog(text, button1, button2, button3))
        {
            return dialog.ShowDialog(owner) == DialogResult.OK ? (int)dialog.Tag : 2;
        }
    }
}
