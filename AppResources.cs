using System.Drawing;
using System.IO;

namespace MovaCore
{
    /// <summary>
    /// Icon and logo embedded into the executable (see the EmbeddedResource items in MovaCore.csproj),
    /// so the app does not depend on loose image files next to the exe.
    /// </summary>
    internal static class AppResources
    {
        private const string IconName = "MovaCore.movacore.ico";
        private const string LogoName = "MovaCore.logo.png";

        /// <summary>The app icon frame closest to <paramref name="size"/>, or null if the resource is missing.</summary>
        public static Icon? LoadIcon(Size size)
        {
            using Stream? stream = OpenResource(IconName);
            return stream == null ? null : new Icon(stream, size);
        }

        /// <summary>The 256 px logo, or null if the resource is missing. The caller owns the bitmap.</summary>
        public static Bitmap? LoadLogo()
        {
            using Stream? stream = OpenResource(LogoName);
            if (stream == null) return null;

            // A bitmap created from a stream needs that stream for its whole lifetime; the copy does not
            using var decoded = new Bitmap(stream);
            return new Bitmap(decoded);
        }

        private static Stream? OpenResource(string name) =>
            typeof(AppResources).Assembly.GetManifestResourceStream(name);
    }
}
