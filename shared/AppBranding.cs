using System.Drawing;
using System.Windows.Forms;

namespace ScreensaverApp.Configuration
{
    internal static class AppBranding
    {
        // CUSTOMIZE ICON: replace assets/App.ico, then rebuild the full installer.
        // The same asset is also embedded as the native executable/shortcut icon.
        internal static Icon CreateIcon(int size = 32)
        {
            using (var stream = typeof(AppBranding).Assembly.GetManifestResourceStream("CineSaver.App.ico"))
            using (var icon = new Icon(stream, size, size)) return (Icon)icon.Clone();
        }
        internal static Bitmap CreateLogo() { using (var icon = CreateIcon(256)) return icon.ToBitmap(); }
        internal static void SetWindowIcon(Form form)
        {
            Icon icon = CreateIcon(); form.Icon = icon;
            form.Disposed += (_, __) => icon.Dispose();
        }
    }
}
