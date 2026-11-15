using System;
using System.Collections.Generic;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

/// <summary>Independent native render-prefab tree; the only item reference is canonical hit identity.</summary>
internal static class NativeItemMirror
{
    internal static Sprite? CurrentSprite(GameItemElement item) => item.background.image?.sprite;

    // Copy only view state through the public UI API. No runtime type lookup,
    // allocations of boxed property values, or ownership/parent changes.
    private static void CopyProperties(TreeNodeRender source, TreeNodeRender target)
    {
        var mask = source.mask; var targetMask = target.mask;
        if (mask != null && targetMask != null)
        { targetMask.enabled = mask.enabled; targetMask.padding = mask.padding; }
        var image = source.image; var targetImage = target.image;
        if (image != null && targetImage != null)
        {
            targetImage.sprite = image.sprite; targetImage.color = image.color;
            targetImage.material = image.material; targetImage.type = image.type;
            targetImage.preserveAspect = image.preserveAspect; targetImage.fillCenter = image.fillCenter;
            targetImage.fillMethod = image.fillMethod; targetImage.fillAmount = image.fillAmount;
            targetImage.fillClockwise = image.fillClockwise; targetImage.fillOrigin = image.fillOrigin;
            targetImage.pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier;
            targetImage.alphaHitTestMinimumThreshold = image.alphaHitTestMinimumThreshold;
            targetImage.raycastTarget = image.raycastTarget; targetImage.enabled = image.enabled;
        }
        var text = source.text; var targetText = target.text;
        if (text != null && targetText != null)
        {
            targetText.font = text.font; targetText.fontSharedMaterial = text.fontSharedMaterial;
            targetText.fontSize = text.fontSize; targetText.fontStyle = text.fontStyle;
            targetText.alignment = text.alignment; targetText.enableAutoSizing = text.enableAutoSizing;
            targetText.fontSizeMin = text.fontSizeMin; targetText.fontSizeMax = text.fontSizeMax;
            targetText.enableWordWrapping = text.enableWordWrapping; targetText.overflowMode = text.overflowMode;
            targetText.richText = text.richText; targetText.color = text.color; targetText.text = text.text;
            targetText.raycastTarget = text.raycastTarget; targetText.enabled = text.enabled;
        }
    }
    internal static TreeNodeRender Copy(TreeNodeRender source, TreeNodeRender parent, GameItemElement? identity = null)
    {
        // Factories initialize TreeNodeRender's parent/rect/image/text references properly.
        // Object.Instantiate(source.gameObject) would leave its managed properties uninitialized.
        var text = source.text;
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
        CopyProperties(source, node);
        var originals = Children(source); var copies = Children(node);
        bool rebuild = originals.Count != copies.Count;
        if (!rebuild) for (int i = 0; i < originals.Count; i++)
            if ((originals[i].text != null) != (copies[i].text != null)) { rebuild = true; break; }
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
