using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;
namespace DeepSpaceAE.GameAdapter;
internal static class IoBus
{
    internal const string Import="deepspaceae.import_bus",Export="deepspaceae.export_bus",
        Formation="deepspaceae.formation_bus",Annihilation="deepspaceae.annihilation_bus";
    internal static bool IsPlane(GameItem item)=>item.identifier==Formation || item.identifier==Annihilation;
    internal static bool IsIo(GameItem item)=>item.identifier==Import || item.identifier==Export;
    internal static bool IsAutomation(GameItem item)=>IsIo(item) || IsPlane(item);
    internal static bool IsImport(GameItem item)=>item.identifier==Import || item.identifier==Annihilation;
    internal static bool WholeSpace(GameItem item)=>PartStorage.Int(item,"plane_whole")!=0;
    internal static bool IsBus(GameItem item)=>item.identifier=="deepspaceae.item_storage_bus" || IsAutomation(item);
    internal static string FacePrefix(GameItem item)=>(PartModes.IsFluid(item)?"fluid-":"")+(item.identifier==Import?"import-face-":item.identifier==Export?"export-face-":item.identifier==Formation?"formation-face-":item.identifier==Annihilation?"annihilation-face-":"bus-face-");
    internal static int Order(GameItem bus)=>Math.Max(1,Math.Min(15,PartStorage.Int(bus,"io_order",IsImport(bus)?11:1)));
    internal static bool CollectOverflow(GameItem bus)=>PartStorage.Int(bus,"io_overflow",1)!=0;
    internal static bool Exact(GameItem bus)=>PartStorage.Int(bus,"io_exact")!=0;
    internal static IEnumerable<GameItem> LiquidTargets(GameItem machine,bool import)
    {
        if(LiquidStorage.IsContainer(machine)) return new[]{machine};
        var slots=!import && !DrawerStorage.IsDrawer(machine)
            ? FindSlots(machine,false,true,fluidInput:true) : TargetSlots(machine,import);
        return slots.SelectMany(slot=>slot.childItems).Where(LiquidStorage.IsContainer).Distinct();
    }
    internal static bool Matches(GameItem bus,GameItem item)
    {
        var policy=PartStorage.Policy(bus);
        if(PartModes.IsFluid(bus) && IsIo(bus) && !policy.Tags)
        {
            if(policy.Keys.Count==0) return policy.Exclude;
            if(!LiquidStorage.IsContainer(item)) return false;
            var liquid=LiquidStorage.Read(item).Composition;
            string actual=Exact(bus)?liquid.Signature:string.Join("\n",liquid.Parts.Select(p=>p.Key).OrderBy(k=>k,StringComparer.Ordinal));
            string field=Exact(bus)?"filter_liquid_":"filter_liquid_kinds_";
            var samples=PartStorage.Filters(bus); bool fluidMatch=false;
            for(int slot=0;slot<policy.Slots;slot++)
            {
                if(samples[slot].Length==0) continue;
                string expected=PartStorage.Text(bus,field+slot);
                if(expected.Length>0 && expected==actual) { fluidMatch=true; break; }
            }
            return policy.Exclude?!fluidMatch:fluidMatch;
        }
        if(policy.Tags || !Exact(bus)) return policy.Matches(item.identifier,PartStorage.Tags(item));
        if(policy.Keys.Count==0) return policy.Exclude;
        var filters=PartStorage.Filters(bus); string? state=null; bool read=false,match=false;
        for(int slot=0;slot<policy.Slots;slot++)
        {
            if(filters[slot]!=item.identifier) continue;
            var expected=PartStorage.Text(bus,"filter_state_"+slot);
            if(expected.Length==0) continue;
            if(!read) { state=ExactItemFilter.Read(item); read=true; }
            if(state!=null && state==ExactItemFilter.Normalize(expected)) { match=true; break; }
        }
        return policy.Exclude?!match:match;
    }
    // Custom machines can mark an inventory even when their UI has no native panel roles.
    internal const string OutputInventoryTag="deepspaceae.machine_output";
    internal static bool IsProductionOutput(GameItem owner,GameInventory inventory) =>
        inventory.IsTag(OutputInventoryTag) || MachineSlots(owner,true).Contains(inventory);

    internal static List<GameInventory> MachineSlots(GameItem machine,bool import)=>FindSlots(machine,import,false);
    internal static List<GameInventory> TargetSlots(GameItem owner,bool import)
    {
        // A drawer's upgrade bay is configuration, never transferable stock.
        if(FluidDrawer.IsDrawer(owner)) return new();
        if(DrawerStorage.IsDrawer(owner))
        {
            var data=DrawerStorage.Data(owner);
            return data.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG)?new():new(){data};
        }
        return FindSlots(owner,import,true);
    }
    private static List<GameInventory> FindSlots(GameItem machine,bool import,bool includeStorage,bool fluidInput=false)
    {
        var result=new List<GameInventory>(); var seen=new HashSet<PixelElement>();
        var root=machine.contentWindow?.childElement;
        if(root==null) return result;
        // MachineryPanel is a rendering container, not a storage graph node.
        // Its role is lost when the native save graph flattens the panel's inventories.
        bool ordinary=includeStorage && !machine.IsTag(MachineryHelper.MACHINERY_TAG)
            && (machine.IsTag(ContainerHelper.CONTAINER_TAG) || machine.IsTag(ContainerHelper.BACKPACK_TAG) || machine.IsTag("STORAGE"));
        bool machineStorage=import && (machine.IsGameItemType("MACHINE") || machine.IsTag(MachineryHelper.MACHINERY_TAG));
        var queue=new Queue<(PixelElement Node,bool Match)>(); queue.Enqueue((root,ordinary));
        while(queue.Count>0)
        {
            var (node,match)=queue.Dequeue(); if(!seen.Add(node)) continue;
            if(node is GameInventory inv)
            {
                if((match || import && inv.IsTag(OutputInventoryTag)) && ReferenceEquals(inv.GetParentItem(),machine) && !inv.IsTag(HopperHelper.NO_HOPPER_INVENTORY_TAG)) result.Add(inv);
                continue;
            }
            if(node is GameItem) continue; // Never recurse into stored containers.
            switch(node)
            {
                case MachineryPanelRow row:
                    foreach(var panel in row.Panels) queue.Enqueue((panel,match));
                    break;
                case MachineryPanel panel when panel.content!=null:
                    queue.Enqueue((panel.content,panel.role==null?match:panel.role==AutomationRules.MachineRole(import)
                        || (includeStorage || machineStorage) && panel.role=="storage" || fluidInput && panel.role is ("container" or "water")));
                    break;
                case MachineryIntake intake when intake.content!=null:
                    queue.Enqueue((intake.content,match));
                    break;
                case MachineryStack stack:
                    foreach(var child in stack.elements) queue.Enqueue((child,match));
                    break;
                case MachineryTabs tabs:
                    for(int i=0;i<tabs.count;i++) if(tabs.GetPage(i) is PixelElement page) queue.Enqueue((page,match));
                    break;
                case GraphNodeStorage graph:
                    foreach(var child in graph.children.OfType<PixelElement>()) queue.Enqueue((child,match));
                    break;
            }
        }
        return result;
    }
}
