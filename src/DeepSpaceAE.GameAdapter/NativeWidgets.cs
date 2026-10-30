using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal static class NativeWidgets
{
    private static readonly Dictionary<string, string> tagNames = new(StringComparer.Ordinal);
    private static string locale = "";
    private static int localeFrame = -1;
    internal static string TagName(string tag)
    {
        if (localeFrame != Time.frameCount)
        {
            localeFrame = Time.frameCount; string current = LocHelper.GetCurrentLocaleCode();
            if (current != locale) { locale = current; tagNames.Clear(); }
        }
        if (tagNames.TryGetValue(tag, out var name)) return name;
        name = TypeHelper.GetTypeDisplayName(tag);
        tagNames.Add(tag, name); return name;
    }
    internal static Sprite? Sprite(string name) => ModLoader.Mods.FirstOrDefault(m => m.Manifest.ID == ModEntry.ModId)?.Sprites.TryGetValue(name, out var image) == true ? image : null;
    internal sealed class Field : EmptyPixelElement
    {
        internal CustomUIElement Control = null!;
        internal Field(int width) : base(width, 18) { }
    }
    internal static Field Input(string placeholder, string value, Action<string> change, int width = 160)
    {
        var element = new Field(width);
        var window = new CustomUIWindow { root = element.handler.gameObject, useMachineryStyle = true };
        var builder = new CustomUIBuilder(CustomUIManager.Instance, window, element.rectTransform);
        builder.AddInput(placeholder, value, change, onTyping: change);
        element.Control = builder.Last;
        var rect = builder.Last.root.transform as RectTransform;
        if (rect != null) { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(width, 18); }
        return element;
    }
    internal static Field Dropdown(IList<string> labels, int selected, Action<int> change, int width = 110)
    {
        var element = new Field(width);
        var window = new CustomUIWindow { root = element.handler.gameObject, useMachineryStyle = true };
        var builder = new CustomUIBuilder(CustomUIManager.Instance, window, element.rectTransform);
        builder.AddDropdown(labels, selected, change);
        element.Control = builder.Last;
        var rect = builder.Last.root.transform as RectTransform;
        if (rect != null) { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(width, 18); }
        return element;
    }
    internal static PixelElement Icon(Sprite? sprite, int width = 16, int height = 16)
    {
        var element = new EmptyPixelElement(width, height);
        if (sprite != null) { var node = TreeNodeRender.Instantiate().SetParent(element.handler); MachineryStyle.SetSimple(node, sprite, Color.white); MachineryStyle.Place(node, 0, 0, width, height); }
        return element;
    }
}
