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
    public const int EngineerCost=200,FirstSteveDay=15,VisitChance=5;
    public static IReadOnlyList<ComponentOffer> Offers { get; }=Array.AsReadOnly(new[]{
        new ComponentOffer("core",600,20,1,1),new ComponentOffer("pipeline",20,100,4,8),
        new ComponentOffer("item_storage_bus",120,70,1,3),new ComponentOffer("terminal",250,55,1,1),
        new ComponentOffer("disk_drive",500,25,1,1),new ComponentOffer("disk_small",200,60,1,2),
        new ComponentOffer("disk_large",800,10,1,1),new ComponentOffer("capacity_upgrade",80,75,1,2),
        new ComponentOffer("tag_upgrade",100,35,1,1),new ComponentOffer("wrench",60,85,1,1),
        new ComponentOffer("import_bus",150,50,1,1),new ComponentOffer("export_bus",150,45,1,1),
        new ComponentOffer("energy_receiver",350,30,1,1)});
    public static IReadOnlyList<string> Starter { get; }=Array.AsReadOnly(new[]{"deepspaceae.core"}
        .Concat(Enumerable.Repeat("deepspaceae.pipeline",8)).Concat(Enumerable.Repeat("deepspaceae.item_storage_bus",2))
        .Concat(new[]{"deepspaceae.terminal","deepspaceae.disk_drive","deepspaceae.disk_small","deepspaceae.disk_small","deepspaceae.wrench"}).ToArray());
    public static int Price(string id)=>Offers.First(o=>o.Id==id).Price;
    public static bool CanRoll(int day,int lastRollDay,int servedDay)=>day>=FirstSteveDay && lastRollDay!=day && servedDay!=day;
    public static IReadOnlyList<string> RollStock(Func<int,bool> chance,Func<int,int,int> inclusiveRange)
    {
        var stock=new List<string>();
        foreach(var offer in Offers) if(chance(offer.Chance))
        {
            int count=inclusiveRange(offer.Minimum,offer.Maximum);
            if(count<offer.Minimum || count>offer.Maximum) throw new InvalidOperationException("Stock RNG outside requested range.");
            stock.AddRange(Enumerable.Repeat(offer.Id,count));
        }
        return stock.AsReadOnly();
    }
}
