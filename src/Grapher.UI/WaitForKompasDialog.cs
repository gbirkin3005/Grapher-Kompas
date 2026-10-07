using System;
using System.Drawing;
using System.Windows.Forms;
using Grapher.Kompas;

namespace Grapher.UI;

/// <summary>Ожидание запуска КОМПАС: опрашивает таблицу запущенных объектов, пока КОМПАС не ответит.</summary>
internal sealed class WaitForKompasDialog : Form
{
    private readonly Timer _timer = new Timer { Interval = 700 };
    private readonly Label _label = new Label { AutoSize = true, Margin = new Padding(12, 14, 12, 8) };
    private readonly DateTime _started = DateTime.Now;

    public WaitForKompasDialog()
    {
        Text = "Запуск КОМПАС-3D";
        ShowIcon = false;
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        Button cancel = Ui.Button("Отмена");
        cancel.DialogResult = DialogResult.Cancel;
        cancel.Anchor = AnchorStyles.Right;
        cancel.Margin = new Padding(12, 4, 12, 12);
        var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(_label, 0, 0);
        layout.Controls.Add(cancel, 0, 1);
        Controls.Add(layout);
        CancelButton = cancel;

        UpdateText();
        _timer.Tick += OnTick;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>Подключение, полученное после запуска.</summary>
    public KompasSession Session { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _timer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }

    private void OnTick(object sender, EventArgs e)
    {
        UpdateText();
        try
        {
            KompasSession session = KompasSession.TryAttach();
            if (session == null || !session.IsAlive()) return;
            Session = session;
            DialogResult = DialogResult.OK;
        }
        catch (KompasException)
        {
            // КОМПАС ещё загружается и не готов отвечать — ждём дальше.
        }
    }

    private void UpdateText()
    {
        int seconds = (int)(DateTime.Now - _started).TotalSeconds;
        _label.Text = seconds < 60
            ? "КОМПАС-3D запускается, подождите… (" + seconds + " с)"
            : "КОМПАС-3D всё ещё не отвечает (" + seconds + " с).\nЕсли открылось окно с вопросом или лицензией — ответьте в нём.";
    }
}
