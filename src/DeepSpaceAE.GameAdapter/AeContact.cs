using System;
using System.Collections.Generic;
using System.Globalization;
using DeepSpaceAE.Core.Network;
using HarmonyLib;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal sealed partial class Acquisition
{
    private const string ContactBook="ae-contact";
    private void EnableContact()
    {
        PhoneClientList.DialogFuncs[Steve]=PhoneDialog;
        // Native paper background and widgets are resolved from the installed game.
        // Only the authored 32x16 inventory icon is distributed with this mod.
        var page=new PaperPageData { name="ME" };
        page.elements.AddRange(new[] {
            new PaperElementData { type="image",x=18,y=18,w=38,h=38,sprite=ModEntry.ModId+":engineer-start",fit="contain" },
            new PaperElementData { x=64,y=18,w=178,h=24,loc="contact_title",fontSize=17,wrap=false },
            new PaperElementData { x=64,y=44,w=178,h=14,text="APPLIED ENERGISTICS",fontSize=9,wrap=false },
            new PaperElementData { x=20,y=67,w=228,h=33,text=AcquisitionCatalog.ContactNumber.ToString(CultureInfo.InvariantCulture),fontSize=30,align="center",wrap=false },
            new PaperElementData { x=20,y=105,w=228,h=14,text="ME + 2 + 2",fontSize=10,align="center",wrap=false },
            new PaperElementData { x=18,y=122,w=232,h=16,loc="contact_subtitle",fontSize=11,align="center",wrap=false },
            new PaperElementData { type="button",id="dial",x=50,y=143,w=168,h=22,loc="contact_dial",action="dial",fontSize=12 },
        });
        ModHelper.RegisterPaperBook(ModEntry.ModId,new PaperBookData {
            id=ContactBook,title="ME / 6322",background="card_ripped_2x",width=536,height=352,designWidth=268,designHeight=176,
            zoomable=false,chrome=PaperStarters.DefaultChrome(new Vector2(268,176),2,true,false,false,false),
            pages=new List<PaperPageData> { page },
            item=new PaperItemData { id=AcquisitionCatalog.ContactSlip,nameLoc="item_contact_name",descriptionLoc="item_contact_desc",sprite="steve-contact",value=0,important=true }
        });
        ModHelper.OnPaperOpened(ModEntry.ModId,ContactBook,ui=>ui.OnAction("dial",()=>{
            if(!EnsureContact()) return;
            ui.Close(); PhoneUIManager.Instance.OpenUI(); PhoneUIManager.Instance.AutoCall(AcquisitionCatalog.ContactNumber);
        }));
    }
    private bool EnsureContact()
    {
        var contacts=PlayerStore.Instance.PhoneClientDict;
        if(contacts.TryGetValue(AcquisitionCatalog.ContactNumber,out var existing))
        {
            if(existing.dialogFuncId==Steve) return true;
            log.Error("AE phone number 6322 is already used by another contact."); return false;
        }
        contacts.Add(AcquisitionCatalog.ContactNumber,new StorePhoneClient {
            displayName=UiText.Get("npc_steve_name"),locID=Steve,dialogFuncId=Steve,
            phoneState=StorePhoneClient.PhoneState.Regular,phoneClientType=StorePhoneClient.PhoneClientType.Supplier,
            alias=new List<string> { Steve }
        });
        return true;
    }
    private Dialogue PhoneDialog(StorePhoneClient _)
    {
        int day=StoreStation.GetDayCounter(),due=Get("steve_appointment_day");
        string message=due>day?"phone_steve_booked":due>0?"phone_steve_today":"phone_steve_confirmed";
        if(due<=0)
        {
            var stock=AcquisitionCatalog.RollStock(RNG.Roll,(min,max)=>RNG.GetRandomInt(min,max));
            ModHelper.SetModData(ModEntry.ModId,"steve_stock",string.Join("\n",stock));
            Put("steve_appointment_day",day+1);
        }
        return new Dialogue().SetText(UiText.Get("npc_steve_name"),UiText.Get(message))
            .SetEndAction(()=>PhoneUIManager.Instance.OnPhoneDialogEnd());
    }
    private void DisableContact()
    {
        if(PhoneClientList.DialogFuncs.TryGetValue(Steve,out var dialog) && dialog==PhoneDialog) PhoneClientList.DialogFuncs.Remove(Steve);
        ModHelper.RemovePaperBooks(ModEntry.ModId);
        // Keep the serialized contact (including dialedBefore) with its world.
        // Without the mod's handler it cannot book another appointment.
    }
}

[HarmonyPatch(typeof(StorePhoneClient),nameof(StorePhoneClient.GetLocalizedDisplayName))]
internal static class AeContactNamePatch
{
    [HarmonyPrefix]
    private static bool Prefix(StorePhoneClient __instance,ref string __result)
    {
        if(ModEntry.ActiveSession==null || __instance.dialogFuncId!=Acquisition.Steve) return true;
        __result=UiText.Get("npc_steve_name"); return false;
    }
}
