using System.Drawing;
using System.IO;

namespace Grapher.UI;

/// <summary>Иконка приложения (assets\grapher.ico), встроенная в сборку.</summary>
public static class AppIcon
{
    private const string ResourceName = "Grapher.UI.grapher.ico";
    private static Icon _icon;

    /// <summary>Иконка для окон; null, если ресурс недоступен (окно получит стандартную иконку).</summary>
    public static Icon Get()
    {
        if (_icon != null) return _icon;

        using (Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName))
        {
            if (stream == null) return null;
            _icon = new Icon(stream);
        }
        return _icon;
    }
}
