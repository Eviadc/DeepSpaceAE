using System;
namespace DeepSpaceAE.GameAdapter;

/// <summary>Only ASCII resource keys enter the official localization API. Resolve after assets load.</summary>
internal static class UiText
{
    internal static string Get(string key, params object[] arguments)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Empty localization key.", nameof(key));
        foreach (char c in key)
            if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_')
                throw new ArgumentException("Localization keys must contain only lowercase ASCII, digits and underscores.", nameof(key));
        return ModHelper.GetLocalized(ModEntry.ModId, key, arguments);
    }
}
