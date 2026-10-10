using System.Drawing.Drawing2D;

namespace NPConfigurator;

public sealed class ProbeView : ScrollableControl
{
    public ProbeConfiguration Configuration { get; set; } = ProbeConfiguration.Create(ProbeProfile.Np1);
    public event Action<int, int>? SiteClicked;
    public event Action<int, int, int>? RangeSelected;
    public event Action<int, int, int>? RangePreviewChanged;
    private HashSet<int> highlightedChannels = [];
    public IReadOnlySet<int> HighlightedChannels => highlightedChannels;
    private HashSet<(int Shank, int Site)>? highlightedSites;
    public IReadOnlySet<(int Shank, int Site)>? HighlightedSites => highlightedSites;
    internal bool IsSelecting => selecting;
    internal bool AdditiveSelection { get; private set; }
    public int ConflictSite { get; set; } = -1;
    public int ConflictShank { get; set; } = -1;
    public int ViewedShank { get; private set; }
    private readonly ToolTip tip = new();
    private readonly System.Windows.Forms.Timer scrollTimer = new() { Interval = 45 };
    private (int, int) lastHover = (-1, -1);
    private (int shank, int site) anchor = (-1, -1), rangeStart = (-1, -1);
    private int rangeEnd = -1;
    private Point mouseDown, pointer;
    private bool selecting, rangeGesture;
    private const int RowHeight = 9, Dot = 7, MapTop = 45;

