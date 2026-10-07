using System.Drawing;
using System.Windows.Forms;

namespace Grapher.UI;

/// <summary>Мелкие помощники для сборки окон в коде.</summary>
internal static class Ui
{
    public static Label Label(string text) => new Label
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 6, 3, 3)
    };

    /// <summary>Пояснение серым цветом.</summary>
    public static Label Hint(string text) => new Label
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(3, 6, 3, 3)
    };

    /// <summary>Таблица раскладки, растущая по содержимому.</summary>
    public static TableLayoutPanel Table(int columns)
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = columns,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        for (int i = 0; i < columns; i++)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }
        return table;
    }

    /// <summary>Горизонтальный ряд элементов.</summary>
    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Anchor = AnchorStyles.Left
        };
        row.Controls.AddRange(controls);
        return row;
    }

    /// <summary>Группа с содержимым, высота которой определяется содержимым.</summary>
    public static GroupBox Group(string title, Control content)
    {
        var group = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(6, 3, 6, 6),
            Margin = new Padding(3)
        };
        content.Dock = DockStyle.Fill;
        group.Controls.Add(content);
        return group;
    }

    /// <summary>Тонкая горизонтальная линия-разделитель на всю ширину таблицы.</summary>
    public static Control Separator(TableLayoutPanel table, int row)
    {
        var line = new Label
        {
            AutoSize = false,
            Height = 2,
            BorderStyle = BorderStyle.Fixed3D,
            Dock = DockStyle.Top,
            Margin = new Padding(3, 3, 3, 3)
        };
        table.Controls.Add(line, 0, row);
        table.SetColumnSpan(line, table.ColumnCount);
        return line;
    }

    /// <summary>
    /// Кнопка по размеру текста. GrowAndShrink обязателен: иначе при масштабе экрана больше 100 %
    /// кнопка масштабируется дважды (по шрифту и ещё раз вместе с формой).
    /// </summary>
    public static Button Button(string text) => new Button
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(6, 1, 6, 1),
        Margin = new Padding(3, 2, 3, 2),
        Anchor = AnchorStyles.Left
    };

    public static CheckBox Check(string text) => new CheckBox
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 5, 3, 3)
    };

    public static ComboBox Combo(int width, params string[] items)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = width,
            Anchor = AnchorStyles.Left
        };
        combo.Items.AddRange(items);
        if (items.Length > 0) combo.SelectedIndex = 0;
        return combo;
    }
}
