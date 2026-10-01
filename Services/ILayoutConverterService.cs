using MovaCore.Models;

namespace MovaCore.Services
{
    public interface ILayoutConverterService
    {
        string Convert(string text);

        /// <summary>The layout <see cref="Convert"/> converts <paramref name="text"/> into.</summary>
        KeyboardLanguage TargetOf(string text);
    }
}