    public ProbeView()
    {
        DoubleBuffered = true; AutoScroll = true; BackColor = Color.FromArgb(246, 248, 251);
        SetStyle(ControlStyles.ResizeRedraw, true);
        AccessibleName = "Probe site map";
        AccessibleDescription = "Click, drag, or Shift-click to highlight electrodes without changing sampling. Control adds to the selection. E enables, D disables, Space toggles. Escape cancels a drag.";
        TabStop = true;
        scrollTimer.Tick += (_, _) => ScrollSelection();
    }
    public void ResetSelection()
    {
        CancelSelection(); anchor = (-1, -1); lastHover = (-1, -1);
    }
    public void RefreshMap()
    {
        AutoScrollMinSize = new Size(0, MapTop + (Configuration.SitesPerShank / 2) * RowHeight + 30);
        Invalidate();
    }
    public void GoToSite(int site)
    {
        AutoScrollPosition = new Point(0, Math.Max(0, MapTop + site / 2 * RowHeight - 60));
        Invalidate();
    }
    public void BrowseToSite(int shank, int site)
    {
        // Bank pages share one physical shank. Keep its endpoint so Shift-click can cross pages.
        CancelSelection();
        if (anchor.shank != shank) anchor = (-1, -1);
        lastHover = (-1, -1);
        FocusShank(shank); GoToSite(site);
    }
    public void FocusShank(int shank)
    {
        ViewedShank = Math.Clamp(shank, 0, Configuration.Shanks - 1);
        Invalidate();
    }
    public void SetHighlightedChannels(IEnumerable<int> channels)
    {
        var next = channels.ToHashSet();
        if (highlightedSites == null && highlightedChannels.SetEquals(next)) return;
        highlightedSites = null;
        highlightedChannels = next; Invalidate();
    }
    public void SetHighlightedSites(IEnumerable<(int Shank, int Site)> sites)
    {
        var next = sites.ToHashSet();
        if (highlightedSites != null && highlightedSites.SetEquals(next)) return;
        highlightedSites = next;
        highlightedChannels = next.Select(s => Catalog.Channel(Configuration.Profile, s.Shank, s.Site)).ToHashSet();
        Invalidate();
    }
    public void EnsureSiteVisible(int site)
    {
        int y = SiteCenter(0, site).Y;
        if (y < 36 || y > ClientSize.Height - 12) GoToSite(site);
    }
    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        // Native scrolling copies viewport pixels, including our fixed header. Repaint the entire map.
        Invalidate();
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        // ScrollableControl's wheel path changes the offset without raising OnScroll.
        Invalidate();
    }
    private int ShankX(int shank) => Configuration.Shanks == 1 ? ClientSize.Width / 2 - 12
        : 72 + shank * Math.Max(50, (ClientSize.Width - 90) / Configuration.Shanks);
    internal Point SiteCenter(int shank, int site) => new(ShankX(shank) + (site % 2) * 12 + Dot / 2,
        MapTop + site / 2 * RowHeight + AutoScrollPosition.Y + Dot / 2);
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.None;
        var selected = Configuration.Channels.Where(c => c.Site != null).GroupBy(c => (c.Shank, c.Site!.Value)).ToDictionary(g => g.Key, g => g.First());
        int yScroll = AutoScrollPosition.Y;
        using var inactive = new SolidBrush(Color.FromArgb(206, 213, 224));
        using var chosen = new SolidBrush(Color.FromArgb(15, 133, 126));
        using var disabled = new SolidBrush(Color.FromArgb(134, 145, 162));
        using var reserved = new SolidBrush(Color.FromArgb(195, 136, 43));
        using var conflict = new SolidBrush(Color.FromArgb(203, 60, 57));
        using var textBrush = new SolidBrush(Color.FromArgb(60, 73, 94));
        using var highlight = new Pen(Color.FromArgb(34, 92, 205), 2);
        using var selectedOutline = new Pen(Color.FromArgb(19, 87, 84));
        using var rangeFill = new SolidBrush(Color.FromArgb(95, 68, 131, 235));
        using var rangeOutline = new Pen(Color.FromArgb(52, 104, 205));
        using var shankFocus = new SolidBrush(Color.FromArgb(226, 236, 252));
        e.Graphics.FillRectangle(shankFocus, ShankX(ViewedShank) - 8, 31, 36, Math.Max(0, ClientSize.Height - 31));
        int firstRow = Math.Max(0, (-yScroll - MapTop) / RowHeight - 1);
        int lastRow = Math.Min(Configuration.SitesPerShank / 2, firstRow + ClientSize.Height / RowHeight + 5);
        for (int shank = 0; shank < Configuration.Shanks; shank++)
        {
            int x = ShankX(shank);
            for (int row = firstRow; row < lastRow; row++)
            {
                int y = MapTop + row * RowHeight + yScroll;
                if (row % 192 == 0)
                {
                    using var line = new Pen(Color.FromArgb(170, 183, 201));
                    e.Graphics.DrawLine(line, x - 6, y - 3, x + 23, y - 3);
                    if (shank == 0) e.Graphics.DrawString($"B{row / 192}\n{row * 2}", Font, textBrush, 6, y);
                }
                for (int side = 0; side < 2; side++)
                {
                    int site = row * 2 + side;
                    selected.TryGetValue((shank, site), out var channel);
                    bool isReserved = Configuration.Profile == ProbeProfile.Np1 && site is 191 or 575 or 959;
                    Brush brush = isReserved ? reserved : channel == null ? inactive : channel.Standby ? disabled : chosen;
                    if (ConflictSite == site && ConflictShank == shank) brush = conflict;
                    var rect = new Rectangle(x + side * 12, y, Dot, Dot);
                    e.Graphics.FillRectangle(brush, rect);
                    if (channel != null && !channel.Standby) e.Graphics.DrawRectangle(selectedOutline, rect);
                    if (highlightedSites != null ? highlightedSites.Contains((shank, site)) : channel != null && highlightedChannels.Contains(channel.Channel))
                        e.Graphics.DrawRectangle(highlight, Rectangle.Inflate(rect, 1, 1));
                    if (selecting && rangeGesture && shank == rangeStart.shank && site >= Math.Min(rangeStart.site, rangeEnd) && site <= Math.Max(rangeStart.site, rangeEnd))
                    {
                        var preview = Rectangle.Inflate(rect, 1, 1);
                        e.Graphics.FillRectangle(rangeFill, preview); e.Graphics.DrawRectangle(rangeOutline, preview);
                    }
                }
            }
        }
        using var header = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(header, 0, 0, ClientSize.Width, 31);
        e.Graphics.FillRectangle(shankFocus, ShankX(ViewedShank) - 8, 3, 36, 26);
        for (int shank = 0; shank < Configuration.Shanks; shank++)
            e.Graphics.DrawString($"S{shank}", Font, textBrush, ShankX(shank) - 4, 8);
    }
    private (int shank, int site) Hit(Point point)
    {
        if (point.Y < 31) return (-1, -1); // The sticky shank header is not a site.
        int y = point.Y - AutoScrollPosition.Y - MapTop;
        if (y < 0) return (-1, -1);
        int row = y / RowHeight;
        if (row >= Configuration.SitesPerShank / 2) return (-1, -1);
        for (int shank = 0; shank < Configuration.Shanks; shank++)
            for (int side = 0; side < 2; side++)
                if (point.X >= ShankX(shank) + side*12 - 2 && point.X <= ShankX(shank) + side*12 + Dot + 2)
                    return (shank, row*2+side);
        return (-1, -1);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) BeginSelection(e.Location, (ModifierKeys & Keys.Shift) != 0, (ModifierKeys & Keys.Control) != 0);
    }
    internal void BeginSelection(Point point, bool extend, bool additive = false)
    {
        var hit = Hit(point);
        if (hit.site < 0) return;
        Focus(); mouseDown = pointer = point; selecting = true; AdditiveSelection = additive;
        rangeGesture = extend && anchor.shank == hit.shank && anchor.site >= 0;
        rangeStart = rangeGesture ? anchor : hit; rangeEnd = hit.site;
        if (!rangeGesture) anchor = hit;
        Capture = true; tip.Active = false; scrollTimer.Start();
        PublishPreview();
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left) EndSelection(e.Location);
    }
    internal void EndSelection(Point point)
    {
        if (!selecting) return;
        MoveSelection(point);
        var start = rangeStart; int end = rangeEnd; bool isRange = rangeGesture;
        CancelSelection();
        if (isRange) RangeSelected?.Invoke(start.shank, Math.Min(start.site, end), Math.Max(start.site, end));
        else SiteClicked?.Invoke(start.shank, start.site);
    }
    internal void MoveSelection(Point point)
    {
        if (!selecting) return;
        pointer = point;
        if (!rangeGesture && (Math.Abs(point.X - mouseDown.X) >= SystemInformation.DragSize.Width / 2 ||
                              Math.Abs(point.Y - mouseDown.Y) >= SystemInformation.DragSize.Height / 2)) rangeGesture = true;
        if (!rangeGesture) return;
        int row = Math.Clamp((point.Y - AutoScrollPosition.Y - MapTop) / RowHeight, 0, Configuration.SitesPerShank / 2 - 1);
        int side = point.X < ShankX(rangeStart.shank) + 10 ? 0 : 1;
        rangeEnd = row * 2 + side;
        PublishPreview();
    }
    private void PublishPreview()
    {
        if (rangeGesture) RangePreviewChanged?.Invoke(rangeStart.shank, Math.Min(rangeStart.site, rangeEnd), Math.Max(rangeStart.site, rangeEnd));
        Invalidate();
    }
    internal void ScrollSelection()
    {
        if (!selecting || !rangeGesture) return;
        int step = pointer.Y < 52 ? -RowHeight * 4 : pointer.Y > ClientSize.Height - 26 ? RowHeight * 4 : 0;
        if (step == 0) return;
        int top = -AutoScrollPosition.Y;
        int next = Math.Clamp(top + step, 0, Math.Max(0, AutoScrollMinSize.Height - ClientSize.Height));
        if (next == top) return;
        AutoScrollPosition = new Point(0, next); MoveSelection(pointer);
    }
    private void CancelSelection()
    {
        selecting = false; rangeGesture = false; scrollTimer.Stop(); Capture = false; tip.Active = true;
        RangePreviewChanged?.Invoke(-1, -1, -1); Invalidate();
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (selecting && !Capture) CancelSelection();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && selecting) { CancelSelection(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (selecting) { MoveSelection(e.Location); return; }
        var hit = Hit(e.Location);
        if (hit == lastHover) return; lastHover = hit;
        tip.SetToolTip(this, hit.site < 0 ? "" : $"Shank {hit.shank} · Site {hit.site} · Bank {hit.site / 384}\nChannel {Catalog.Channel(Configuration.Profile, hit.shank, hit.site)}");
    }
    protected override void Dispose(bool disposing) { if (disposing) { scrollTimer.Dispose(); tip.Dispose(); } base.Dispose(disposing); }
}
