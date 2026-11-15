using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;

namespace DeepSpaceAE.GameAdapter;

// Each inventory keeps its own coordinate space. A wireless pair is an ordinary
// graph edge, so loops, multiple cores, routes and power share the wired rules.
internal sealed class NetworkLayout
{
    internal readonly List<GameItem> Items=new(),Parts=new();
    internal readonly HashSet<GameItem> Owned=new();
    internal readonly Dictionary<GameItem,GameItem> BusCables=new();
    internal readonly HashSet<GameInventory> Inventories=new();
    private readonly Dictionary<GameItem,InventoryLayout> locations=new();
    private readonly List<InventoryLayout> layouts=new();
    internal static bool IsMachineBay(GameItem? item)=>item?.identifier is "machine_bay" or "machine_bay_ext";
    internal NetworkLayout(GameGridInventory root)
    {
        Inventories.Add(root);
        // Machine bays are working areas even without a wireless connector.
        // Keep each native page in its own coordinate space.
        foreach(var bay in PlayerStore.Instance.FindAllItem(false).Where(IsMachineBay).Where(GeneralHelper.IsItemEffectivelyOwned).Distinct())
            foreach(var inventory in GameSession.Contents(bay)) Inventories.Add(inventory);
        foreach(var connector in WirelessConnector.All())
            if(connector.parentInventory!=null && GeneralHelper.IsItemEffectivelyOwned(connector)) Inventories.Add(connector.parentInventory);
        foreach(var inventory in Inventories)
        {
            var layout=new InventoryLayout(inventory); layouts.Add(layout);
            Items.AddRange(layout.Items); Parts.AddRange(layout.Parts); Owned.UnionWith(layout.Owned);
            foreach(var part in layout.Parts) locations.Add(part,layout);
            foreach(var bus in layout.BusCables) BusCables.Add(bus.Key,bus.Value);
        }
    }
    internal GameItem? FrontOwner(GameItem bus)=>locations.TryGetValue(bus,out var layout)?layout.FrontOwner(bus):null;
    internal GridCell? FrontCell(GameItem bus)=>locations.TryGetValue(bus,out var layout)?layout.FrontCell(bus):null;
    internal GameInventory? InventoryOf(GameItem part)=>locations.TryGetValue(part,out var layout)?layout.Inventory:null;
    internal int CableMask(GameItem cable)=>locations.TryGetValue(cable,out var layout)?layout.CableMask(cable):0;
    internal IEnumerable<GameItem> DrawerMembers(GameItem controller)=>locations.TryGetValue(controller,out var layout)
        ?layout.DrawerMembers(controller).OrderBy(i=>i.GetUniqueID()):Enumerable.Empty<GameItem>();
    internal IEnumerable<(int X,int Y,int Side)> DevicePorts(GameItem part)=>locations.TryGetValue(part,out var layout)
        ?layout.DevicePorts(part):Enumerable.Empty<(int,int,int)>();
    internal IEnumerable<NetworkLink> Edges(Func<GameItem,string> id)
    {
        foreach(var layout in layouts) foreach(var edge in layout.Edges(id)) yield return edge;
        var connectors=Parts.Where(WirelessConnector.IsConnector).ToDictionary(c=>c.GetUniqueID());
        foreach(var connector in connectors.Values)
        {
            int source=connector.GetUniqueID(),target=WirelessConnector.Link(connector);
            if(source<target && connectors.TryGetValue(target,out var peer) && WirelessConnector.Link(peer)==source)
                yield return new NetworkLink(id(connector),id(peer));
        }
    }
}
