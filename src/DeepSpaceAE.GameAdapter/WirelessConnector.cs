using System;
using System.Collections.Generic;
using System.Linq;
namespace DeepSpaceAE.GameAdapter;

internal static class WirelessConnector
{
    internal const string Id="deepspaceae.wireless_connector";
    internal static bool IsConnector(GameItem item)=>item.identifier==Id;
    internal static int Link(GameItem item)=>PartStorage.Int(item,"wireless_link");
    internal static IEnumerable<GameItem> All()=>PlayerStore.HasInstance
        ?PlayerStore.Instance.FindAllItem(false).Where(IsConnector).Distinct():Enumerable.Empty<GameItem>();
    internal static bool CanPair(GameItem a,GameItem b)=>a!=b && IsConnector(a) && IsConnector(b)
        && a.parentInventory!=null && b.parentInventory!=null && GeneralHelper.IsItemEffectivelyOwned(a)
        && GeneralHelper.IsItemEffectivelyOwned(b);
    internal static GameItem? Peer(GameItem item)=>All().FirstOrDefault(other=>other.GetUniqueID()==Link(item)
        && Link(other)==item.GetUniqueID() && CanPair(item,other));
    internal static void Create(GameItem item)
    {
        item.maySelectSlotItemFunc=(_,_,_)=>true;
        item.canSelectSlotItemFunc=(self,_,_)=>GeneralHelper.IsItemEffectivelyOwned(self);
        item.mayThisTargetItemFunc=CanPair; item.canThisTargetItemFunc=CanPair;
        item.onThisTargetItemFunc=(a,b)=>ModEntry.ActiveSession?.PairWireless(a,b);
        item.onSlotAddItemFunc+=(self,_,_)=>ModEntry.ActiveSession?.WirelessChanged(self);
        item.onSlotRemoveItemFunc+=(self,_,_)=>ModEntry.ActiveSession?.WirelessChanged(self);
    }
    internal static void Unlink(GameItem item)
    {
        var peer=Peer(item);
        PartStorage.Number(item,"wireless_link",0);
        if(peer!=null) PartStorage.Number(peer,"wireless_link",0);
    }
}

internal sealed partial class GameSession
{
    internal void WirelessChanged(GameItem item)
    { AutomationStructureChanged(); Visuals.Invalidate(); NativeStorageChanged(item,true); }
    internal void PairWireless(GameItem a,GameItem b)
    {
        if(Loading || writing || !WirelessConnector.CanPair(a,b)) return;
        WirelessConnector.Unlink(a); WirelessConnector.Unlink(b);
        PartStorage.Number(a,"wireless_link",b.GetUniqueID());
        PartStorage.Number(b,"wireless_link",a.GetUniqueID());
        WirelessChanged(a);
        AudioManager.Instance?.Play3("scanner_activate");
    }
    private void ShowWireless(GameItem item)
    {
        ClosePart(); displayedPart=item;
        var peer=WirelessConnector.Peer(item);
        string place=peer?.parentInventory?.GetParentItem()?.GetDisplayName(preferCustomName:true) ?? UiText.Get("ui_wireless_main");
        var elements=new List<PixelElement>{NativeWidgets.Text(UiText.Get("ui_wireless_hint")),
            NativeWidgets.Text(peer==null?UiText.Get("ui_wireless_unpaired"):UiText.Get("ui_wireless_paired",peer.GetUniqueID(),place))};
        var board=ReadBoard();
        bool online=board.Evaluation.Devices.TryGetValue(Id(item),out var state)
            && state.Status==DeepSpaceAE.Core.Network.DeviceStatus.Online && state.CoreId!=null && board.Powered.Contains(state.CoreId);
        elements.Add(NativeWidgets.Text(UiText.Get(online?"ui_interface_online":"ui_interface_offline")));
        elements.Add(new MachineryButton(UiText.Get("ui_wireless_unlink"),()=>{
            if(writing || !GeneralHelper.IsItemEffectivelyOwned(item)) return;
            WirelessConnector.Unlink(item); WirelessChanged(item); ShowWireless(item);
        },150,18));
        partWindow=MachineHelper.CreateMachineryWindow(true,MachineHelper.Panel(item.name,MachineryStack.Vertical(elements.ToArray()),"wireless",false));
        partWindow.closeCallback=ClosePart; partWindow.Show().Center();
    }
}
