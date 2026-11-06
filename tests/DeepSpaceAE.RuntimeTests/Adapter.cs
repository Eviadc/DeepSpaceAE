using System;
using System.Linq;
using System.Reflection;

namespace DeepSpaceAE.RuntimeTests;

// Narrow test-only reflection into the exact installed product, not copied logic.
internal static class Adapter
{
    private const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private static Assembly Product=>AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="DeepSpaceAE.GameAdapter");
    private static Type Type(string name)=>Product.GetType("DeepSpaceAE.GameAdapter."+name,true)!;
    internal static object Session=>Type("ModEntry").GetProperty("ActiveSession",Flags)!.GetValue(null)!;
    internal static object? Field(object owner,string name)=>owner.GetType().GetField(name,Flags)!.GetValue(owner);
    internal static void SetField(object owner,string name,object value)=>owner.GetType().GetField(name,Flags)!.SetValue(owner,value);
    internal static object? Property(object owner,string name)=>owner.GetType().GetProperty(name,Flags)!.GetValue(owner);
    internal static object? Call(object owner,string name,params object?[] args)=>Invoke(owner.GetType(),owner,name,args);
    internal static object? Static(string type,string name,params object?[] args)=>Invoke(Type(type),null,name,args);
    private static object? Invoke(Type type,object? target,string name,object?[] args)
    {
        var method=type.GetMethods(Flags).Single(m=>m.Name==name && m.GetParameters().Length==args.Length);
        try { return method.Invoke(target,args); }
        catch(TargetInvocationException e) { throw e.InnerException ?? e; }
    }
    internal static GameSlotInventory[] Slots(string type,string name,GameItem item)=>(GameSlotInventory[])Static(type,name,item)!;
    internal static GameInventory Data(GameItem disk)=>(GameInventory)Static("DiskStorage","Data",disk)!;
    internal static object Board()=>Call(Session,"ReadBoard",false)!;
    internal static object Evaluation(object board)=>Field(board,"Evaluation")!;
    internal static object Indexed(object owner,string property,string key)=>((System.Collections.IDictionary)Property(owner,property)!)[key]!;
    internal static string DeviceStatus(object evaluation,string id)=>Property(Indexed(evaluation,"Devices",id),"Status")!.ToString()!;
    internal static int SearchCount(object search,string query)
    {
        var method=search.GetType().GetMethods(Flags).Single(m=>m.Name=="Find" && m.GetParameters().Length==3);
        var result=(System.Collections.ICollection)method.Invoke(search,new[]{(object)query,null,Enum.ToObject(method.GetParameters()[2].ParameterType,0)})!;
        return result.Count;
    }
    internal static string Id(GameItem item)=>(string)Call(Session,"Id",item)!;
    internal static void Move(GameItem item,GameInventory target)
    {
        var source=item.parentInventory ?? throw new InvalidOperationException("No real source.");
        var slot=target.TryFindOneValidInventorySlot(item);
        if(slot==null || slot.targetItem!=null || slot.numTransfer<item.unitCount) throw new InvalidOperationException("No whole native slot.");
        Call(Session,"NativeMove",item,source,slot);
    }
}
