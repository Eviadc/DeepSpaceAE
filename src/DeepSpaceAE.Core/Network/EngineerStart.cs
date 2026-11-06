using System;
using System.Collections.Generic;
using System.Linq;
namespace DeepSpaceAE.Core.Network;

public static class EngineerStart
{
    public const string Scenario = "deepspaceae.network_engineer", Battery = "energy_credit";
    public const int GeneralistCost = 50;
    // Current unreleased loadout; development targets fresh saves.
    private static readonly string[] ordinary = { "storage_bay", "processed_meat", "processed_meat", "processed_juice",
        "toothpaste", "toothpaste", "toilet_paper", "toilet_paper" };
    private static readonly IReadOnlyList<string> withIntro = Array.AsReadOnly(ordinary.Concat(AcquisitionCatalog.Starter).Append(Battery).ToArray());
    private static readonly IReadOnlyList<string> withoutIntro = Array.AsReadOnly(ordinary.Concat(new[] { "sign_food", "sign_household" })
        .Concat(AcquisitionCatalog.Starter).Append(Battery).ToArray());
    private static readonly string[] routing = { "deepspaceae.tag_upgrade", "deepspaceae.import_bus", "deepspaceae.export_bus",
        "deepspaceae.import_bus", "deepspaceae.export_bus" };
    private static readonly IReadOnlyList<string> withContact = Array.AsReadOnly(withIntro.Concat(routing).Append(AcquisitionCatalog.ContactSlip).ToArray());
    private static readonly IReadOnlyList<string> withoutIntroWithContact = Array.AsReadOnly(withoutIntro.Concat(routing).Append(AcquisitionCatalog.ContactSlip).ToArray());
    public static IReadOnlyList<string> Items(bool skipIntro) => skipIntro?withoutIntroWithContact:withContact;
}
