using System;
using System.Collections.Generic;
using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

// Fixed native grid: every configured ghost slot is visible; this never owns items.
internal sealed class GhostFilterInventory : GameGridInventory
{
    internal Action<int, GameItem>? ConfigureFilter;
    private readonly List<TreeNodeRender> tiles = new();
    internal GhostFilterInventory(int width, int height) : base(width, height) { SetMachineryStyle(); }
    internal void ClearTiles()
    {
        foreach (var tile in tiles) { tile.gameObject.SetActive(false); UnityEngine.Object.Destroy(tile.gameObject); }
        tiles.Clear();
    }
    internal void GhostTile(int index, Sprite? sprite, string description, Action clear)
    {
        int cell = UISettings.current.gridSize;
        var button = new MachineryButton("", clear, cell, cell);
        if (sprite != null) { var image = TreeNodeRender.Instantiate().SetParent(button.handler); MachineryStyle.SetSimple(image, sprite, Color.white); MachineryStyle.Place(image, 1, 1, cell - 2, cell - 2); }
        button.SetDescription(description); button.handler.SetParent(inner);
        MachineryStyle.Place(button.handler, index % 9 * cell, index / 9 * cell, cell, cell); tiles.Add(button.handler);
    }
    public override bool SupportsGroupPlacement => false;
    public override bool UncheckedAccept(GameItem item) => false;
    public override bool Expel(GameItem item) => false;
    public override bool MayHaveValidInventorySlot(GameItem item) => false;
    public override SlotMarker TryFindOneValidInventorySlot(GameItem item) => null!;
    public override SlotMarker TryInventorySlot(GameItem item, int count, Vector2 position, GridShape shape, TagSystem state)
    {
        var point = inner.rectTransform.InverseTransformPoint(position); int cell = UISettings.current.gridSize;
        int x = (int)Math.Floor(point.x / cell), y = (int)Math.Floor(-point.y / cell);
        return new SlotMarker { item = item, inventory = this, itemGridShape = shape, index = y * 9 + x,
            numTransfer = x >= 0 && y >= 0 && x < 9 && y < inventoryShape.height ? item.unitCount : 0 };
    }
}
