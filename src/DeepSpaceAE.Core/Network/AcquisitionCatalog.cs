using System;
using System.Collections.Generic;
using System.Linq;
namespace DeepSpaceAE.Core.Network;
public sealed class ComponentOffer
{
    public string Id { get; }
    public int Price { get; }
    public int Chance { get; }
    public int Minimum { get; }
    public int Maximum { get; }
    public ComponentOffer(string id,int price,int chance,int minimum,int maximum)
    { Id="deepspaceae."+id; Price=price; Chance=chance; Minimum=minimum; Maximum=maximum; }
}
public static class AcquisitionCatalog
{
    public const int EngineerCost=200;
    public const string ContactSlip="deepspaceae.steve_contact";
    public const string Wireless="deepspaceae.wireless_connector";
    public const long ContactNumber=6322; // Telephone keypad: ME + 2 + 2.
    // Keep the receiver's factory value for existing saves; it has no acquisition route.
    private static readonly ComponentOffer receiver = new("energy_receiver",350,30,1,1);
    public static IReadOnlyList<ComponentOffer> Offers { get; }=Array.AsReadOnly(new[]{
        // Guaranteed stock goes first, then cards; remaining stock is interleaved so
        // one large lot cannot crowd out all later types. Lots use native fit.
        new ComponentOffer("disk_large",800,15,1,1),
        new ComponentOffer("capacity_upgrade",80,100,3,5),new ComponentOffer("tag_upgrade",100,25,1,2),
        new ComponentOffer("core",600,20,1,1),new ComponentOffer("disk_drive",500,30,1,1),
        new ComponentOffer("terminal",250,55,1,1),new ComponentOffer("item_interface",300,45,1,1),
        new ComponentOffer("wireless_connector",450,100,2,4),new ComponentOffer("drawer",160,70,1,2),new ComponentOffer("fluid_drawer",160,70,1,2),
        new ComponentOffer("drawer_controller",350,70,1,1),
        new ComponentOffer("formation_bus",180,60,1,2),new ComponentOffer("annihilation_bus",180,60,1,2),
        new ComponentOffer("disk_small",200,75,1,3),
        new ComponentOffer("wrench",60,85,1,1),new ComponentOffer("item_storage_bus",120,80,2,4),
        new ComponentOffer("import_bus",150,65,1,3),new ComponentOffer("export_bus",150,60,1,3),
        new ComponentOffer("pipeline",20,100,3,6)});
    // Always place these before optional stock; unused counter space stays empty.
    public static IReadOnlyList<string> GuaranteedStock { get; }=Array.AsReadOnly(new[]{
        "deepspaceae.disk_large","deepspaceae.disk_small","deepspaceae.disk_small","deepspaceae.tag_upgrade"});
    public static IReadOnlyList<string> Starter { get; }=Array.AsReadOnly(new[]{"deepspaceae.core"}
        .Concat(Enumerable.Repeat("deepspaceae.pipeline",8)).Concat(Enumerable.Repeat("deepspaceae.item_storage_bus",2))
        .Concat(new[]{"deepspaceae.terminal","deepspaceae.disk_drive","deepspaceae.disk_small","deepspaceae.disk_small","deepspaceae.wrench",
            "deepspaceae.wireless_connector","deepspaceae.wireless_connector"}).ToArray());
    public static IReadOnlyList<string> UpgradeGifts { get; }=Array.AsReadOnly(Starter.Append(ContactSlip).ToArray());
    public static int Price(string id)=>id==receiver.Id?receiver.Price:id=="deepspaceae.fluid_interface"?300:Offers.First(o=>o.Id==id).Price;
    public static IReadOnlyList<string> RollStock(Func<int,bool> chance,Func<int,int,int> inclusiveRange)
    {
        var stock=new List<string>(GuaranteedStock);
        var lots=new List<(string Id,int Count)>();
        foreach(var offer in Offers) if(chance(offer.Chance))
        {
            int count=offer.Id==Wireless?checked(inclusiveRange(offer.Minimum/2,offer.Maximum/2)*2)
                :inclusiveRange(offer.Minimum,offer.Maximum);
            if(count<offer.Minimum || count>offer.Maximum) throw new InvalidOperationException("Stock RNG outside requested range.");
            // A pair is offered together before optional stock can fill the counter.
            if(offer.Id==Wireless) stock.InsertRange(0,Enumerable.Repeat(offer.Id,count));
            else if(offer.Id=="deepspaceae.capacity_upgrade" || offer.Id=="deepspaceae.tag_upgrade")
                stock.AddRange(Enumerable.Repeat(offer.Id,count));
            else lots.Add((offer.Id,count));
        }
        int rounds=lots.Count==0?0:lots.Max(lot=>lot.Count);
        for(int round=0;round<rounds;round++)
            foreach(var lot in lots) if(round<lot.Count) stock.Add(lot.Id);
        return stock.AsReadOnly();
    }
    public static IReadOnlyList<string> PairWirelessStock(IEnumerable<string> stock)
    {
        var items=stock.ToArray();
        int count=items.Count(id=>id==Wireless)/2*2;
        return Array.AsReadOnly(Enumerable.Repeat(Wireless,count).Concat(items.Where(id=>id!=Wireless)).ToArray());
    }
}
