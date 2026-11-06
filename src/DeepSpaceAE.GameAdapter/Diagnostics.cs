using System.Text;

namespace DeepSpaceAE.GameAdapter;

internal static class Diagnostics
{

    // The F8 console font in the tested build has no CJK fallback. Keep its input ASCII,
    // including exception messages, while retaining the original text in our own UI.
    internal static string ConsoleText(string value)
    {
        var result = new StringBuilder();
        foreach (var character in value)
            if (character <= 127) result.Append(character);
            else result.Append("\\u").Append(((int)character).ToString("X4"));
        return result.ToString();
    }

}
