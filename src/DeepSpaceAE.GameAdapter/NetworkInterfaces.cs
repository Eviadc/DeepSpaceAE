using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;
namespace DeepSpaceAE.GameAdapter;

internal sealed partial class GameSession
{
    private bool interfaceQueued,interfacePumping;
    private int interfaceGeneration;
    private readonly Dictionary<GameItem,string> interfaceLiquids=new();
    private View ReadInterface(GameItem device)
    {
        if(Loading || !ReferenceEquals(device.parentInventory,MainInventory()) || !GeneralHelper.IsItemEffectivelyOwned(device))
            throw new Rejected(UiText.Get("error_part_main"));
        var board=ReadBoard();
        if(!board.Evaluation.Devices.TryGetValue(Id(device),out var connection) || connection.Status!=DeviceStatus.Online || connection.CoreId==null)
            throw new Rejected(UiText.Get("ui_interface_offline"));
        if(!board.Powered.Contains(connection.CoreId)) throw new Rejected(UiText.Get("error_network_power"));
        return ReadStorage(board,connection.CoreId);
    }
    private SlotMarker InterfacePlacement(View view,GameItem item)
    {
        var devices=view.Policies.Keys.ToDictionary(Id,StringComparer.Ordinal);
        var policies=devices.ToDictionary(p=>p.Key,p=>view.Policies[p.Value],StringComparer.Ordinal);
        foreach(var id in StorageRouting.InsertionOrder(policies,item.identifier,PartStorage.Tags(item)))
        {
            var owner=view.Owners[devices[id]];
            try { return Placement(item,view.Inventories[owner]); }
            catch(Rejected) { }
        }
        throw new Rejected(UiText.Get("error_network_no_slot"));
    }
    internal int PreviewInterface(GameItem device,GameItem item)
    {
        if(Loading || item==null || item.unitCount<=0 || !GeneralHelper.IsItemEffectivelyOwned(item)) return 0;
        var state=InterfaceStorage.Get(device);
        if(ReferenceEquals(state.PreviewItem,item) && state.PreviewRevision==Visuals.Revision && state.PreviewUnits==item.unitCount) return state.Preview;
        try { state.Preview=InterfacePlacement(ReadInterface(device),item).numTransfer; }
        catch(Rejected) { state.Preview=0; }
        catch(Exception e) { state.Preview=0; log.Warning(Diagnostics.ConsoleText(e.ToString())); }
        state.PreviewItem=item; state.PreviewRevision=Visuals.Revision; state.PreviewUnits=item.unitCount;
        return state.Preview;
    }
    internal int DepositInterface(GameItem device,GameItem item,int amount)
    {
        if(Loading || writing) return 0;
        writing=true;
        try
        {
            if(!GeneralHelper.IsItemEffectivelyOwned(item)) throw new Rejected(UiText.Get("error_not_owned"));
            // Preview is never a write authorization. Resolve the real destination
            // once at release, then use native partial/stack transfer callbacks.
            var slot=InterfacePlacement(ReadInterface(device),item);
            int accepted=slot.TryAcceptOnce(amount);
            PartStorage.Number(device,"interface_moved",accepted);
            PartStorage.String(device,"interface_status",accepted>0?"":UiText.Get("error_drop_here"));
            return accepted;
        }
        catch(Exception e)
        {
            PartStorage.String(device,"interface_status",e is Rejected?e.Message:UiText.Get("error_operation"));
            log.Warning(Diagnostics.ConsoleText(e.ToString())); return 0;
        }
        finally { writing=false; Visuals.Invalidate(); NativeStorageChanged(device); }
    }
    internal void QueueInterfaces()
    {
        if(Loading || interfaceQueued || interfacePumping || MainInventory()?.childItems.Any(InterfaceStorage.IsFluid)!=true) return;
        interfaceQueued=true; int expected=interfaceGeneration;
        LetterboxManager.RunNextUpdate(()=>{
            if(expected!=interfaceGeneration) return;
            interfaceQueued=false;
            if(writing || ItemMouseDragHandler.current?.IsDraggingItem==true || ItemMultiSelectHandler.current?.IsGroupDragging==true) return;
            PumpInterfaces();
        });
    }
    internal void InterfaceItemChanged(GameItem item)
    {
        if(Loading || MainInventory()==null) return;
        if(!(InterfaceStorage.IsFluid(item) && ReferenceEquals(item.parentInventory,MainInventory())) && !interfaceLiquids.ContainsKey(item)) return;
        if(!LiquidStorage.IsContainer(item)) return;
        string stamp=LiquidStorage.Read(item).Stamp;
        if(interfaceLiquids.TryGetValue(item,out var old) && stamp==old) return;
        interfaceLiquids[item]=stamp; QueueInterfaces();
    }
    private IEnumerable<GameItem> LiquidRoutes(View view,bool extract)
        =>view.LiquidPolicies.Where(p=>p.Value.Any(policy=>extract?policy.CanExtract(p.Key.identifier,PartStorage.Tags(p.Key)):policy.CanInsert(p.Key.identifier,PartStorage.Tags(p.Key))))
            .OrderByDescending(p=>p.Value.Where(policy=>extract?policy.CanExtract(p.Key.identifier,PartStorage.Tags(p.Key)):policy.CanInsert(p.Key.identifier,PartStorage.Tags(p.Key))).Max(p=>p.Priority))
            .ThenBy(p=>p.Key.GetUniqueID()).Select(p=>p.Key);
    internal void PumpInterfaces()
    {
        if(Loading || writing || interfacePumping || MainInventory()==null) return;
        writing=true; interfacePumping=true;
        try
        {
            var board=ReadBoard(); int generation=automationGeneration,operations=0;
            var views=new Dictionary<string,View>(StringComparer.Ordinal); interfaceLiquids.Clear();
            foreach(var device in board.Layout.Parts.Where(InterfaceStorage.IsFluid))
            {
                if(operations>=64 || generation!=automationGeneration) break;
                if(!board.Evaluation.Devices.TryGetValue(Id(device),out var connection) || connection.Status!=DeviceStatus.Online || connection.CoreId==null
                    || !board.Powered.Contains(connection.CoreId)) continue;
                if(!views.TryGetValue(connection.CoreId,out var view)) views.Add(connection.CoreId,view=ReadStorage(board,connection.CoreId));
                foreach(var owner in view.LiquidPolicies.Keys) interfaceLiquids[owner]=LiquidStorage.Read(owner).Stamp;
                interfaceLiquids[device]=LiquidStorage.Read(device).Stamp;
                bool output=InterfaceStorage.Mode(device)==1; string filter=InterfaceStorage.Output(device);
                int attempts=0;
                foreach(var container in LiquidRoutes(view,output))
                {
                    if(attempts>=16 || operations>=64 || generation!=automationGeneration) break;
                    if(!WaterHelper.CanUseWaterContainer(device) || !WaterHelper.CanUseWaterContainer(container)) continue;
                    var buffer=LiquidStorage.Read(device); var liquid=LiquidStorage.Read(container);
                    GameItem source,target;
                    if(output)
                    {
                        if(filter.Length==0 || buffer.Composition.Volume>=buffer.Capacity || buffer.Composition.Volume>0 && buffer.Composition.Signature!=filter) break;
                        if(liquid.Composition.Volume==0 || liquid.Composition.Signature!=filter) continue;
                        source=container; target=device;
                    }
                    else
                    {
                        if(buffer.Composition.Volume==0) break;
                        if(liquid.Composition.Volume>=liquid.Capacity || InterfaceStorage.Strict(device) && liquid.Composition.Volume>0
                            && liquid.Composition.Signature!=buffer.Composition.Signature) continue;
                        source=device; target=container;
                    }
                    attempts++; operations++;
                    WaterHelper.TransferLiquid(source,target);
                }
            }
        }
        catch(Exception e) { log.Warning("AE interface transfer failed: "+Diagnostics.ConsoleText(e.ToString())); }
        finally { writing=false; Visuals.Invalidate(); NativeStorageChanged(); interfacePumping=false; }
    }
    private void ResetInterfaces()
    { interfaceGeneration++; interfaceQueued=false; interfacePumping=false; interfaceLiquids.Clear(); }
}
