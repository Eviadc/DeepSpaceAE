using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal static class NativeWidgets
{
    // The native slot exposes only atlas-based backgrounds. Bind its verified
    // background nodes to a mod sprite, retaining native empty/occupied handling.
    internal static void SlotGhost(GameSlotInventory slot,string key)
    {
        var sprite=Sprite(key); if(sprite==null) return;
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var field=typeof(GameSlotInventory).GetField("backgroundImage",flags)!;
        var node=(TreeNodeRender?)field.GetValue(slot);
        if(node==null)
        {
            node=TreeNodeRender.Instantiate().SetParent((TreeNodeRender)typeof(GameSlotInventory).GetField("highlight",flags)!.GetValue(slot)!);
            field.SetValue(slot,node);
        }
        MachineryStyle.SetSimple(node,sprite,new Color(.65f,.65f,.65f,.35f));
        node.rectTransform.sizeDelta=new Vector2(sprite.rect.width,sprite.rect.height);
        slot.Validate();
    }
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
    // The tag dictionary also stores machine parameters and accounting fields.
    // Only registered item categories have native type_* display names.
    internal static bool HasTagLabel(string tag) => TypeHelper.ALL_TYPES_LIST.Contains(tag) || tag=="deepspaceae.liquid";
    internal static string TagName(string tag)
    {
        if(tag=="deepspaceae.liquid") return UiText.Get("ui_liquids");
        if(tag==GameSession.CustomerFilter) return UiText.Get("ui_customer_demand");
        if (localeFrame != Time.frameCount)
        {
            localeFrame = Time.frameCount; string current = LocHelper.GetCurrentLocaleCode();
            if (current != locale) { locale = current; tagNames.Clear(); }
        }
        if (tagNames.TryGetValue(tag, out var name)) return name;
        name = HasTagLabel(tag) ? LocHelper.GetLocalizedMechanicOrDefault("type_"+tag.ToLowerInvariant(),tag) : "#"+tag;
        tagNames.Add(tag, name); return name;
    }
    internal static Sprite? Sprite(string name) => ModLoader.Mods.FirstOrDefault(m => m.Manifest.ID == ModEntry.ModId)?.Sprites.TryGetValue(name, out var image) == true ? image : null;
    // CustomUI machinery controls use twice the design-pixel scale of PixelWindow.
    // Keep their real prefab layout; scale the whole control instead of clipping 16px text into 18px.
    internal sealed class Field : PixelElement, MachineryWidget
    {
        internal CustomUIElement Control = null!;
        private readonly int minWidth;
        public bool fillWidth { get; set; }
        public bool canResize => false;
        public int widthPixels { get; private set; }
        public int heightPixels => 20;
        public TreeNodeRender handler { get; }
        public RectTransform rectTransform => handler.rectTransform;
        public TreeNodeRender inner => handler;
        internal Field(int width) { minWidth = widthPixels = width; handler = TreeNodeRender.Instantiate(this, "AE field"); }
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
        public void ResizePixels(int width, int height)
        { if(fillWidth) widthPixels=Math.Max(minWidth,width); Validate(); }
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
    internal static Field Dropdown(IList<string> labels, int selected, Action<int> change, int width = 110, int visibleOptions = 0)
    {
        var element = new Field(width);
        var window = new CustomUIWindow { root = element.handler.gameObject };
        var builder = new CustomUIBuilder(CustomUIManager.Instance, window, element.rectTransform);
        builder.AddDropdown(labels, selected, change);
        element.Finish(builder.Last);
        if(visibleOptions>0)
        {
            // Keep the native scrolling popup, capped to a few rows. Reflection
            // avoids distributing/referencing the game's TMPro/UI assemblies.
            var dropdown=typeof(CustomUIElement).GetField("dropdown")!.GetValue(element.Control)!;
            var type=dropdown.GetType();
            var template=(RectTransform)type.GetProperty("template")!.GetValue(dropdown)!;
            var item=(Component)type.GetProperty("itemText")!.GetValue(dropdown)!;
            float rowHeight=((RectTransform)item.transform.parent).rect.height;
            template.sizeDelta=new Vector2(template.sizeDelta.x,Math.Max(32,rowHeight)*visibleOptions+8);
        }
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
    internal sealed class StatusLine : EmptyPixelElement, TooltipListener
    {
        private readonly MachineryText label=Text("");
        private readonly TreeNodeRender textNode;
        private string value="";
        internal StatusLine(int width) : base(width,12)
        {
            label.handler.SetParent(handler);
            textNode=label.handler.transform.GetChild(0).GetComponent<TreeNodeRender>();
            var text=typeof(TreeNodeRender).GetProperty("text")!.GetValue(textNode)!;
            var type=text.GetType(); var overflow=type.GetProperty("overflowMode")!;
            overflow.SetValue(text,Enum.Parse(overflow.PropertyType,"Ellipsis"));
            type.GetProperty("enableWordWrapping")!.SetValue(text,false);
        }
        internal void SetText(string text)
        {
            value=text; label.SetText(text);
            // The fixed wrapper controls layout width; a long status is clipped
            // with an ellipsis and remains available through its native tooltip.
            MachineryStyle.Place(textNode,0,0,widthPixels,heightPixels);
        }
        public bool HasTooltip()=>!string.IsNullOrEmpty(value);
        public RichText GetTooltip()=>new RichTextBuilder().Add(value).Build();
        public RichText GetTooltipAdvanced()=>GetTooltip();
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
