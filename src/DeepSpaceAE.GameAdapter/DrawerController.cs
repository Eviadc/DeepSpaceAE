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
    private View ReadDrawerController(GameItem controller,Board board)
    {
        if(!GeneralHelper.IsItemEffectivelyOwned(controller) || controller.parentInventory==null) throw new Rejected(UiText.Get("error_not_owned"));
        var view=new View { Board=board,Root=MainInventory()!,Core=controller };
        var policy=DrawerController.Policy(controller);
        view.Owners.Add(controller,controller); view.Policies.Add(controller,policy);
        view.Inventories.Add(controller,DrawerController.Members(controller,board.Layout).OrderByDescending(d=>DrawerStorage.Data(d).Used>0 || DrawerStorage.IsLocked(d) && DrawerStorage.LockedId(d).Length>0).Select(d=>(GameInventory)DrawerStorage.Data(d)).ToList());
        view.Attachments.Add(controller.parentInventory);
        return view;
    }
    private MachineryPanel? controllerStockPanel;
    private int controllerStockPage;
    private void ShowDrawerController(GameItem controller)
    {
        ClosePart(); displayedPart=controller;
        controllerStockPage=0;
        controllerStockPanel=new MachineryPanel(UiText.Get("ui_drawer_contents"),null,"drawer_stock");
        partWindow=MachineHelper.CreateMachineryWindow(true,MachineHelper.Panel(UiText.Get("item_drawer_controller_name"),PriorityControl(controller),"drawer_controller",false),controllerStockPanel);
        RefreshControllerStock(controller);
        partWindow.closeCallback=ClosePart; partWindow.Show().Center();
    }

    private void RefreshControllerStock(GameItem controller)
    {
        if(controllerStockPanel==null) return;
        var members=new InventoryLayout(controller.parentInventory).DrawerMembers(controller);
        var rows=new List<(GameItem Item,long Count)>();
        foreach(var entry in members.SelectMany(drawer=>DrawerStorage.Data(drawer).OverviewStock()))
        {
            int index=rows.FindIndex(row=>DrawerStorage.Same(row.Item,entry.Item));
            if(index<0) rows.Add(entry);
            else rows[index]=(rows[index].Item,rows[index].Count+entry.Count);
        }
        rows.Sort((a,b)=>string.Compare(a.Item.name,b.Item.name,StringComparison.CurrentCulture));
        const int columns=5, pageSize=20;
        var tabs=rows.Count>pageSize?new MachineryTabs():null;
        PixelElement? stock=null;
        for(int start=0;start<rows.Count;start+=pageSize)
        {
            int length=Math.Min(pageSize,rows.Count-start);
            var grid=new GridPixelElement(Math.Min(columns,length),(length+columns-1)/columns,true).SetSpacing(6,3);
            for(int i=0;i<length;i++)
            {
                var entry=rows[start+i];
                grid.AttachPos(MachineryStack.Vertical(new NativeWidgets.StockIcon(entry.Item),NativeWidgets.Text("× "+entry.Count)),i%columns,i/columns);
            }
            if(tabs==null) stock=grid;
            else { tabs.AddTab((1+start/pageSize).ToString(),grid); stock=tabs; }
        }
        if(tabs!=null)
        {
            tabs.Select(Math.Min(controllerStockPage,tabs.count-1));
            tabs.onChanged=page=>controllerStockPage=page;
        }
        var old=controllerStockPanel.content;
        controllerStockPanel.SetContent(MachineryStack.Vertical(NativeWidgets.Text(UiText.Get("ui_drawer_controller_count",members.Count)),
            stock??NativeWidgets.Text(UiText.Get("ui_drawer_empty"))));
        if(old!=null) UnityEngine.Object.Destroy(old.handler.gameObject);
        partWindow?.Validate();
    }
}
