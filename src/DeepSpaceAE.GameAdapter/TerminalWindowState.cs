using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace DeepSpaceAE.GameAdapter;

/// <summary>Use the native per-save window-position and tablet zoom stores.</summary>
internal sealed class TerminalWindowState : MonoBehaviour
{
    internal const string ZoomId = "deepspaceae.terminal";
    private static readonly ConditionalWeakTable<PixelWindow, TerminalWindowState> windows = new();
    private PixelWindow window = null!;
    private PlayerStore store = null!;
    private GameItem owner = null!;
    private StoreUI zoom = null!;
    private MachineryButton sizeButton = null!;
    private int ownerId;
    private bool ready;

    internal static TerminalWindowState Attach(PixelWindow window, GameItem terminal)
    {
        var state = window.handler.gameObject.AddComponent<TerminalWindowState>();
        state.window = window; state.store = PlayerStore.Instance; state.owner = terminal; state.ownerId = terminal.GetUniqueID();
        state.store.contentWindowPositions ??= new Dictionary<int, Vector2>();
        state.store.storeUIZoomLevels ??= new Dictionary<string, int>();
        state.zoom = window.handler.gameObject.AddComponent<TerminalZoom>();
        state.zoom.identifier = ZoomId; state.zoom.zoomable = true; state.zoom.zoomRoot = window.rectTransform;
        // Match the native negotiation tablet's 100% -> 50% -> 75% cycle.
        state.zoom.zoomScaleNormal = 1f; state.zoom.zoomScaleAlt = 0.5f; state.zoom.zoomScaleAlt2 = 0.75f;
        state.sizeButton = new MachineryButton("100%", state.ToggleSize, 42, 16)
            .SetDescription(UiText.Get("ui_terminal_size"));
        window.AddHeaderButton(state.sizeButton);
        windows.Add(window, state);
        return state;
    }

    internal void Restore()
    {
        zoom.OnEnable(); UpdateSizeLabel();
        if (store.contentWindowPositions.TryGetValue(ownerId, out var position)) window.rectTransform.anchoredPosition = position;
        else
        {
            Bounds(out var min, out var max, out var size);
            window.rectTransform.anchoredPosition = new Vector2((min.x + max.x - size.x) / 2, (min.y + max.y + size.y) / 2);
        }
        Clamp(); ready = true; Capture();
    }

    internal void ToggleSize()
    {
        zoom.ToggleZoom(); UpdateSizeLabel(); Clamp(); Capture();
    }
    private void UpdateSizeLabel() => sizeButton.SetText(Mathf.RoundToInt(window.rectTransform.localScale.x * 100) + "%");
    private void Bounds(out Vector2 min, out Vector2 max, out Vector2 size)
    {
        var canvas = window.handler.GetComponentInParent<Canvas>(true)?.rootCanvas ?? RenderHandler.current.windowUICanvas;
        var parent = window.rectTransform.parent;
        min = parent.InverseTransformPoint(canvas.pixelRect.min); max = parent.InverseTransformPoint(canvas.pixelRect.max);
        size = Vector2.Scale(new Vector2(window.widthPixels, window.heightPixels), window.rectTransform.localScale);
    }
    internal void Clamp()
    {
        Bounds(out var min, out var max, out var size);
        var position = window.rectTransform.anchoredPosition;
        window.rectTransform.anchoredPosition = new Vector2(
            Mathf.Clamp(position.x, min.x, Mathf.Max(min.x, max.x - size.x)),
            Mathf.Clamp(position.y, Mathf.Min(max.y, min.y + size.y), max.y));
    }
    internal void Capture()
    {
        if (ready && window.handler != null && store != null && (owner is not GameItemElement item || !item.IsDestroyed()))
            store.contentWindowPositions[ownerId] = window.rectTransform.anchoredPosition;
    }
    private void LateUpdate() => Capture();
    private void OnDisable() => Capture();
    internal static bool ClampIfTerminal(PixelWindow window)
    {
        if (!windows.TryGetValue(window, out var state) || state == null) return false;
        state.Clamp(); return true;
    }
}

// Keep native tablet zoom without its built-in-window close dispatch: AE's
// PixelWindow already owns closing, and StoreUIManager only knows native IDs.
internal sealed class TerminalZoom : StoreUI
{
    public new void OnDisable() { }
}

// PixelWindow's native bounds use unscaled dimensions. Only AE terminals use
// the tablet zoom component, so leave all other windows on their native path.
[HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.FixBounds))]
internal static class TerminalWindowBoundsPatch
{
    [HarmonyPrefix] private static bool Prefix(PixelWindow __instance) => !TerminalWindowState.ClampIfTerminal(__instance);
}
