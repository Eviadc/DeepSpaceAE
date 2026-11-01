using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal sealed partial class GameSession
{
    internal const string CustomerFilter = "deepspaceae.customer_demand";
    private BorrowedInventoryView? terminalUpgrades;
    private bool HasTagUpgrade => openTerminal?.identifier == "deepspaceae.terminal"
        && PartStorage.Inventory(openTerminal).childItems.Any(i => i.identifier == PartStorage.Tag);
    private bool PowerView(View view) => DrawerStorage.IsDrawer(view.Core)
        || PartStorage.Powered(view.Core, view.Board.Evaluation.NightCosts[Id(view.Core)], true);
    internal static bool MatchesCustomer(GameItem item)
    {
        var instance = PlayerStore.HasInstance ? PlayerStore.Instance.currentClientInstance : null;
        var customer = instance?.GetClientBlueprint();
        return customer != null && customer.IsClientBuying() && !StoreClientInstance.IsClientBusinessDone(instance)
            && customer.IsClientBuyingThisItem(item);
    }
    private CatalogSearch ActiveCatalog => liquidMode ? displayedView!.LiquidSearch : HasTagUpgrade ? displayedView!.TagSearch : displayedView!.Search;
    private void UpdateTerminalFilterOptions()
    {
        var tags = ActiveCatalog.Tags.ToArray();
        if (HasTagUpgrade) tags = new[] { CustomerFilter }.Concat(tags).ToArray();
        if (displayedTags.SequenceEqual(tags)) return;
        displayedTags = tags; if (!tags.Contains(tag)) tag = "";
        tagField!.Control.SetOptions(new[] { UiText.Get(HasTagUpgrade ? "ui_all_tags" : "ui_all_categories") }
            .Concat(tags.Select(NativeWidgets.TagName)).ToArray()).SetIndex(Array.IndexOf(tags, tag) + 1);
    }
    private IReadOnlyList<CatalogRecord> TerminalRows()
    {
        UpdateTerminalFilterOptions();
        if (HasTagUpgrade && tag == CustomerFilter)
        {
            // Apply the real demand predicate before quantity aggregation/sorting.
            var rows = ActiveCatalog.Find(search).Where(r => MatchesCustomer(displayedView!.Live[r.Instance]));
            return new CatalogSearch(rows, aggregateQuantity: !liquidMode).Find("", null, sort);
        }
        return ActiveCatalog.Find(search, string.IsNullOrEmpty(tag) ? null : tag, sort);
    }
    internal void CustomerContextChanged()
    {
        if (openTerminal == null || tag != CustomerFilter) return;
        if (writing || ItemMouseDragHandler.current?.IsDraggingItem == true || ItemMultiSelectHandler.current?.IsGroupDragging == true)
        { terminalDirty = true; ScheduleTerminalRefresh(); return; }
        RenderTerminal();
    }
}

// Only a current-customer reference check, four times a second while the window
// exists. No inventory traversal or repeated network scan in Update.
public sealed class TerminalCustomerWatch : MonoBehaviour
{
    internal GameSession Session = null!;
    private StoreClientInstance? prior;
    private float next;
    private void Update()
    {
        if (Time.unscaledTime < next) return; next = Time.unscaledTime + .25f;
        var current = PlayerStore.HasInstance ? PlayerStore.Instance.currentClientInstance : null;
        if (ReferenceEquals(current, prior)) return; prior = current;
        Session.CustomerContextChanged();
    }
}
