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
    internal static bool Exact(GameItem bus)=>PartStorage.Int(bus,"io_exact")!=0;
    internal static bool Matches(GameItem bus,GameItem item)
    {
        var policy=PartStorage.Policy(bus);
        if(policy.Tags || !Exact(bus)) return policy.Matches(item.identifier,PartStorage.Tags(item));
        if(policy.Keys.Count==0) return true;
        var filters=PartStorage.Filters(bus); string? state=null; bool read=false,match=false;
        for(int slot=0;slot<policy.Slots;slot++)
        {
            if(filters[slot]!=item.identifier) continue;
            var expected=PartStorage.Text(bus,"filter_state_"+slot);
            if(expected.Length==0) continue;
            if(!read) { state=TerminalStackKey.Read(item); read=true; }
            if(state!=null && state==expected) { match=true; break; }
        }
        return policy.Exclude?!match:match;
    }
    internal static List<GameInventory> MachineSlots(GameItem machine,bool import)
    {
        var result=new List<GameInventory>(); var seen=new HashSet<PixelElement>();
        var root=machine.contentWindow?.childElement;
        if(root==null) return result;
        // MachineryPanel is a rendering container, not a storage graph node.
        // Its role is lost when the native save graph flattens the panel's inventories.
        var queue=new Queue<(PixelElement Node,bool Match)>(); queue.Enqueue((root,false));
        while(queue.Count>0)
        {
            var (node,match)=queue.Dequeue(); if(!seen.Add(node)) continue;
            if(node is GameInventory inv)
            {
                if(match && ReferenceEquals(inv.GetParentItem(),machine) && !inv.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG)) result.Add(inv);
                continue;
            }
            switch(node)
            {
                case MachineryPanelRow row:
                    foreach(var panel in row.Panels) queue.Enqueue((panel,match));
                    break;
                case MachineryPanel panel when panel.content!=null:
                    queue.Enqueue((panel.content,panel.role==null?match:panel.role==AutomationRules.MachineRole(import)));
                    break;
                case MachineryIntake intake when intake.content!=null:
                    queue.Enqueue((intake.content,match));
                    break;
                case MachineryStack stack:
                    foreach(var child in stack.elements) queue.Enqueue((child,match));
                    break;
            }
        }
        return result;
    }
}
