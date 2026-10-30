using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DeepSpaceAE.GameAdapter;

internal static class Diagnostics
{
    internal const string WindowId = "deepspaceae.diagnostics";

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

    internal static string PartCounts(GameInventory root) => string.Join("; ", ModEntry.Parts.Select(part =>
        $"{part.Part}={root.childItems.Count(item => item.identifier == part.Id)}"));

    internal static void Show(string title, IEnumerable<string> lines)
    {
        if (CustomUIManager.Instance == null) return;
        var builder = CustomUIManager.Window(WindowId, title, CustomUIManager.LAYER_PAPER)
            .SetSize(660, 620).SetDraggable(true).SetCloseOnEscape(true).BeginScroll(500);
        foreach (var line in lines) builder.AddLabel(line);
        builder.End().AddButton("Close", Close).Show();
    }

    internal static void Close()
    {
        if (CustomUIManager.Instance != null) CustomUIManager.Instance.CloseWindow(WindowId);
    }
}
