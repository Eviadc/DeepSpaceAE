using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal static class NativeWidgets
{
    internal static void StorageBackground(GameGridScrollableInventory inventory)
    {
        var node = inventory.handler.transform.GetChild(0).GetComponent<TreeNodeRender>();
        var image = typeof(TreeNodeRender).GetProperty("image")!.GetValue(node)!;
        typeof(RenderHandler).GetMethod("SetHighlightColor")!.Invoke(RenderHandler.current,
            new object[] { image, RenderHandler.BackgroundColor.Tan, RenderHandler.BackgroundType.Grid });
    }
    internal static void FullCellHit(GameItem item)
    {
        if (item is not GameItemElement element) return;
        var image = typeof(TreeNodeRender).GetProperty("image")!.GetValue(element.background)!;
        image.GetType().GetProperty("alphaHitTestMinimumThreshold")!.SetValue(image, 0f);
    }

    private static readonly Dictionary<string, string> tagNames = new(StringComparer.Ordinal);
    private static string locale = "";
    private static int localeFrame = -1;
    internal static string TagName(string tag)
    {
        if(tag=="deepspaceae.liquid") return UiText.Get("ui_liquids");
        if(tag==GameSession.CustomerFilter) return UiText.Get("ui_customer_demand");
        if(tag.StartsWith("deepspaceae.", StringComparison.Ordinal)) return tag;
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
    // CustomUI machinery controls use twice the design-pixel scale of PixelWindow.
    // Keep their real prefab layout; scale the whole control instead of clipping 16px text into 18px.
    internal sealed class Field : PixelElement, MachineryWidget
    {
        internal CustomUIElement Control = null!;
        public bool fillWidth { get; set; }
        public bool canResize => false;
        public int widthPixels { get; }
        public int heightPixels => 20;
        public TreeNodeRender handler { get; }
        public RectTransform rectTransform => handler.rectTransform;
        public TreeNodeRender inner => handler;
        internal Field(int width) { widthPixels = width; handler = TreeNodeRender.Instantiate(this, "AE field"); }
        public void Validate()
        {
            if (Control?.root?.transform is not RectTransform rect) return;
            const float scale = CustomUIMachineryStyle.SCALE;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(widthPixels * scale, heightPixels * scale);
            rect.localScale = new Vector3(1 / scale, 1 / scale, 1);
            rectTransform.sizeDelta = new Vector2(widthPixels, heightPixels);
        }
        public void ResizePixels(int width, int height) => Validate();
        public int WidthPixelsClamp(int width) => widthPixels;
        public int HeightPixelsClamp(int height) => heightPixels;
        public int XClamp(int x) => 0;
        public int YClamp(int y) => 0;
        public bool Attach(PixelElement element) => false;
        public bool Detach(PixelElement element) => false;
        internal void Finish(CustomUIElement control)
        {
            Control = control;
            if (control.root == null) throw new InvalidOperationException("Native AE control prefab is unavailable.");
            // Native Reskin replaces every Image, including dropdown arrows and checkmarks.
            // Preserve those real prefab decorations while applying the native locale font
            // and machinery frame. Public Image properties only; no private game fields.
            var decorations = new List<(Component Image, System.Reflection.PropertyInfo Property, object? Value)>();
            foreach (var component in control.root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.gameObject == control.root || component.GetType().FullName != "UnityEngine.UI.Image") continue;
                foreach (var name in new[] { "sprite", "type", "color", "pixelsPerUnitMultiplier" })
                {
                    var property = component.GetType().GetProperty(name);
                    if (property?.CanRead == true && property.CanWrite) decorations.Add((component, property, property.GetValue(component)));
                }
            }
            CustomUIMachineryStyle.Reskin(control.root);
            foreach (var decoration in decorations) decoration.Property.SetValue(decoration.Image, decoration.Value);
            Validate();
        }
    }
    internal static Field Input(string placeholder, string value, Action<string> change, int width = 160)
    {
        var element = new Field(width);
        var window = new CustomUIWindow { root = element.handler.gameObject };
        var builder = new CustomUIBuilder(CustomUIManager.Instance, window, element.rectTransform);
        builder.AddInput(placeholder, value, change, onTyping: change);
        element.Finish(builder.Last);
        return element;
    }
    internal static Field Dropdown(IList<string> labels, int selected, Action<int> change, int width = 110)
    {
        var element = new Field(width);
        var window = new CustomUIWindow { root = element.handler.gameObject };
        var builder = new CustomUIBuilder(CustomUIManager.Instance, window, element.rectTransform);
        builder.AddDropdown(labels, selected, change);
        element.Finish(builder.Last);
        return element;
    }
    internal static Field Toggle(string label, bool selected, Action<bool> change, int width = 110)
    {
        var element = new Field(width);
        var window = new CustomUIWindow { root = element.handler.gameObject };
        var builder = new CustomUIBuilder(CustomUIManager.Instance, window, element.rectTransform);
        builder.AddToggle(label, selected, change);
        element.Finish(builder.Last);
        return element;
    }
    internal static MachineryText Text(string text)
    {
        var label = MachineryText.Status("", ""); label.SetText(text); return label;
    }
    private sealed class IconView : EmptyPixelElement, TooltipListener
    {
        private readonly string? description;
        internal IconView(int width, int height, string? description) : base(width, height) { this.description = description; }
        public bool HasTooltip() => !string.IsNullOrEmpty(description);
        public RichText GetTooltip() => new RichTextBuilder().Add(description ?? "").Build();
        public RichText GetTooltipAdvanced() => GetTooltip();
    }
    internal static PixelElement Icon(Sprite? sprite, int width = 16, int height = 16, string? description = null)
    {
        var element = new IconView(width, height, description);
        if (sprite != null) { var node = TreeNodeRender.Instantiate().SetParent(element.handler); MachineryStyle.SetSimple(node, sprite, Color.white); MachineryStyle.Place(node, 0, 0, width, height); }
        return element;
    }
}
