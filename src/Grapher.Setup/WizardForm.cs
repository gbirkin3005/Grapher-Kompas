using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Grapher.Setup;

/// <summary>Окно мастера установки и удаления: страницы, кнопки «Назад», «Далее», «Отмена».</summary>
internal sealed class WizardForm : Form
{
    private enum Page
    {
        Welcome,
        Options,
        Ready,
        Progress,
        Finish,
        UninstallConfirm
    }

    private readonly Arguments _arguments;
    private readonly bool _uninstallMode;
    private readonly string _uninstallDirectory;
    private readonly ExistingInstall _existing;
    private readonly Dictionary<Page, Control> _pages = new Dictionary<Page, Control>();
    private Page _page;
    private bool _busy;
    private bool _succeeded;

    // Каркас окна
    private Label _title, _subtitle;
    private Button _back, _next, _cancel;
    private Panel _content;

    // «Добро пожаловать»
    private Label _welcomeText, _kompasStatus;
    private GroupBox _existingGroup;
    private Label _existingText;
    private RadioButton _modeUpdate, _modeRemove;

    // «Параметры»
    private RadioButton _forMe, _forAll;
    private TextBox _directory;
    private Button _browse;
    private Label _spaceLabel, _kompasHint;
    private CheckBox _startMenu, _desktop;

    // Версии КОМПАС-3D, к которым подключается Grapher: по флажку на версию (и редакцию).
    private FlowLayoutPanel _kompasList;
    private readonly List<KeyValuePair<CheckBox, KompasProfile>> _kompasChecks = new List<KeyValuePair<CheckBox, KompasProfile>>();

    // «Готово к установке» и удаление
    private TextBox _summary;
    private Label _readyText;
    private CheckBox _detach, _removeSettings;
    private Label _uninstallText;

    // Ход работы и завершение
    private Label _progressAction;
    private ProgressBar _progressBar;
    private TextBox _log;
    private Label _finishTitle, _finishText;
    private CheckBox _launch, _openGuide;

    /// <summary>Код завершения программы (см. <see cref="Program"/>).</summary>
    public int ExitCode { get; private set; } = 3;

    /// <param name="uninstallDirectory">Папка удаляемой программы (в режиме удаления).</param>
    public WizardForm(Arguments arguments, string uninstallDirectory)
    {
        _arguments = arguments;
        _uninstallMode = arguments.Uninstall;
        _uninstallDirectory = uninstallDirectory;
        _existing = ExistingInstall.Find();

        BuildLayout();
        FillDefaults();
        ShowPage(_uninstallMode ? Page.UninstallConfirm : Page.Welcome);
    }

    // ------------------------------------------------------------------ каркас

    private void BuildLayout()
    {
        SuspendLayout();
        Text = _uninstallMode ? "Удаление Grapher" : "Установка Grapher " + Product.Version;
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(660, 540);
        using (Stream icon = typeof(WizardForm).Assembly.GetManifestResourceStream("grapher.ico"))
        {
            if (icon != null) Icon = new Icon(icon);
        }

        // Шапка: заголовок и пояснение страницы, значок программы.
        var header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = SystemColors.Window };
        _title = new Label { AutoSize = true, Location = new Point(18, 12), Font = new Font(Font.FontFamily, Font.Size + 2.5f, FontStyle.Bold) };
        _subtitle = new Label { AutoSize = true, Location = new Point(30, 40), ForeColor = SystemColors.GrayText };
        // Положение задаётся причаливанием, а не привязкой к краю: размер панели ещё не окончательный.
        var logo = new PictureBox
        {
            Dock = DockStyle.Right,
            Width = 72,
            Padding = new Padding(10, 9, 14, 9),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = Icon?.ToBitmap()
        };
        header.Controls.Add(_title);
        header.Controls.Add(_subtitle);
        header.Controls.Add(logo);

