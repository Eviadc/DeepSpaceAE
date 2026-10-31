using System;
namespace DeepSpaceAE.Core.Network;
public static class AutomationRules
{
    public static string MachineRole(bool importsToNetwork)=>importsToNetwork?"output":"input";
    public static bool PaidNightPower(int today,int budgetDay,bool settled,long reservedSource,long source,int budget,int cost)
        =>settled && today==budgetDay && source==reservedSource && cost>0 && cost<=budget;
    public static bool SourceRemainder(int before,int moved,int remaining,bool inSource,bool inDestination,bool destroyed)
        =>moved>0 && moved<=before && (moved<before?inSource && remaining==before-moved:
            !inSource && (inDestination || destroyed || remaining==0));
}
