using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
namespace DeepSpaceAE.GameAdapter;
internal static class EnergyReceiver
{
    internal const string Id="deepspaceae.energy_receiver";
    private sealed class Data { internal GameSlotInventory[] Slots=null!; }
    private static readonly ConditionalWeakTable<GameItem,Data> data=new();
    private static readonly List<WeakReference<GameItem>> hosts=new();
    internal static bool IsReceiver(GameItem item)=>item.identifier==Id;
    internal static void RegisterHost(GameItem host)=>hosts.Add(new WeakReference<GameItem>(host));
    internal static IEnumerable<GameItem> Hosts()
    {
        hosts.RemoveAll(w=>!w.TryGetTarget(out var item) || (item is GameItemElement e && e.IsDestroyed()));
        return hosts.Select(w=>w.TryGetTarget(out var item)?item:null).Where(i=>i!=null).Cast<GameItem>().Distinct().ToArray();
    }
    internal static GameSlotInventory[] Slots(GameItem host)=>IsReceiver(host)?data.GetValue(host,_=>throw new InvalidOperationException("Missing receiver graph.")).Slots:
        new[]{(GameSlotInventory)PartStorage.Inventory(host)};
    internal static IEnumerable<GameItem> Sources()=>Hosts().SelectMany(h=>Slots(h)).Select(s=>s.childItem).Where(i=>i!=null);
    internal static void Create(GameItem host)
    {
        var slots=new GameSlotInventory[3]; var grid=new GridPixelElement(3,1,true).SetSpacing(4,4);
        for(int i=0;i<3;i++)
        {
            var slot=new GameSlotInventory(); slot.SetMachineryStyle(); slot.SetBackgroundFadeSprite("Items/items_tool","battery100");
            slot.mayInventoryAddItemFunc=(source,_)=>slot.childItem==null && ReferenceEquals(host.parentInventory,GameSession.MainInventory())
                && GeneralHelper.IsItemEffectivelyOwned(host) && PartStorage.IsPowerItem(source) && PartStorage.CanRemoveSource(source);
            slot.maxSlotRemoveItemFunc=(source,_,_)=>PartStorage.CanRemoveSource(source)?int.MaxValue:0;
            void Changed(GameItem _,GameInventory __,SlotMarker ___)
            { ModEntry.ActiveSession?.NativeStorageChanged(host,true); ModEntry.ActiveSession?.Visuals.Invalidate(); }
            slot.onSlotAddItemEndFunc+=Changed; slot.onSlotRemoveItemEndFunc+=Changed;
            slots[i]=slot; grid.AttachPos(slot,i,0);
        }
        data.Add(host,new Data{Slots=slots}); RegisterHost(host);
        var window=new PixelWindow(true,UiText.Get("ui_receiver_slots")); window.Attach(grid); host.SetContentWindow(window);
    }
}
