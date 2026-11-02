using System;
using System.Collections.Generic;
using System.Linq;
namespace DeepSpaceAE.Core.Network;

public static class EngineerStart
{
    public const string Scenario = "deepspaceae.network_engineer", Battery = "energy_credit";
    public const int GeneralistCost = 50;
    // Rev6 generalist supplies. Keep this v1 journal order stable for pending saves.
    private static readonly string[] ordinary = { "storage_bay", "processed_meat", "processed_meat", "processed_juice",
        "toothpaste", "toothpaste", "toilet_paper", "toilet_paper" };
    private static readonly IReadOnlyList<string> withIntro = Array.AsReadOnly(ordinary.Concat(AcquisitionCatalog.Starter).Append(Battery).ToArray());
    private static readonly IReadOnlyList<string> withoutIntro = Array.AsReadOnly(ordinary.Concat(new[] { "sign_food", "sign_household" })
        .Concat(AcquisitionCatalog.Starter).Append(Battery).ToArray());
    public static IReadOnlyList<string> Items(bool skipIntro) => skipIntro ? withoutIntro : withIntro;
}
