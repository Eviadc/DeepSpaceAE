namespace DeepSpaceAE.GameAdapter;

internal static class ModSettings
{
    internal const string EnergyKey = "experimental_network_energy";
    private static ConfigEntry<bool>? energy;
    internal static bool EnergyEnabled => energy?.Value ?? false;

    internal static void Enable()
    {
        var config = ModHelper.GetConfig(ModEntry.ModId);
        energy = config.Get(EnergyKey) as ConfigEntry<bool> ?? config.Define(EnergyKey, false, "AE",
            () => UiText.Get("setting_energy_name"), tooltip: () => UiText.Get("setting_energy_desc"));
        energy.OnChanged += Changed;
    }

    internal static void Disable()
    {
        if (energy != null) energy.OnChanged -= Changed;
    }

    private static void Changed(bool _) => ModEntry.ActiveSession?.PowerModeChanged();
}
