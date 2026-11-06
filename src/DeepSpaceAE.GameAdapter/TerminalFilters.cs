using System;
using System.Collections.Generic;
using System.Linq;
using DeepSpaceAE.Core.Network;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

internal sealed partial class GameSession
{
    internal const string CustomerFilter = "deepspaceae.customer_demand";
    private string terminalFilterLocale="";
    private bool HasTagFilters => openTerminal?.identifier == "deepspaceae.terminal";
    private bool PowerView(View view) => DrawerStorage.IsDrawer(view.Core)
        || view.Board.Powered.Contains(Id(view.Core));
    internal static bool MatchesCustomer(GameItem item)
    {
        var instance = PlayerStore.HasInstance ? PlayerStore.Instance.currentClientInstance : null;
        var customer = instance?.GetClientBlueprint();
        return customer != null && customer.IsClientBuying() && !StoreClientInstance.IsClientBusinessDone(instance)
            && customer.IsClientBuyingThisItem(item);
    }
    private CatalogSearch ActiveCatalog => liquidMode ? displayedView!.LiquidSearch : HasTagFilters ? displayedView!.TagSearch : displayedView!.Search;
    private void UpdateTerminalFilterOptions()
    {
        // Raw state keys stay searchable with #tag, but do not populate the
        // ordinary category menu or get passed to the native type translator.
        var tags = ActiveCatalog.Tags.Where(NativeWidgets.HasTagLabel)
            .OrderBy(NativeWidgets.TagName,StringComparer.CurrentCulture).ToArray();
        if (HasTagFilters) tags = new[] { CustomerFilter }.Concat(tags).ToArray();
        string locale=LocHelper.GetCurrentLocaleCode();
        if (displayedTags.SequenceEqual(tags) && terminalFilterLocale==locale) return;
        terminalFilterLocale=locale;
        displayedTags = tags; if (!tags.Contains(tag)) tag = "";
        tagField!.Control.SetOptions(new[] { UiText.Get(HasTagFilters ? "ui_all_tags" : "ui_all_categories") }
            .Concat(tags.Select(NativeWidgets.TagName)).ToArray()).SetIndex(Array.IndexOf(tags, tag) + 1);
    }
    private IReadOnlyList<CatalogRecord> TerminalRows()
    {
        UpdateTerminalFilterOptions();
        if (HasTagFilters && tag == CustomerFilter)
        {
            // Apply the real demand predicate before quantity aggregation/sorting.
            var rows = ActiveCatalog.Find(search).Where(r => MatchesCustomer(displayedView!.Live[r.Instance]));
            return new CatalogSearch(rows, aggregateQuantity: !liquidMode).Find("", null, liquidMode?sort:CatalogSort.Native);
        }
        return ActiveCatalog.Find(search, string.IsNullOrEmpty(tag) ? null : tag, liquidMode?sort:CatalogSort.Native);
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
