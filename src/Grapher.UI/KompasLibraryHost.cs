using System;
using System.Windows.Forms;
using Grapher.Kompas;

namespace Grapher.UI;

/// <summary>
/// Запуск окна приложения изнутри КОМПАС (приложение-библиотека).
/// Окно показывается модально поверх главного окна КОМПАС.
/// </summary>
public static class KompasLibraryHost
{
    private static bool _stylesEnabled;

    /// <param name="kompasObject">Объект KompasObject, который КОМПАС передаёт библиотеке при вызове команды.</param>
    public static void ShowDialog(object kompasObject)
    {
        KompasSession session = KompasSession.FromLibrary(kompasObject);

        if (!_stylesEnabled)
        {
            Application.EnableVisualStyles();
            _stylesEnabled = true;
        }

        var owner = new WindowHandle(session.MainWindowHandle);
        using (var form = new MainForm(session))
        {
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.CenterParent;

            // Чтобы указать точку курсором, модальное окно нужно убрать: пока оно открыто,
            // КОМПАС не принимает ввод. Поэтому окно закрывается, точка указывается, окно открывается снова.
            while (true)
            {
                DialogResult result = form.ShowDialog(owner);
                if (result != DialogResult.Retry || !form.HasPendingBuild) break;
                form.CompletePendingBuild();
            }
        }
    }

    private sealed class WindowHandle : IWin32Window
    {
        public WindowHandle(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }
}
