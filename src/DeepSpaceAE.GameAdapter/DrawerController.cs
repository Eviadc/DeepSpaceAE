using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;

namespace DeepSpaceAE.GameAdapter;

internal static class DrawerController
{
    internal const string Id="deepspaceae.drawer_controller";
    internal static bool IsController(GameItem item)=>item.identifier==Id;
    internal static StoragePolicy Policy(GameItem item)=>new(PartStorage.Int(item,"priority"),StorageAccess.ReadWrite,false,true,false,0,Array.Empty<string>());
    internal static IEnumerable<GameItem> Members(GameItem item,NetworkLayout network)
        =>network.InventoryOf(item)!=null?network.DrawerMembers(item)
            :item.parentInventory==null?Enumerable.Empty<GameItem>():new InventoryLayout(item.parentInventory).DrawerMembers(item).OrderBy(i=>i.GetUniqueID());
    internal static void Create(GameItem item)
    {
        var state=InterfaceStorage.Get(item);
        state.Inventory=new InterfaceDepositInventory(item); state.Ready=true;
        var graph=new PixelWindow(true,UiText.Get("item_drawer_controller_name"));
        graph.Attach(state.Inventory); item.SetContentWindow(graph);
    }
}

internal sealed partial class GameSession
{
    private View ReadDrawerController(GameItem controller,Board board,bool catalog=true)
    {
        if(!GeneralHelper.IsItemEffectivelyOwned(controller) || controller.parentInventory==null) throw new Rejected(UiText.Get("error_not_owned"));
        var view=new View { Board=board,Root=MainInventory()!,Core=controller };
        var policy=DrawerController.Policy(controller);
        view.Owners.Add(controller,controller); view.Policies.Add(controller,policy);
        view.Inventories.Add(controller,DrawerController.Members(controller,board.Layout).OrderByDescending(d=>DrawerStorage.Data(d).Used>0 || DrawerStorage.IsLocked(d) && DrawerStorage.LockedId(d).Length>0).Select(d=>(GameInventory)DrawerStorage.Data(d)).ToList());
        view.Attachments.Add(controller.parentInventory);
        return catalog?ReadCatalog(view,new Dictionary<GameItem,List<StoragePolicy>>{[controller]=new(){policy}}):view;
    }
    private void ShowDrawerController(GameItem controller)
    {
        ClosePart(); displayedPart=controller;
        var board=ReadBoard(); int count=DrawerController.Members(controller,board.Layout).Count();
        var body=MachineryStack.Vertical(PriorityControl(controller),NativeWidgets.Text(UiText.Get("ui_drawer_controller_count",count)),
            NativeWidgets.Text(UiText.Get("item_drawer_controller_desc")),
            new MachineryButton(UiText.Get("ui_drawer_controller_open"),()=>Open(controller)));
        partWindow=MachineHelper.CreateMachineryWindow(true,MachineHelper.Panel(UiText.Get("item_drawer_controller_name"),body,"drawer_controller",false));
        partWindow.closeCallback=ClosePart; partWindow.Show().Center();
    }
}
