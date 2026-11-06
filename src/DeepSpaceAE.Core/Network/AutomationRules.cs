namespace DeepSpaceAE.Core.Network;
public static class AutomationRules
{
    public static string MachineRole(bool importsToNetwork)=>importsToNetwork?"output":"input";
    public static bool PaidNightPower(int today,int budgetDay,bool settled,long reservedSource,long source,int budget,int cost)
        =>settled && today==budgetDay && source==reservedSource && cost>0 && cost<=budget;
}