        // Кнопки внизу.
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 11, 12, 0)
        };
        _cancel = new Button { Text = "Отмена", Size = new Size(96, 29), Margin = new Padding(8, 0, 0, 0) };
        _next = new Button { Text = "Далее >", Size = new Size(116, 29), Margin = new Padding(8, 0, 0, 0) };
        _back = new Button { Text = "< Назад", Size = new Size(96, 29), Margin = new Padding(8, 0, 0, 0) };
        footer.Controls.Add(_cancel);
        footer.Controls.Add(_next);
        footer.Controls.Add(_back);
        _back.Click += (s, e) => GoBack();
        _next.Click += (s, e) => GoNext();
        _cancel.Click += (s, e) => Close();
        AcceptButton = _next;

        _content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 24, 8) };

        Controls.Add(_content);
        Controls.Add(Line(DockStyle.Top));
        Controls.Add(header);
        Controls.Add(Line(DockStyle.Bottom));
        Controls.Add(footer);

        BuildWelcome();
        BuildOptions();
        BuildReady();
        BuildUninstall();
        BuildProgress();
        BuildFinish();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }

    private static Label Line(DockStyle dock) => new Label { Dock = dock, Height = 2, BorderStyle = BorderStyle.Fixed3D, AutoSize = false };

    private TableLayoutPanel NewPage(Page page)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Visible = false };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _pages[page] = table;
        _content.Controls.Add(table);
        return table;
    }

    private static void Add(TableLayoutPanel table, Control control)
    {
        table.Controls.Add(control, 0, table.Controls.Count);
    }

    private static Label Paragraph(string text, bool bold = false) => new Label
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(580, 0),
        Margin = new Padding(0, 4, 0, 6),
        Font = bold ? new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) : SystemFonts.MessageBoxFont
    };

    private static GroupBox Group(string title, params Control[] controls)
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        flow.Controls.AddRange(controls);
        var group = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 6),
            Margin = new Padding(0, 4, 0, 8)
        };
        group.Controls.Add(flow);
        return group;
    }

    private static CheckBox Check(string text) => new CheckBox { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 2) };

    private static RadioButton Radio(string text) => new RadioButton { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 2) };

    private static Label Hint(string text) => new Label
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(540, 0),
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(21, 0, 3, 4)
    };

    // ------------------------------------------------------------------ страницы

    private void BuildWelcome()
    {
        TableLayoutPanel page = NewPage(Page.Welcome);
        _welcomeText = Paragraph(
            "Мастер установит программу Grapher " + Product.Version + " — построение графиков и диаграмм по точкам " +
            "в чертежах и фрагментах КОМПАС-3D: в прямоугольных и полярных координатах, треугольных, " +
            "с оформлением по Р 50-77-88.\n\n" +
            "Нужны Windows 10 или 11 (64-разрядная) и КОМПАС-3D. Grapher встраивается в меню «Приложения» КОМПАС " +
            "и работает как отдельная программа.");
        Add(page, _welcomeText);

        var link = new LinkLabel { Text = Product.RepositoryUrl, AutoSize = true, Font = SystemFonts.MessageBoxFont, Margin = new Padding(0, 4, 0, 6) };
        link.LinkClicked += (s, e) => OpenUrl(Product.RepositoryUrl);
        Label caption = Paragraph("Страница программы:");
        caption.Margin = new Padding(0, 4, 0, 6);
        var site = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        site.Controls.Add(caption);
        site.Controls.Add(link);
        Add(page, site);

        _kompasStatus = Paragraph("");
        Add(page, _kompasStatus);

        _existingText = Paragraph("");
        _modeUpdate = Radio("Обновить");
        _modeRemove = Radio("Удалить Grapher");
        _existingGroup = Group("Grapher уже установлен на этом компьютере", _existingText, _modeUpdate, _modeRemove);
        Add(page, _existingGroup);

        Add(page, Paragraph("Нажмите «Далее», чтобы продолжить, или «Отмена», чтобы закрыть мастер."));
    }

    private void BuildOptions()
    {
        TableLayoutPanel page = NewPage(Page.Options);

        _forMe = Radio("только для меня — права администратора не нужны");
        _forAll = Radio("для всех пользователей компьютера — потребуются права администратора");
        Add(page, Group("Для кого установить", _forMe, _forAll));
        _forMe.CheckedChanged += (s, e) => OnScopeChanged();

        _directory = new TextBox { Width = 440, Margin = new Padding(3, 6, 3, 3) };
        _browse = new Button { Text = "Обзор…", Size = new Size(90, 26), Margin = new Padding(3, 5, 3, 3) };
        _browse.Click += (s, e) => Browse();
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.Add(_directory);
        row.Controls.Add(_browse);
        _spaceLabel = Hint("");
        _spaceLabel.Margin = new Padding(3, 0, 3, 4);
        Add(page, Group("Папка установки", row, _spaceLabel));

        _startMenu = Check("в меню «Пуск»");
        _desktop = Check("на рабочем столе");
        _desktop.Margin = new Padding(24, 4, 3, 2);
        var shortcuts = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        shortcuts.Controls.Add(_startMenu);
        shortcuts.Controls.Add(_desktop);
        Add(page, Group("Ярлыки Grapher", shortcuts));

        _kompasList = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _kompasHint = Hint("");
        _kompasHint.Margin = new Padding(3, 2, 3, 4);
        Add(page, Group("Подключить Grapher к КОМПАС-3D (пункт «Grapher» в меню «Приложения»)", _kompasList, _kompasHint));

        // Пустая строка забирает оставшуюся высоту страницы, чтобы последняя рамка не растягивалась.
        Add(page, new Label { AutoSize = true, Margin = Padding.Empty });

        // Если установлено много версий КОМПАС, список может не поместиться в окно.
        page.AutoScroll = true;
    }

    private void BuildReady()
    {
        TableLayoutPanel page = NewPage(Page.Ready);
        _readyText = Paragraph("Всё готово к установке. Проверьте параметры и нажмите «Установить».");
        Add(page, _readyText);
        _summary = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Height = 230,
            BackColor = SystemColors.Window,
            Margin = new Padding(0, 4, 0, 4)
        };
        Add(page, _summary);
    }

    private void BuildUninstall()
    {
        TableLayoutPanel page = NewPage(Page.UninstallConfirm);
        _uninstallText = Paragraph("");
        Add(page, _uninstallText);
        _detach = Check("убрать Grapher из списка приложений КОМПАС-3D");
        _removeSettings = Check("удалить также настройки программы (" + Product.SettingsDirectory + ")");
        Add(page, Group("Что удалить", _detach, _removeSettings));
        Add(page, Paragraph("Перед удалением закройте КОМПАС-3D и Grapher. Проекты графиков (*.graph.json) и чертежи не удаляются."));
    }

    private void BuildProgress()
    {
        TableLayoutPanel page = NewPage(Page.Progress);
        _progressAction = Paragraph("");
        Add(page, _progressAction);
        _progressBar = new ProgressBar { Dock = DockStyle.Fill, Height = 22, Maximum = 1000, Margin = new Padding(0, 4, 0, 10) };
        Add(page, _progressBar);
        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Height = 220,
            BackColor = SystemColors.Window
        };
        Add(page, _log);
    }

    private void BuildFinish()
    {
        TableLayoutPanel page = NewPage(Page.Finish);
        _finishTitle = Paragraph("", true);
        Add(page, _finishTitle);
        _finishText = Paragraph("");
        Add(page, _finishText);
        _launch = Check("запустить Grapher");
        _openGuide = Check("открыть руководство пользователя");
        var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        flow.Controls.Add(_launch);
        flow.Controls.Add(_openGuide);
        Add(page, flow);
    }

    // ------------------------------------------------------------------ начальные значения

    private void FillDefaults()
    {
        bool kompas = Kompas.IsInstalled();
        _kompasStatus.Text = kompas
            ? "КОМПАС-3D на этом компьютере найден."
            : "КОМПАС-3D на этом компьютере не найден. Grapher можно установить, но строить графики он сможет только после установки КОМПАС-3D.";

        if (_existing != null)
        {
            bool same = _existing.Version == Product.Version;
            _existingText.Text = "Версия " + _existing.Version + " в папке " + _existing.Directory +
                                 (_existing.AllUsers ? " (для всех пользователей)." : " (для текущего пользователя).");
            _modeUpdate.Text = same ? "Переустановить версию " + Product.Version : "Обновить до версии " + Product.Version;
            _modeUpdate.Checked = true;
        }
        _existingGroup.Visible = _existing != null;

        bool allUsers = _existing != null ? _existing.AllUsers : _arguments.AllUsers;
        _forAll.Checked = allUsers;
        _forMe.Checked = !allUsers;
        _directory.Text = _existing?.Directory ?? _arguments.Directory ?? (allUsers ? Product.AllUsersDirectory : Product.PerUserDirectory);
        _startMenu.Checked = !_arguments.NoStartMenu;
        _desktop.Checked = !_arguments.NoDesktop;
        FillKompasList();
        _spaceLabel.Text = string.Format("Нужно около {0:0.0} МБ на диске.", Payload.InstalledSize / 1048576.0);
        UpdateKompasHint();

        string directory = _uninstallDirectory ?? _existing?.Directory;
        _uninstallText.Text = directory != null
            ? "Мастер удалит Grapher " + (_existing?.Version ?? "") + " из папки " + directory + "."
            : "Grapher не установлен: запись об установке не найдена.";
        _detach.Checked = !_arguments.NoKompas;
        _removeSettings.Checked = _arguments.RemoveSettings;
    }

    /// <summary>Флажок на каждую установленную версию (и редакцию) КОМПАС-3D.</summary>
    private void FillKompasList()
    {
        _kompasList.SuspendLayout();
        _kompasList.Controls.Clear();
        _kompasChecks.Clear();
        foreach (KompasProfile profile in Kompas.Profiles())
        {
            bool started = profile.ConfigFile != null;
            string text = profile.Title;
            if (!started) text += " — ещё не запускалась от вашего имени";
            else if (profile.Attached) text += " (Grapher уже подключён)";
            CheckBox box = Check(text);
            box.Checked = started && !_arguments.NoKompas;
            box.Enabled = started;
            _kompasList.Controls.Add(box);
            _kompasChecks.Add(new KeyValuePair<CheckBox, KompasProfile>(box, profile));
        }
        _kompasList.ResumeLayout();
    }

    private void UpdateKompasHint()
    {
        string text;
        if (_kompasChecks.Count == 0) text = "КОМПАС-3D не найден: подключите Grapher после установки КОМПАС (см. «Установка.txt»).";
        else if (Kompas.IsRunning()) text = "КОМПАС-3D сейчас запущен — закройте его перед установкой, иначе Grapher придётся подключить вручную.";
        else if (_kompasChecks.Exists(pair => !pair.Key.Enabled))
            text = "Незапускавшуюся версию подключите вручную после её первого запуска.";
        else text = _kompasChecks.Count > 1 ? "Отметьте версии КОМПАС, в меню которых должен появиться Grapher." : "";
        _kompasHint.Text = text;
        _kompasHint.Visible = text.Length > 0;
    }

    /// <summary>Файлы настроек отмеченных версий КОМПАС.</summary>
    private List<string> SelectedKompasConfigs()
    {
        var files = new List<string>();
        foreach (KeyValuePair<CheckBox, KompasProfile> pair in _kompasChecks)
        {
            if (pair.Key.Checked && pair.Key.Enabled && pair.Value.ConfigFile != null) files.Add(pair.Value.ConfigFile);
        }
        return files;
    }

    private List<string> SelectedKompasTitles()
    {
        var titles = new List<string>();
        foreach (KeyValuePair<CheckBox, KompasProfile> pair in _kompasChecks)
        {
            if (pair.Key.Checked && pair.Key.Enabled) titles.Add(pair.Value.Title);
        }
        return titles;
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
        {
            MessageBox.Show(this, "Не удалось открыть браузер: " + ex.Message + "\n\n" + url, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnScopeChanged()
    {
        string current = _directory.Text.Trim();
        // Папку по умолчанию меняем вместе с выбором; свою папку пользователя не трогаем.
        if (string.IsNullOrEmpty(current) || SamePathSafe(current, Product.PerUserDirectory) || SamePathSafe(current, Product.AllUsersDirectory))
        {
            _directory.Text = _forAll.Checked ? Product.AllUsersDirectory : Product.PerUserDirectory;
        }
        UpdateShield();
    }

    private static bool SamePathSafe(string a, string b)
    {
        try
        {
            return Product.SamePath(a, b);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void Browse()
    {
        using (var dialog = new FolderBrowserDialog { Description = "Папка для установки Grapher", ShowNewFolderButton = true })
        {
            string current = _directory.Text.Trim();
            if (Directory.Exists(current)) dialog.SelectedPath = current;
            else if (Directory.Exists(Path.GetDirectoryName(current) ?? "")) dialog.SelectedPath = Path.GetDirectoryName(current);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            // Выбрана общая папка (например, «Program Files») — программа ставится в свою подпапку.
            string chosen = dialog.SelectedPath;
            _directory.Text = Path.GetFileName(chosen).Equals(Product.Name, StringComparison.OrdinalIgnoreCase) ? chosen : Path.Combine(chosen, Product.Name);
        }
    }

    /// <summary>Права администратора понадобятся: установка для всех пользователей или удаление такой установки.</summary>
    private bool NeedsElevation => !Elevation.IsElevated && (_uninstallMode || _page == Page.UninstallConfirm ? UninstallAllUsers : _forAll.Checked);

    private bool UninstallAllUsers
    {
        get
        {
            ExistingInstall machine = ExistingInstall.Find(true);
            string directory = _uninstallDirectory ?? _existing?.Directory;
            return machine != null && directory != null && Product.SamePath(machine.Directory, directory);
        }
    }

    private void UpdateShield()
    {
        if (!IsHandleCreated) return;
        bool action = _page == Page.Ready || _page == Page.UninstallConfirm;
        Elevation.SetShield(_next, action && NeedsElevation);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateShield();
    }

    // ------------------------------------------------------------------ переходы

    private void ShowPage(Page page)
    {
        _page = page;
        foreach (KeyValuePair<Page, Control> pair in _pages) pair.Value.Visible = pair.Key == page;

        _back.Visible = page == Page.Options || page == Page.Ready;
        _back.Enabled = true;
        _next.Enabled = true;
        _cancel.Visible = page != Page.Finish;
        _cancel.Enabled = page != Page.Progress;

        switch (page)
        {
            case Page.Welcome:
                SetHeader("Вас приветствует мастер установки Grapher", "Grapher " + Product.Version + " — графики по точкам для КОМПАС-3D");
                _next.Text = "Далее >";
                break;
            case Page.Options:
                SetHeader("Параметры установки", "Для кого установить программу, куда и что добавить");
                UpdateKompasHint();
                _next.Text = "Далее >";
                break;
            case Page.Ready:
                SetHeader("Всё готово к установке", "Проверьте выбранные параметры");
                _summary.Text = Summary();
                _readyText.Text = NeedsElevation
                    ? "Всё готово к установке. Нажмите «Установить» — Windows попросит разрешения на изменения (права администратора)."
                    : "Всё готово к установке. Проверьте параметры и нажмите «Установить».";
                _next.Text = "Установить";
                break;
            case Page.UninstallConfirm:
                SetHeader("Удаление Grapher", "Программа будет удалена с компьютера");
                _next.Text = "Удалить";
                _next.Enabled = (_uninstallDirectory ?? _existing?.Directory) != null;
                break;
            case Page.Progress:
                SetHeader(_uninstallMode ? "Удаление" : "Установка", "Подождите, пока мастер выполнит работу");
                _next.Enabled = false;
                _back.Visible = false;
                break;
            case Page.Finish:
                _next.Text = "Готово";
                break;
        }
        UpdateShield();
        if (_next.Enabled) ActiveControl = _next;
    }

    private void SetHeader(string title, string subtitle)
    {
        _title.Text = title;
        _subtitle.Text = subtitle;
    }

    private void GoBack()
    {
        if (_page == Page.Options) ShowPage(Page.Welcome);
        else if (_page == Page.Ready) ShowPage(Page.Options);
    }

    private void GoNext()
    {
        switch (_page)
        {
            case Page.Welcome:
                if (_existing != null && _modeRemove.Checked)
                {
                    SwitchToUninstall();
                    return;
                }
                ShowPage(Page.Options);
                break;

            case Page.Options:
                try
                {
                    _directory.Text = Installer.CheckDirectory(_directory.Text);
                }
                catch (ArgumentException ex)
                {
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ShowPage(Page.Ready);
                break;

            case Page.Ready:
                StartInstall();
                break;

            case Page.UninstallConfirm:
                StartUninstall();
                break;

            case Page.Finish:
                FinishAndClose();
                break;
        }
    }

    /// <summary>На первой странице выбрано «Удалить»: удаление выполняет копия мастера во временной папке.</summary>
    private void SwitchToUninstall()
    {
        string copy = Path.Combine(Path.GetTempPath(), "GrapherUninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
        File.Copy(Product.SelfPath, copy, true);
        Process.Start(new ProcessStartInfo(copy, Arguments.Join(new[] { "/uninstall", "/relocated", "/dir=" + _existing.Directory }))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath()
        });
        ExitCode = 0;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            return;
        }
        if (_page != Page.Finish && e.CloseReason == CloseReason.UserClosing)
        {
            string question = _uninstallMode ? "Прервать удаление Grapher?" : "Прервать установку Grapher?";
            if (MessageBox.Show(this, question, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        base.OnFormClosing(e);
    }

    // ------------------------------------------------------------------ установка

    private InstallOptions CollectOptions() => new InstallOptions
    {
        AllUsers = _forAll.Checked,
        Directory = _directory.Text.Trim(),
        StartMenuShortcut = _startMenu.Checked,
        DesktopShortcut = _desktop.Checked,
        KompasConfigs = SelectedKompasConfigs(),
        AttachToKompas = SelectedKompasConfigs().Count > 0
    };

    private string Summary()
    {
        InstallOptions o = CollectOptions();
        var text = new StringBuilder();
        text.AppendLine("Программа: Grapher " + Product.Version);
        text.AppendLine("Для кого: " + (o.AllUsers ? "все пользователи компьютера (нужны права администратора)" : "только для текущего пользователя"));
        text.AppendLine("Папка: " + o.Directory);
        text.AppendLine();
        text.AppendLine("Будет сделано:");
        text.AppendLine("• скопированы файлы программы и примеры данных;");
        text.AppendLine("• зарегистрирована библиотека для КОМПАС-3D;");
        foreach (string title in SelectedKompasTitles()) text.AppendLine("• Grapher подключён к " + title + " (меню «Приложения»);");
        if (o.StartMenuShortcut) text.AppendLine("• добавлен ярлык в меню «Пуск»;");
        if (o.DesktopShortcut) text.AppendLine("• добавлен ярлык на рабочем столе;");
        text.AppendLine("• Grapher добавлен в список установленных приложений Windows (оттуда же его можно удалить).");
        if (_existing != null)
        {
            text.AppendLine();
            bool sameDirectory = SamePathSafe(_existing.Directory, o.Directory) && _existing.AllUsers == o.AllUsers;
            if (sameDirectory) text.AppendLine("Установленная версия " + _existing.Version + " будет заменена.");
            else if (_existing.AllUsers && !o.AllUsers && !Elevation.IsElevated)
                text.AppendLine("Установка для всех пользователей в " + _existing.Directory + " останется: удалить её можно через «Приложения и возможности».");
            else text.AppendLine("Прежняя установка в " + _existing.Directory + " будет удалена.");
        }
        return text.ToString();
    }

    private void StartInstall()
    {
        InstallOptions options = CollectOptions();

        // КОМПАС при выходе перезаписывает свой список приложений: подключение возможно, только пока он закрыт.
        while (options.AttachToKompas && Kompas.IsRunning())
        {
            DialogResult answer = MessageBox.Show(this,
                "КОМПАС-3D запущен. Закройте его, чтобы мастер подключил к нему Grapher, и нажмите «Повторить».\n\n" +
                "«Пропустить» — установить без подключения (подключить Grapher можно потом вручную).",
                Text, MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Information);
            if (answer == DialogResult.Abort) return;
            if (answer == DialogResult.Ignore) options.AttachToKompas = false;
        }

        if (options.AllUsers && !Elevation.IsElevated)
        {
            List<string> configs = options.KompasConfigs;
            Action<Action<string>> userPart = null;
            if (options.AttachToKompas) userPart = log => Installer.AttachToKompas(log, configs);
            RunElevated(BuildChildArguments(options, false), userPart, false);
            return;
        }

        RunWork(log => Installer.Install(options, log, SetProgress), false);
    }

    private void StartUninstall()
    {
        string directory = _uninstallDirectory ?? _existing?.Directory;
        if (directory == null) return;
        var options = new UninstallOptions
        {
            Directory = directory,
            AllUsers = UninstallAllUsers,
            DetachFromKompas = _detach.Checked,
            RemoveSettings = _removeSettings.Checked
        };

        if (options.AllUsers && !Elevation.IsElevated)
        {
            // Работу с правами администратора выполняет копия мастера; список приложений КОМПАС
            // и настройки — это данные текущего пользователя, их обрабатывает это окно.
            Action<Action<string>> userPart = log =>
            {
                if (options.DetachFromKompas)
                {
                    if (Kompas.IsRunning()) log("КОМПАС-3D запущен — Grapher остался в списке его приложений.");
                    else Kompas.Detach(log);
                }
                if (options.RemoveSettings && Directory.Exists(Product.SettingsDirectory))
                {
                    Directory.Delete(Product.SettingsDirectory, true);
                    log("Настройки программы удалены.");
                }
            };
            var list = new List<string> { "/uninstall", "/silent", "/child", "/relocated", "/allusers", "/nokompas", "/dir=" + directory };
            RunElevated(list, userPart, true);
            return;
        }

        RunWork(log => Installer.Uninstall(options, log, SetProgress), true);
    }

    private static List<string> BuildChildArguments(InstallOptions options, bool uninstall)
    {
        var list = new List<string> { "/silent", "/child", "/allusers", "/nokompas", "/dir=" + options.Directory };
        if (!options.StartMenuShortcut) list.Add("/nostartmenu");
        if (!options.DesktopShortcut) list.Add("/nodesktop");
        return list;
    }

    /// <summary>
    /// Работа с правами администратора: этот же файл запускается через окно UAC без окна мастера,
    /// а это окно показывает его журнал и затем выполняет часть, относящуюся к текущему пользователю.
    /// </summary>
    private void RunElevated(List<string> childArguments, Action<Action<string>> userPart, bool uninstall)
    {
        string logFile = Path.Combine(Path.GetTempPath(), "GrapherSetup-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".log");
        childArguments.Add("/log=" + logFile);

        Process child = Elevation.Start(Arguments.Join(childArguments));
        if (child == null)
        {
            MessageBox.Show(this,
                (uninstall ? "Удаление установки для всех пользователей" : "Установка для всех пользователей") +
                " требует прав администратора, а разрешение не получено.\n\n" +
                (uninstall ? "Повторите удаление и разрешите изменения в окне Windows." : "Выберите установку «только для меня» или повторите и разрешите изменения в окне Windows."),
                Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        RunWork(log =>
        {
            log("Работа выполняется с правами администратора…");
            child.WaitForExit();
            int code = child.ExitCode;
            child.Dispose();
            if (File.Exists(logFile))
            {
                foreach (string line in File.ReadAllLines(logFile, Encoding.UTF8)) log(line);
                File.Delete(logFile);
            }
            if (code == 2) throw new FilesInUseException(Product.LibraryName);
            if (code != 0) throw new InvalidOperationException((uninstall ? "Удаление" : "Установка") + " с правами администратора не выполнена (код " + code + ").");
            SetProgress(0.95);
            userPart?.Invoke(log);
            SetProgress(1);
        }, uninstall, marquee: true);
    }

    private void RunWork(Action<Action<string>> work, bool uninstall, bool marquee = false)
    {
        _log.Clear();
        _progressBar.Style = marquee ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        _progressBar.Value = 0;
        ShowPage(Page.Progress);
        _busy = true;

        void Log(string line) => BeginInvoke((Action)(() =>
        {
            _progressAction.Text = line;
            _log.AppendText(line + Environment.NewLine);
        }));

        Task.Run(() =>
        {
            Exception failure = null;
            try
            {
                work(Log);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            BeginInvoke((Action)(() => WorkFinished(failure, uninstall, () => RunWork(work, uninstall, marquee))));
        });
    }

    private void SetProgress(double fraction)
    {
        BeginInvoke((Action)(() =>
        {
            if (_progressBar.Style == ProgressBarStyle.Marquee) return;
            _progressBar.Value = Math.Max(0, Math.Min(1000, (int)Math.Round(fraction * 1000)));
        }));
    }

    private void WorkFinished(Exception failure, bool uninstall, Action retry)
    {
        _busy = false;
        _progressBar.Style = ProgressBarStyle.Continuous;

        if (failure is FilesInUseException)
        {
            DialogResult answer = MessageBox.Show(this, failure.Message + "\n\nЗакройте КОМПАС-3D и Grapher и нажмите «Повтор».", Text,
                MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning);
            if (answer == DialogResult.Retry)
            {
                retry();
                return;
            }
        }

        _succeeded = failure == null;
        ExitCode = _succeeded ? 0 : failure is FilesInUseException ? 2 : 1;
        ShowPage(Page.Finish);

        if (!_succeeded)
        {
            SetHeader(uninstall ? "Удаление не выполнено" : "Установка не выполнена", "Подробности — ниже");
            _finishTitle.Text = (uninstall ? "Grapher не удалён." : "Grapher не установлен.");
            _finishText.Text = failure.Message + "\n\nЖурнал работы:\n" + _log.Text;
            _launch.Visible = _openGuide.Visible = false;
            return;
        }

        if (uninstall)
        {
            SetHeader("Удаление завершено", "Grapher удалён с компьютера");
            _finishTitle.Text = "Grapher удалён.";
            _finishText.Text = (_removeSettings.Checked ? "" : "Настройки программы оставлены в папке " + Product.SettingsDirectory + ".\n\n") +
                               (Kompas.IsAttached()
                                   ? "Grapher остался в списке приложений КОМПАС-3D: уберите его в КОМПАС — «Приложения» → «Конфигуратор…» → Grapher → «Исключить из конфигурации»."
                                   : "Проекты графиков и чертежи не затронуты.");
            _launch.Visible = _openGuide.Visible = false;
            return;
        }

        SetHeader("Установка завершена", "Grapher " + Product.Version + " установлен");
        _finishTitle.Text = "Grapher " + Product.Version + " установлен.";
        string kompas;
        if (!Kompas.IsInstalled())
        {
            kompas = "КОМПАС-3D на этом компьютере не найден. После его установки подключите Grapher: «Приложения» → " +
                     "«Добавить приложения…» → вкладка «ActiveX» → «GrapherKompas.Library» → «Открыть».";
        }
        else if (Kompas.IsAttached())
        {
            kompas = "Запустите КОМПАС-3D: в меню «Приложения» появится пункт «Grapher» → «Построить график…». " +
                     "Если КОМПАС был открыт, перезапустите его.";
        }
        else
        {
            kompas = "Осталось один раз подключить Grapher в КОМПАС-3D: «Приложения» → «Добавить приложения…» → " +
                     "вкладка «ActiveX» → «GrapherKompas.Library» → «Открыть». После этого в меню «Приложения» появится пункт «Grapher».";
        }
        _finishText.Text = kompas + "\n\nПрограмму можно запускать и отдельно — ярлыком «Grapher». Удалить её можно через «Приложения и возможности» Windows.";
        _launch.Visible = _openGuide.Visible = true;
        _launch.Checked = true;
        _openGuide.Checked = false;
    }

    private void FinishAndClose()
    {
        if (_succeeded && !_uninstallMode)
        {
            string directory = _directory.Text.Trim();
            try
            {
                if (_launch.Checked) Process.Start(new ProcessStartInfo(Path.Combine(directory, Product.ExeName)) { WorkingDirectory = directory });
                if (_openGuide.Checked && File.Exists(Path.Combine(directory, Product.GuideName)))
                {
                    Process.Start(new ProcessStartInfo(Path.Combine(directory, Product.GuideName)) { UseShellExecute = true });
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is FileNotFoundException)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        Close();
    }

    // ------------------------------------------------------------------ снимки страниц

    /// <summary>Снимки всех страниц мастера в папку (для документации и проверки раскладки).</summary>
    public static int SaveScreens(string folder)
    {
        Directory.CreateDirectory(folder);
        var arguments = new Arguments(new string[0]);
        using (var form = new WizardForm(arguments, null))
        {
            form.Show();
            Application.DoEvents();

            void Shot(string name)
            {
                form.UpdateShield();
                Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                    bitmap.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
                }
            }

            form.ShowPage(Page.Welcome);
            Shot("1-welcome");
            form.ShowPage(Page.Options);
            Shot("2-options");
            form.ShowPage(Page.Ready);
            Shot("3-ready");
            form.ShowPage(Page.Progress);
            form._progressAction.Text = "Копирование файлов в " + Product.PerUserDirectory + "…";
            form._progressBar.Value = 600;
            form._log.Text = "Проверка, не заняты ли файлы программы…\r\nКопирование файлов в " + Product.PerUserDirectory + "…\r\n";
            Shot("4-progress");
            form._succeeded = true;
            form.WorkFinished(null, false, null);
            Shot("5-finish");
            form._page = Page.UninstallConfirm;
            form.ShowPage(Page.UninstallConfirm);
            Shot("6-uninstall");
            form._page = Page.Finish;
            form.Close();
        }
        return 0;
    }
}
