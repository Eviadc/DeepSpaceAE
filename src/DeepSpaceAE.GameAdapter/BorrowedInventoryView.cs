using UnityEngine;
namespace DeepSpaceAE.GameAdapter;

/// <summary>Render-only native slot borrowing. The saved storage graph is never reparented.</summary>
internal sealed class BorrowedInventoryView : PixelElement, MachineryWidget
{
    private readonly PixelElement storage;
    private readonly StealPixelElement borrowed = new(false);
    private readonly Vector2 savedPosition;
    private bool returned;
    public bool fillWidth { get; set; }
    public bool canResize => false;
    public int widthPixels { get; private set; }
    public int heightPixels { get; private set; }
    public TreeNodeRender handler { get; }
    public RectTransform rectTransform => handler.rectTransform;
    public TreeNodeRender inner => handler;
    internal BorrowedInventoryView(PixelElement storage)
    {
        this.storage = storage; savedPosition = storage.rectTransform.anchoredPosition;
        handler = TreeNodeRender.Instantiate(this, "AE borrowed inventory");
        borrowed.handler.SetParent(handler);
        borrowed.Attach(storage);
        Validate();
    }
    public void Validate()
    {
        if (returned || storage.handler == null) return;
        storage.Validate();
        widthPixels = storage.widthPixels; heightPixels = storage.heightPixels;
        // StealPixelElement is neither MachineryContainer nor MachineryWidget: the native
        // layout does not call its Validate. It also retains the old window's slot offset.
        borrowed.ResizePixels(widthPixels, heightPixels);
        MachineryStyle.Place(storage.handler, 0, 0, widthPixels, heightPixels);
        MachineryStyle.Place(borrowed.inner, 0, 0, widthPixels, heightPixels);
        MachineryStyle.Place(borrowed.handler, 0, 0, widthPixels, heightPixels);
        rectTransform.sizeDelta = new Vector2(widthPixels, heightPixels);
    }
    internal void Return()
    {
        if (returned) return;
        returned = true;
        if (storage.handler == null) return;
        borrowed.UnStealItem();
        storage.rectTransform.anchoredPosition = savedPosition;
    }
    public void ResizePixels(int width, int height) => Validate();
    public int WidthPixelsClamp(int width) => widthPixels;
    public int HeightPixelsClamp(int height) => heightPixels;
    public int XClamp(int x) => 0;
    public int YClamp(int y) => 0;
    public bool Attach(PixelElement element) => false;
    public bool Detach(PixelElement element) => false;
}
