using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using TMPro;

namespace DeepSpaceAE.GameAdapter;

/// <summary>A native-font quantity overlay; all hit nodes still name the one real top item.</summary>
internal sealed class NativeStackBadge
{
    private const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    private static readonly FieldInfo countField=typeof(GameItemElement).GetField("stackCountText",Fields)!;
    private static readonly FieldInfo shadowField=typeof(GameItemElement).GetField("stackCountTextBackground",Fields)!;
    private readonly GameItemElement item;
    private readonly TreeNodeRender root;
    private readonly TreeNodeRender nativeCount,nativeShadow;
    private readonly TextMeshProUGUI countText,shadowText;
    private bool visible=true,showSingle;
    private long quantity;
    internal NativeStackBadge(GameItemElement item,TreeNodeRender parent,int width,int height,bool raycast=true)
    {
        this.item=item;
        nativeCount=(TreeNodeRender)countField.GetValue(item)!;
        nativeShadow=(TreeNodeRender)shadowField.GetValue(item)!;
        root=TreeNodeRender.Instantiate(item,"AE display stack count").SetParent(parent);
        root.image.enabled=false;
        MachineryStyle.Place(root,0,0,width,height);
        var shadow=NativeItemMirror.Copy(nativeShadow,root);
        var count=NativeItemMirror.Copy(nativeCount,root);
        shadow.gameObject.SetActive(true); count.gameObject.SetActive(true);
        MachineryStyle.Place(shadow,1,Math.Max(0,height-10),Math.Max(1,width-2),10);
        MachineryStyle.Place(count,0,Math.Max(0,height-11),Math.Max(1,width-2),10);
        countText=Configure(count,Color.white,raycast);
        shadowText=Configure(shadow,new Color(41/255f,34/255f,52/255f,1),false);
    }
    private static TextMeshProUGUI Configure(TreeNodeRender node,Color color,bool raycast)
    {
        var text=node.text;
        text.color=color; text.fontSize=8f; text.fontSizeMin=4f; text.fontSizeMax=8f;
        text.enableAutoSizing=true; text.enableWordWrapping=false;
        text.raycastTarget=raycast; text.enabled=true; text.alignment=TextAlignmentOptions.BottomRight;
        return text;
    }
    internal void Update(TreeNodeRender mirror,long quantity)
    {
        // customText uses the same native nodes for labels, not just quantities.
        // Hide only the mirrored native unit counter, never the real renderer.
        if(string.IsNullOrEmpty(item.customText))
        { HideCopy(nativeCount,mirror); HideCopy(nativeShadow,mirror); }
        Update(quantity);
    }
    internal void Update(long quantity,bool showSingle=false)
    {
        this.quantity=quantity; this.showSingle=showSingle;
        root.gameObject.SetActive(visible && quantity>(showSingle?0:1));
        string value=quantity.ToString(CultureInfo.InvariantCulture);
        countText.text=value;
        shadowText.text=value;
    }
    internal void SetVisibility(bool enabled,Color tint)
    {
        visible=enabled;
        root.gameObject.SetActive(enabled && quantity>(showSingle?0:1));
        countText.color=tint;
        shadowText.color=new Color(tint.r*41/255f,tint.g*34/255f,tint.b*52/255f,tint.a);
    }
    private void HideCopy(TreeNodeRender source,TreeNodeRender mirror)
    {
        var path=new Stack<int>(); var cursor=source.transform;
        while(cursor!=item.middle.transform)
        { if(cursor.parent==null) return; path.Push(cursor.GetSiblingIndex()); cursor=cursor.parent; }
        var copy=mirror.transform;
        while(path.Count>0) copy=copy.GetChild(path.Pop());
        copy.gameObject.SetActive(false);
    }
}
