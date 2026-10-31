using System;
using System.Collections.Generic;
using DeepSpaceAE.Core.Network;
namespace DeepSpaceAE.GameAdapter;
internal static class IoBus
{
    internal const string Import="deepspaceae.import_bus",Export="deepspaceae.export_bus";
    internal static bool IsIo(GameItem item)=>item.identifier==Import || item.identifier==Export;
    internal static bool IsBus(GameItem item)=>item.identifier=="deepspaceae.item_storage_bus" || IsIo(item);
    internal static string FacePrefix(GameItem item)=>item.identifier==Import?"import-face-":item.identifier==Export?"export-face-":"bus-face-";
    internal static int Order(GameItem bus)=>Math.Max(1,Math.Min(15,PartStorage.Int(bus,"io_order",bus.identifier==Import?11:1)));
    internal static List<GameInventory> MachineSlots(GameItem machine,bool import)
    {
        var result=new List<GameInventory>(); var seen=new HashSet<GraphNodeStorage>();
        if(machine.child==null) return result;
        var queue=new Queue<(GraphNodeStorage Node,bool Match)>(); queue.Enqueue((machine.child,false));
        while(queue.Count>0)
        {
            var (node,match)=queue.Dequeue(); if(!seen.Add(node)) continue;
            if(seen.Count>2048) throw new InvalidOperationException("Machine inventory graph too large.");
            if(node is GameItem) continue;
            if(node is MachineryPanel panel && panel.role!=null) match=panel.role==AutomationRules.MachineRole(import);
            if(node is GameInventory inv)
            {
                if(match && ReferenceEquals(inv.GetParentItem(),machine) && !inv.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG)) result.Add(inv);
                continue;
            }
            foreach(var child in node.children) if(child!=null) queue.Enqueue((child,match));
        }
        return result;
    }
}
