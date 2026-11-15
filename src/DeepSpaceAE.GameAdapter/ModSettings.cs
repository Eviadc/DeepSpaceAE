namespace DeepSpaceAE.GameAdapter;

internal static class ModSettings
{
    internal const string EnergyKey = "experimental_network_energy";
    internal const string NativeInventoryKey = "native_inventory_reads";
    internal const string NativeDrawerKey = "native_drawer_validation";
    private static ConfigEntry<bool>? energy;
    private static ConfigEntry<bool>? nativeInventory, nativeDrawer;
    internal static bool EnergyEnabled => energy?.Value ?? false;
    internal static bool NativeInventoryReads => nativeInventory?.Value ?? false;
    internal static bool NativeDrawerValidation => nativeDrawer?.Value ?? false;

    internal static void Enable()
    {
        var config = ModHelper.GetConfig(ModEntry.ModId);
        energy = config.Get(EnergyKey) as ConfigEntry<bool> ?? config.Define(EnergyKey, false, "AE",
            () => UiText.Get("setting_energy_name"), tooltip: () => UiText.Get("setting_energy_desc"));
        energy.OnChanged += Changed;
        nativeInventory = config.Get(NativeInventoryKey) as ConfigEntry<bool> ?? config.Define(NativeInventoryKey, false, "AE",
            () => UiText.Get("setting_native_inventory_name"), tooltip: () => UiText.Get("setting_native_inventory_desc"));
        nativeDrawer = config.Get(NativeDrawerKey) as ConfigEntry<bool> ?? config.Define(NativeDrawerKey, false, "AE",
            () => UiText.Get("setting_native_drawer_name"), tooltip: () => UiText.Get("setting_native_drawer_desc"));
    }

    internal static void Disable()
    {
        if (energy != null) energy.OnChanged -= Changed;
    }

    private static void Changed(bool _) => ModEntry.ActiveSession?.PowerModeChanged();
}
