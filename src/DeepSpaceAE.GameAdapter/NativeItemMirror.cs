using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

/// <summary>Independent native render-prefab tree; the only item reference is canonical hit identity.</summary>
internal static class NativeItemMirror
{
    private static readonly Dictionary<(Type, string), PropertyInfo?> properties = new();
    private static readonly string[] maskProperties = { "enabled", "padding" };
    private static readonly string[] imageProperties = { "sprite", "color", "material", "type", "preserveAspect", "fillCenter", "fillMethod", "fillAmount", "fillClockwise", "fillOrigin", "pixelsPerUnitMultiplier", "alphaHitTestMinimumThreshold", "raycastTarget", "enabled" };
    private static readonly string[] textProperties = { "font", "fontSharedMaterial", "fontSize", "fontStyle", "alignment", "enableAutoSizing", "fontSizeMin", "fontSizeMax", "enableWordWrapping", "overflowMode", "richText", "color", "text", "raycastTarget", "enabled" };
    private static object? Get(object node, string name)
    {
        var key = (node.GetType(), name);
        if (!properties.TryGetValue(key, out var property))
        { property = key.Item1.GetProperty(name, BindingFlags.Public | BindingFlags.Instance); properties.Add(key, property); }
        return property?.CanRead == true ? property.GetValue(node) : null;
    }
    private static void CopyProperties(object? from, object? to, string[] names)
    {
        if (from == null || to == null) return;
        foreach (var name in names)
        {
            var value = Get(from, name);
            var key = (to.GetType(), name);
            if (!properties.TryGetValue(key, out var property))
            { property = key.Item1.GetProperty(name, BindingFlags.Public | BindingFlags.Instance); properties.Add(key, property); }
            if (property?.CanWrite == true) property.SetValue(to, value);
        }
    }
    internal static TreeNodeRender Copy(TreeNodeRender source, TreeNodeRender parent, GameItemElement? identity = null)
    {
        // Factories initialize TreeNodeRender's parent/rect/image/text references properly.
        // Object.Instantiate(source.gameObject) would leave its managed properties uninitialized.
        var text = Get(source, "text");
        var node = identity != null ? TreeNodeRender.Instantiate(identity, "AE native item mirror")
            : text != null ? TreeNodeRender.InstantiateText() : TreeNodeRender.Instantiate();
        try
        {
            node.SetParent(parent);
            Sync(source, node);
            return node;
        }
        catch { node.gameObject.SetActive(false); UnityEngine.Object.Destroy(node.gameObject); throw; }
    }
    internal static void Sync(TreeNodeRender source, TreeNodeRender node)
    {
        var from = source.rectTransform; var to = node.rectTransform;
        to.anchorMin = from.anchorMin; to.anchorMax = from.anchorMax; to.pivot = from.pivot;
        to.sizeDelta = from.sizeDelta; to.anchoredPosition = from.anchoredPosition;
        to.localScale = from.localScale; to.localRotation = from.localRotation;
        if (source.eventHandler != null && node.eventHandler == null) node.InitEventHandler();
        CopyProperties(Get(source, "mask"), Get(node, "mask"), maskProperties);
        CopyProperties(Get(source, "image"), Get(node, "image"), imageProperties);
        CopyProperties(Get(source, "text"), Get(node, "text"), textProperties);
        var originals = Children(source); var copies = Children(node);
        bool rebuild = originals.Count != copies.Count;
        if (!rebuild) for (int i = 0; i < originals.Count; i++)
            if ((Get(originals[i], "text") != null) != (Get(copies[i], "text") != null)) { rebuild = true; break; }
        if (rebuild)
        {
            foreach (var child in copies)
            { child.gameObject.SetActive(false); child.transform.SetParent(null); UnityEngine.Object.Destroy(child.gameObject); }
            foreach (var child in originals) Copy(child, node);
        }
        else for (int i = 0; i < originals.Count; i++) Sync(originals[i], copies[i]);
        node.gameObject.SetActive(source.gameObject.activeSelf);
    }
    private static List<TreeNodeRender> Children(TreeNodeRender node)
    {
        var result = new List<TreeNodeRender>();
        for (int i = 0; i < node.transform.childCount; i++)
        {
            var child = node.transform.GetChild(i).GetComponent<TreeNodeRender>();
            if (child != null) result.Add(child);
        }
        return result;
    }
}
