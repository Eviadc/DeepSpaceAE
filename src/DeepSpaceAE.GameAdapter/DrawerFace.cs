using System;
using HarmonyLib;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

/// <summary>Two view-only labels on the real/fake drawer renderers; no saved item or texture copies.</summary>
internal sealed class DrawerFace
{
    private readonly GameItemElement drawer;
    private readonly TreeNodeRender[] images;
    private readonly NativeStackBadge[] badges;
    private Sprite? sprite;
    private long count=-1;
    private readonly bool[] visible={true,false};
    private readonly Color[] tints={Color.white,Color.white};
    internal DrawerFace(GameItemElement drawer)
    {
        this.drawer=drawer;
        images=new[] { TreeNodeRender.Instantiate(drawer,"AE drawer item label").SetParent(drawer.rotator),
            TreeNodeRender.Instantiate(drawer,"AE drawer drag item label").SetParent(drawer.fakeRotator) };
        badges=new[] { new NativeStackBadge(drawer,drawer.middle,drawer.widthPixels,drawer.heightPixels),
            new NativeStackBadge(drawer,drawer.fakeMiddle,drawer.widthPixels,drawer.heightPixels,raycast:false) };
        foreach(var image in images) image.gameObject.SetActive(false);
        foreach(var badge in badges) badge.Update(0,showSingle:true);
        badges[1].SetVisibility(false,Color.white);
    }
    internal void Update(GameItem? item,long quantity)
    {
        Sprite? next=item is GameItemElement element?NativeItemMirror.CurrentSprite(element):null;
        if(next==sprite && quantity==count) return;
        if(next!=sprite)
        {
            sprite=next;
            for(int i=0;i<images.Length;i++)
            {
                var image=images[i]; image.gameObject.SetActive(visible[i] && next!=null);
                if(next==null) continue;
                Paint(image,next,tints[i]);
            }
        }
        count=quantity;
        foreach(var badge in badges) badge.Update(quantity,showSingle:true);
        VirtualInventory.ItemTile.Changed(drawer);
    }
    internal void SetVisibility(bool fake,bool enabled,Color tint)
    {
        int i=fake?1:0;
        if(visible[i]==enabled && tints[i]==tint) return;
        visible[i]=enabled; tints[i]=tint;
        images[i].gameObject.SetActive(enabled && sprite!=null);
        if(sprite!=null) Paint(images[i],sprite,tint);
        badges[i].SetVisibility(enabled,tint);
        VirtualInventory.ItemTile.Changed(drawer);
    }
    private void Paint(TreeNodeRender node,Sprite sprite,Color tint)
    {
        MachineryStyle.SetSimple(node,sprite,tint);
        // SetSimple resets sizeDelta to the source sprite dimensions, including
        // when drag visibility changes. Restore the thumbnail after every paint.
        float scale=Math.Min(20f/sprite.rect.width,20f/sprite.rect.height);
        int width=Math.Max(1,(int)Math.Round(sprite.rect.width*scale)),height=Math.Max(1,(int)Math.Round(sprite.rect.height*scale));
        MachineryStyle.Place(node,(drawer.widthPixels-width)/2,(drawer.heightPixels-height)/2,width,height);
        // The native drawer body receives input; especially the drag preview
        // must never cover the inventory's drop target with a new raycast hit.
        var image=typeof(TreeNodeRender).GetProperty("image")!.GetValue(node)!;
        image.GetType().GetProperty("raycastTarget")!.SetValue(image,false);
    }
}

// Native toggles change individual Images, not the parent object. Follow both
// real and preview toggles so labels cannot remain behind or float over an idle drawer.
[HarmonyPatch(typeof(GameItemElement),nameof(GameItemElement.ToggleBackground),new[]{typeof(bool),typeof(float),typeof(float)})]
internal static class DrawerFaceVisibilityPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItemElement __instance,bool __0,float __1,float __2)
        =>DrawerStorage.FaceVisibility(__instance,false,__0,new Color(__2,__2,__2,__1));
}
[HarmonyPatch(typeof(GameItemElement),nameof(GameItemElement.ToggleFakeBackground),new[]{typeof(bool),typeof(Vector4)})]
internal static class DrawerFacePreviewPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameItemElement __instance,bool __0,Vector4 __1)
        =>DrawerStorage.FaceVisibility(__instance,true,__0,new Color(__1.x,__1.y,__1.z,__1.w));
}
