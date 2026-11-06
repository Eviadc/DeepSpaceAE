using System;
namespace DeepSpaceAE.GameAdapter;

/// <summary>Resolve this mod's resource keys through the native localization API.</summary>
internal static class UiText
{
    internal static string Get(string key, params object[] arguments) => ModHelper.GetLocalized(ModEntry.ModId, key, arguments);
}
