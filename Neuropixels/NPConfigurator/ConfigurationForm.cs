using System.ComponentModel;

namespace NPConfigurator;

public sealed class ConfigurationForm : Form
{
    public ProbeConfiguration Configuration { get; private set; }
    public bool UseConfiguration { get; private set; }
    private readonly bool embeddedMode;
    private readonly ProbeView map = new() { Dock = DockStyle.Fill };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true, BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
    private readonly ComboBox profile = Choice(["NRIC1384", "NP1 standard", "NP2 single shank", "NP2 four shanks"]);
    private readonly NumericUpDown connector = Number(1, 3, 1);
    private readonly TextBox model = new() { Width = 150 }, serial = new() { Width = 150 };
    private readonly NumericUpDown refBank = Number(0, 2, 0), shank = Number(0, 3, 0), bank = Number(0, 3, 0);
    private readonly NumericUpDown firstSite = Number(0, 1279, 0), lastSite = Number(0, 1279, 383);
    private readonly ComboBox rangeMode = Choice(["Add", "Remove", "Replace all"]);
    private const string RangeHelp = "Mouse highlights only · Ctrl adds sites · E enables · D disables · Space toggles";
    private readonly Label rangeHint = new() { Text = RangeHelp, AutoSize = true, Margin = new Padding(12, 7, 0, 0) };
    private readonly ComboBox reference = Choice(["All", "Parity"]), apGain = Choice(ProbeConfiguration.Gains.Select(x => x.ToString()).ToArray()),
        lfpGain = Choice(ProbeConfiguration.Gains.Select(x => x.ToString()).ToArray());
    private readonly Label enabledCount = new() { AutoSize = true, Padding = new Padding(8, 3, 8, 3),
        Margin = new Padding(3, 0, 12, 0), BackColor = Color.FromArgb(226, 244, 240),
        ForeColor = Color.FromArgb(12, 101, 94), AccessibleName = "Enabled electrodes" };
    private readonly CheckBox highPass = new() { Text = "AP high-pass", AutoSize = true, Checked = true };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Padding = new Padding(8, 2, 8, 2), ForeColor = Color.FromArgb(47, 64, 88) };
    private readonly Label notice = new() { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 4), AutoEllipsis = true };
    private readonly Button save = Button("Save configuration"), use = Button("Use configuration"), export = Button("Export…");
    private readonly Button balanced = Button("96 sites / shank");
    private BindingList<ChannelSetting> rows = new();
    private bool refreshing, dirty;
    private bool syncingSelection;
    private HashSet<(int Shank, int Site)>? mapSelection;
    private string? currentPath;

    private readonly int? targetConnector;
    public ConfigurationForm(ProbeConfiguration? initial = null, bool embeddedMode = false, int? targetConnector = null)
    {
        this.embeddedMode = embeddedMode;
        this.targetConnector = targetConnector;
        Configuration = initial?.Clone() ?? ProbeConfiguration.Create(ProbeProfile.Np1);
        profile.Width = 172; profile.DropDownWidth = 190;
        Text = "NP Channel Selector"; Width = 1450; Height = 920;
        MinimumSize = new Size(1280, 780); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F); AutoScaleMode = AutoScaleMode.Dpi; BackColor = Color.White;
        Build(); Bind();
    }
    private static ComboBox Choice(string[] values)
    { var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 112 }; c.Items.AddRange(values); if(values.Length > 0) c.SelectedIndex = 0; return c; }
    private static NumericUpDown Number(int min, int max, int value) => new() { Minimum = min, Maximum = max, Value = value, Width = 64 };
    private static Button Button(string label) => new() { Text = label, AutoSize = true, Height = 30, Padding = new Padding(6, 2, 6, 2), BackColor = Color.White, ForeColor = Color.FromArgb(26,42,66), UseVisualStyleBackColor = false };
    private static void Labelled(FlowLayoutPanel row, string label, Control input)
    { row.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(8, 7, 3, 0) }); row.Controls.Add(input); }
    private static FlowLayoutPanel Flow() => new() { Dock = DockStyle.Fill, Padding = new Padding(5), WrapContents = true };
    private void Build()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 7, ColumnCount = 1 };
        foreach (float height in new[] { 58F, 46F, 46F, 46F }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var header = Flow(); header.BackColor = Color.FromArgb(26, 42, 66);
        header.Controls.Add(new Label { Text = "NP  /  CHANNEL SELECTION", ForeColor = Color.White, Font = new Font(Font.FontFamily, 14, FontStyle.Bold), AutoSize = true, Margin = new Padding(12, 11, 22, 0) });
        var load = Button("Open…"); var fresh = Button("New");
        header.Controls.AddRange([fresh, load, save, export]);
        header.Controls.Add(balanced);
        if (embeddedMode) header.Controls.Add(use);
        var identity = Flow(); Labelled(identity, "Probe", profile); Labelled(identity, "Connector", connector);
        Labelled(identity, "Model / revision", model); Labelled(identity, "Serial", serial); Labelled(identity, "Internal ref. bank", refBank);
        var presets = Flow(); Labelled(presets, "Shank", shank); Labelled(presets, "Bank", bank);
        var selectBank = Button("Select bank"); presets.Controls.Add(selectBank);
        Labelled(presets, "From site", firstSite); Labelled(presets, "To site", lastSite);
        Button range = Button("Apply range"), clear = Button("Disable all"); presets.Controls.AddRange([range, clear]);
        var rangeOptions = Flow();
        enabledCount.Font = new Font(Font.FontFamily, 10, FontStyle.Bold);
        rangeOptions.Controls.Add(enabledCount); Labelled(rangeOptions, "Apply action", rangeMode);
        rangeOptions.Controls.Add(rangeHint);
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 330, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 270 };
        Shown += (_, _) => split.SplitterDistance = 340;
        split.Panel1.Controls.Add(map); split.Panel2.Controls.Add(grid);
        var edit = Flow(); Labelled(edit, "Selected rows:", reference); Labelled(edit, "AP gain", apGain); Labelled(edit, "LFP gain", lfpGain);
        edit.Controls.Add(highPass); Button set = Button("Set settings"), active = Button("Sample on (E)"), standby = Button("Sample off (D)");
        edit.Controls.AddRange([set, active, standby]);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        footer.Controls.Add(status, 0, 0); footer.Controls.Add(notice, 0, 1);
        layout.Controls.Add(header, 0, 0); layout.Controls.Add(identity, 0, 1); layout.Controls.Add(presets, 0, 2);
        layout.Controls.Add(rangeOptions, 0, 3); layout.Controls.Add(split, 0, 4); layout.Controls.Add(edit, 0, 5); layout.Controls.Add(footer, 0, 6); Controls.Add(layout);
        load.Click += (_, _) => Guard(Open); save.Click += (_, _) => Guard(Save);
        var exportMenu = new ContextMenuStrip();
        exportMenu.Items.Add("SpikeGLX / Open Ephys IMRO (.imro)", null, (_, _) => Guard(() => ExportFile("imro", () => StandardFormats.ToImro(Configuration))));
        exportMenu.Items.Add("Legacy NP2 IMRO (type 21 / 24)", null, (_, _) => Guard(() => ExportFile("imro", () => StandardFormats.ToImro(Configuration, Configuration.Profile == ProbeProfile.Np2Multi ? "24" : "21"))));
        exportMenu.Items.Add(new ToolStripSeparator());
        exportMenu.Items.Add("ProbeInterface — 384-channel stream (.json)", null, (_, _) => Guard(() => ExportFile("probeinterface.json", () => StandardFormats.ToProbeInterface(Configuration))));
        exportMenu.Items.Add("ProbeInterface — selected-channel stream (.json)", null, (_, _) => Guard(() => ExportFile("probeinterface.json", () => StandardFormats.ToProbeInterface(Configuration, true))));
        exportMenu.Items.Add("Channel map (.csv)", null, (_, _) => Guard(() => ExportFile("csv", () => Configuration.ChannelCsv())));
        export.ContextMenuStrip = exportMenu;
        export.Click += (_, _) => { exportMenu.Items[1].Enabled = ProbeConfiguration.IsNp2(Configuration.Profile); exportMenu.Show(export, new Point(0, export.Height)); };
        balanced.Click += (_, _) => Guard(() => { FinishEdit(); map.ResetSelection(); Configuration.SelectMultiShankTip(); BindRows(); SyncNavigation(0, 0); map.GoToSite(0); MarkDirty(); });
        fresh.Click += (_, _) => { if (CanDiscard()) { Configuration = ProbeConfiguration.Create(Configuration.Profile); currentPath = null; Bind(); MarkDirty(); } };
        profile.SelectedIndexChanged += (_, _) => {
            if (refreshing || profile.SelectedIndex < 0) return;
            if (!CanDiscard()) { refreshing = true; profile.SelectedIndex = (int)Configuration.Profile; refreshing = false; return; }
            Configuration = ProbeConfiguration.Create((ProbeProfile)profile.SelectedIndex); currentPath = null; Bind(); MarkDirty();
        };
        foreach (var input in new Control[] { connector, model, serial, refBank })
            input.TextChanged += (_, _) => { if (!refreshing) { SyncIdentity(); MarkDirty(); } };
        foreach (var input in new[] { bank, shank })
        {
            input.ValueChanged += (_, _) => { if (!refreshing) BrowsePage(); };
            input.TextChanged += (_, _) => {
                if (!refreshing && int.TryParse(input.Text, out int value) && value >= input.Minimum && value <= input.Maximum && input.Value != value)
                    input.Value = value;
            };
        }
        selectBank.Click += (_, _) => Guard(() => {
            FinishEdit(); map.ResetSelection(); BrowsePage(); int first = (int)bank.Value * 384;
            Configuration.SelectRange((int)shank.Value, first, Math.Min(384, Configuration.SitesPerShank - first)); BindRows(); map.GoToSite(first); MarkDirty();
        });
        range.Click += (_, _) => ApplyRange((int)shank.Value, (int)firstSite.Value, (int)lastSite.Value, true);
        clear.Click += (_, _) => { map.ResetSelection(); Configuration.ClearSites(); BindRows(); MarkDirty(); };
        set.Click += (_, _) => Guard(() => {
            FinishEdit();
            foreach (var c in Selected()) { c.Reference = (string)reference.SelectedItem!; c.ApGain = int.Parse(apGain.Text); c.LfpGain = int.Parse(lfpGain.Text); c.HighPass = highPass.Checked; }
            grid.Refresh(); MarkDirty();
        });
        standby.Click += (_, _) => SetSampling(false);
        active.Click += (_, _) => SetSampling(true);
        use.Click += (_, _) => Guard(() => { FinishEdit(); Configuration.RequireValid(); UseConfiguration = true; DialogResult = DialogResult.OK; dirty = false; Close(); });
        map.SiteClicked += (s, site) => SelectMapRange(s, site, site, map.AdditiveSelection);
        map.RangeSelected += (s, first, last) => SelectMapRange(s, first, last, map.AdditiveSelection);
        map.RangePreviewChanged += (s, first, last) => rangeHint.Text = s < 0 ? RangeHelp
            : $"Highlight S{s}, sites {first}–{last} ({last - first + 1} sites). Release to select; Esc cancels.";
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += (_, _) => { if (!refreshing) MarkDirty(); };
        grid.DataError += (_, e) => { status.Text = "Invalid value: choose an item from the list or enter a whole-number site/shank."; e.ThrowException = false; };
        grid.SelectionChanged += (_, _) => UpdateMapSelection();
        grid.CurrentCellChanged += (_, _) => UpdateMapSelection();
        grid.Enter += (_, _) => UpdateMapSelection();
    }
    private void SelectMapRange(int selectedShank, int first, int last, bool additive)
    {
        int start = Math.Min(first, last), end = Math.Max(first, last);
        var next = additive ? mapSelection?.ToHashSet() ?? Selected().Where(c => c.Site != null).Select(c => (c.Shank, c.Site!.Value)).ToHashSet() : [];
        if (additive && start == end && next.Contains((selectedShank, start))) next.Remove((selectedShank, start));
        else for (int site = start; site <= end; site++) next.Add((selectedShank, site));
        mapSelection = next;
        SyncNavigation(selectedShank, start); firstSite.Value = start; lastSite.Value = end;
        SyncMapSelectionToGrid();
        status.ForeColor = Color.FromArgb(47, 64, 88);
        status.Text = $"{next.Count} electrodes highlighted · E / Sample on to enable · D / Sample off to disable · Space to toggle";
    }
    private void SyncMapSelectionToGrid()
    {
        if (mapSelection == null) return;
        var channels = mapSelection.Select(s => Catalog.Channel(Configuration.Profile, s.Shank, s.Site)).ToHashSet();
        syncingSelection = true;
        try
        {
            if (channels.Count > 0) SelectChannel(channels.Min());
            grid.ClearSelection();
            foreach (DataGridViewRow row in grid.Rows) row.Selected = channels.Contains(((ChannelSetting)row.DataBoundItem!).Channel);
            map.SetHighlightedSites(mapSelection);
        }
        finally { syncingSelection = false; }
    }
    private void UpdateMapSelection(bool reveal = true)
    {
        if (refreshing || syncingSelection) return;
        if (reveal) mapSelection = null;
        if (mapSelection != null) map.SetHighlightedSites(mapSelection);
        else map.SetHighlightedChannels(Selected().Select(c => c.Channel));
        if (reveal && grid.ContainsFocus && grid.CurrentRow is { Selected: true, DataBoundItem: ChannelSetting current } &&
            current.Site is int site && site >= 0 && site < Configuration.SitesPerShank && current.Shank >= 0 && current.Shank < Configuration.Shanks)
        {
            SyncNavigation(current.Shank, site); map.EnsureSiteVisible(site);
        }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Keep typing and native control shortcuts intact in metadata, numeric inputs, and cell editors.
        if ((map.ContainsFocus || grid.ContainsFocus) && !grid.IsCurrentCellInEditMode)
        {
            if (keyData is Keys.E or Keys.D or Keys.Space || keyData == (Keys.Control | Keys.A))
            {
                if (map.IsSelecting) return true;
                if (keyData == Keys.Space && (msg.LParam.ToInt64() & (1L << 30)) != 0) return true;
                if (keyData == (Keys.Control | Keys.A)) { mapSelection = null; grid.SelectAll(); UpdateMapSelection(false); }
                else SetSampling(keyData == Keys.Space ? null : keyData == Keys.E);
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private void SetSampling(bool? enabled)
    {
        try
        {
            FinishEdit();
            if (mapSelection != null)
            {
                if (mapSelection.Count == 0) { status.Text = "Highlight electrodes first."; return; }
                var activeSites = Configuration.Channels.Where(c => c.Site != null && !c.Standby).Select(c => (c.Shank, c.Site!.Value)).ToHashSet();
                bool sample = enabled ?? mapSelection.Any(s => !activeSites.Contains(s) && !(Configuration.Profile == ProbeProfile.Np1 && s.Site is 191 or 575 or 959));
                string before = Configuration.Id;
                var result = Configuration.SetSiteSampling(mapSelection, sample);
                bool mapChanged = Configuration.Id != before;
                BindRows(true); SyncMapSelectionToGrid();
                if (mapChanged) MarkDirty();
                status.Text = $"{(sample ? "Enabled" : "Disabled")} {result.Channels.Length} electrodes · {result.ReplacedChannels} previous electrodes disabled · {result.ReservedSites} reference sites skipped" + (dirty ? " · Unsaved changes" : "");
                return;
            }
            var selected = Selected().ToArray();
            if (selected.Length == 0) { status.Text = "Select table rows or a map range first."; return; }
            var connected = selected.Where(c => c.Site != null).ToArray();
            bool enable = enabled ?? connected.Any(c => c.Standby);
            var targets = enable ? connected : selected;
            bool changed = targets.Any(c => c.Standby == enable);
            foreach (var c in targets) c.Standby = !enable;
            grid.Refresh(); if (changed) MarkDirty(); else UpdateState();
            int skipped = enable ? selected.Length - connected.Length : 0;
            status.Text = $"{(enable ? "Enabled" : "Disabled")} {targets.Length} channels · {Configuration.SamplingChannels.Length} enabled in total" +
                (skipped == 0 ? "" : $" · {skipped} rows skipped: choose a site first") + (dirty ? " · Unsaved changes" : "");
        }
        catch (Exception ex) { status.Text = ex.Message; status.ForeColor = Color.Firebrick; }
    }
    private void BrowsePage()
    {
        int first = (int)bank.Value * 384;
        map.BrowseToSite((int)shank.Value, first);
        firstSite.Value = first; lastSite.Value = Math.Min(first + 383, Configuration.SitesPerShank - 1);
    }
    // Synchronize navigation after a map action without navigating again or resetting the Shift-click anchor.
    private void SyncNavigation(int selectedShank, int site)
    {
        bool was = refreshing; refreshing = true;
        try { shank.Value = selectedShank; bank.Value = site / 384; }
        finally { refreshing = was; }
        map.FocusShank(selectedShank);
    }
    private void ApplyRange(int selectedShank, int first, int last, bool scroll)
    {
        try
        {
            FinishEdit();
            int start = Math.Min(first, last), end = Math.Max(first, last);
            SyncNavigation(selectedShank, start); firstSite.Value = start; lastSite.Value = end;
            var result = Configuration.EditRange(selectedShank, start, end - start + 1, (RangeSelectionMode)rangeMode.SelectedIndex, true);
            BindRows(); MarkDirty();
            var channels = result.Channels.ToHashSet();
            if (channels.Count > 0) SelectChannel(result.Channels[0]);
            grid.ClearSelection();
            foreach (DataGridViewRow row in grid.Rows) row.Selected = channels.Contains(((ChannelSetting)row.DataBoundItem!).Channel);
            if (scroll) { map.ResetSelection(); map.GoToSite(start); }
            string action = rangeMode.SelectedIndex == (int)RangeSelectionMode.Remove ? "Removed" : "Selected";
            status.Text = $"{action} {channels.Count} channels · Shank {selectedShank}, sites {start}–{end} · {result.ReservedSites} reference {(result.ReservedSites == 1 ? "site" : "sites")} skipped · {result.ReplacedChannels} previous electrodes disabled · Unsaved changes";
        }
        catch (Exception ex)
        {
            status.Text = ex.Message; status.ForeColor = Color.Firebrick;
        }
    }
    private IEnumerable<ChannelSetting> Selected() => grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<ChannelSetting>();
    private void SyncIdentity()
    {
        int previousGain = Configuration.FixedNp2Gain;
        Configuration.Connector = (int)connector.Value; Configuration.ModelRevision = model.Text;
        Configuration.SerialNumber = serial.Text; Configuration.InternalReferenceBank = (int)refBank.Value;
        if (ProbeConfiguration.IsNp2(Configuration.Profile) && previousGain != Configuration.FixedNp2Gain)
        {
            foreach (var channel in Configuration.Channels) channel.ApGain = channel.LfpGain = Configuration.FixedNp2Gain;
            // Changing the exact NP2 model changes a fixed hardware property.
            foreach (var gain in new[] { apGain, lfpGain }) { gain.Items.Clear(); gain.Items.Add(Configuration.FixedNp2Gain.ToString()); gain.SelectedIndex = 0; }
            foreach (string field in new[] { "ApGain", "LfpGain" })
                ((DataGridViewComboBoxColumn)grid.Columns.Cast<DataGridViewColumn>().Single(c => c.DataPropertyName == field)).DataSource = new[] { Configuration.FixedNp2Gain };
            grid.Refresh();
        }
    }
    private void FinishEdit() { if (!grid.EndEdit()) throw new InvalidOperationException("Finish editing the invalid cell first."); if (grid.DataSource != null) BindingContext?[grid.DataSource]?.EndCurrentEdit(); SyncIdentity(); }
    private void Bind()
    {
        if(targetConnector.HasValue)Configuration.Connector=targetConnector.Value;
        connector.Enabled=!targetConnector.HasValue;
        refreshing = true;
        map.ResetSelection();
        profile.SelectedIndex = (int)Configuration.Profile; connector.Value = Configuration.Connector;
        model.Text = Configuration.ModelRevision; serial.Text = Configuration.SerialNumber; refBank.Value = Configuration.InternalReferenceBank;
        refBank.Enabled = Configuration.Profile == ProbeProfile.Np1;
        balanced.Visible = Configuration.Profile == ProbeProfile.Np2Multi;
        shank.Maximum = Configuration.Shanks - 1; shank.Value = 0; bank.Maximum = (Configuration.SitesPerShank - 1) / 384; bank.Value = 0;
        firstSite.Maximum = lastSite.Maximum = Configuration.SitesPerShank - 1; firstSite.Value = 0; lastSite.Value = 383;
        rangeMode.SelectedIndex = 0;
        bool np2 = ProbeConfiguration.IsNp2(Configuration.Profile);
        reference.Items.Clear(); reference.Items.AddRange(Configuration.References); reference.SelectedIndex = 0;
        foreach (var gain in new[] { apGain, lfpGain }) { gain.Items.Clear(); gain.Items.AddRange((np2 ? new[] { Configuration.FixedNp2Gain } : ProbeConfiguration.Gains).Select(x => x.ToString()).ToArray()); gain.SelectedIndex = 0; gain.Enabled = !np2; }
        apGain.SelectedItem = (np2 ? Configuration.FixedNp2Gain : 1000).ToString(); highPass.Enabled = !np2; highPass.Checked = !np2;
        grid.Columns.Clear();
        AddText("Channel", "Ch", true, 55); AddText("Site", Configuration.Profile == ProbeProfile.Nric1384 ? "Input" : "Site", Configuration.Profile == ProbeProfile.Nric1384, 70);
        AddText("Shank", "Shank", Configuration.Shanks == 1, 60);
        grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Sample", HeaderText = "Sample", FillWeight = 65 });
        grid.Columns.Add(new DataGridViewComboBoxColumn { DataPropertyName = "Reference", HeaderText = "Reference", DataSource = Configuration.References, FillWeight = 95 });
        foreach (var field in new[] { "ApGain", "LfpGain" }) grid.Columns.Add(new DataGridViewComboBoxColumn { DataPropertyName = field, HeaderText = field == "ApGain" ? "AP gain" : "LFP gain", DataSource = np2 ? new[] { Configuration.FixedNp2Gain } : ProbeConfiguration.Gains, ReadOnly = np2, FillWeight = 80 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "HighPass", HeaderText = "AP HPF", ReadOnly = np2, FillWeight = 65 });
        grid.ColumnHeadersHeight = 34; grid.RowTemplate.Height = 25; grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247,249,252);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(222,235,250); grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(24,45,75);
        BindRows(); refreshing = false; BrowsePage(); UpdateState();
    }
    private void AddText(string property, string label, bool readOnly, int weight) => grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = property, HeaderText = label, ReadOnly = readOnly, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
    private void BindRows(bool preserveMapSelection = false)
    {
        bool was = refreshing; refreshing = true;
        if (!preserveMapSelection) mapSelection = null;
        rows = new BindingList<ChannelSetting>(Configuration.Channels); grid.DataSource = rows;
        map.Configuration = Configuration; map.ConflictSite = -1; map.RefreshMap(); refreshing = was;
        UpdateState();
    }
    private void SelectChannel(int channel)
    {
        grid.ClearSelection(); var row = grid.Rows.Cast<DataGridViewRow>().Single(r => ((ChannelSetting)r.DataBoundItem!).Channel == channel);
        row.Selected = true; grid.CurrentCell = row.Cells[0]; grid.FirstDisplayedScrollingRowIndex = Math.Max(0, row.Index - 3);
    }
    private void MarkDirty() { dirty = true; map.ConflictSite = -1; UpdateState(); }
    private void UpdateState()
    {
        var errors = Configuration.Validate();
        int active = Configuration.SamplingChannels.Length;
        enabledCount.Text = $"Enabled electrodes: {active} / {(Configuration.Profile == ProbeProfile.Np1 ? 383 : 384)}";
        status.Text = errors.Count == 0 ? $"{active} channels enabled · IDs start at 0 · {(dirty ? "Unsaved changes" : "Saved / new configuration")}" : string.Join("  ", errors.Take(2));
        status.ForeColor = errors.Count == 0 ? Color.FromArgb(47,64,88) : Color.Firebrick;
        save.Enabled = use.Enabled = export.Enabled = errors.Count == 0;
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is not ChannelSetting c) continue;
            string error = string.Join(" ", errors.Where(e => e.StartsWith($"Channel {c.Channel}: ", StringComparison.Ordinal)));
            row.DefaultCellStyle.BackColor = error.Length == 0 ? Color.Empty : Color.MistyRose;
            foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = error;
        }
        notice.Text = "Blue outline: highlighted electrodes · Teal: enabled · Gray: inactive · Amber: reference · E: enable · D: disable · Space: toggle · Ctrl+A: select all rows";
        UpdateMapSelection(false);
        map.RefreshMap(); Text = (dirty ? "* " : "") + "NP Channel Selector" + (currentPath == null ? "" : " — " + Path.GetFileName(currentPath));
    }
    private bool CanDiscard() => !dirty || MessageBox.Show(this, "Discard unsaved configuration changes?", "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    protected override void OnFormClosing(FormClosingEventArgs e) { if (!UseConfiguration && !CanDiscard()) e.Cancel = true; base.OnFormClosing(e); }
    private void Guard(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Configuration", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
    private void Open()
    {
        if (!CanDiscard()) return;
        using var dialog = new OpenFileDialog { Filter = "NP configuration or IMRO (*.json;*.imro)|*.json;*.imro|NP configuration (*.json)|*.json|SpikeGLX / Open Ephys IMRO (*.imro)|*.imro" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        LoadFile(dialog.FileName);
    }
    private void LoadFile(string path)
    {
        var loaded = StandardFormats.Load(path);
        bool imported = Path.GetExtension(path).Equals(".imro", StringComparison.OrdinalIgnoreCase);
        // IMRO has no physical connector; preserve the selected host context.
        if(imported)loaded.Connector=Configuration.Connector;
        Configuration = loaded; currentPath = imported ? null : path; dirty = imported; Bind();
    }
    private void ExportFile(string extension, Func<string> serialize)
    {
        FinishEdit(); string text = serialize(); // Validate before offering a destination.
        using var dialog = new SaveFileDialog { Filter = $"{extension} (*.{extension})|*.{extension}", FileName = "probe." + extension, DefaultExt = extension };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ProbeConfiguration.AtomicWrite(dialog.FileName, text);
        status.Text = "Exported " + Path.GetFileName(dialog.FileName) + " · Save configuration retains the complete editable selection";
    }
    private void Save()
    {
        FinishEdit(); using var dialog = new SaveFileDialog { Filter = "NP configuration (*.npconfig.json)|*.npconfig.json", FileName = currentPath == null ? "probe.npconfig.json" : Path.GetFileName(currentPath), DefaultExt = "npconfig.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Configuration.Save(dialog.FileName); currentPath = dialog.FileName; dirty = false; UpdateState();
    }
    // A real WinForms layout render for automated QA, without interacting with the desktop.
    public void RenderPreview(string path)
    {
        Show(); Application.DoEvents(); using var bitmap = new Bitmap(Width, Height);
        DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(path); dirty = false; Hide();
    }
    public void RunUiSmoke()
    {
        Show(); Application.DoEvents();
        if (map.Width < 270) throw new Exception("Shank view clipped.");
        RunScrollPaintSmoke();
        RunSelectionShortcutSmoke();
        RunCrossBankSelectionSmoke();
        RunAutomaticReplacementSmoke();
        RunMouseHighlightSmoke();
        RunFormatImportSmoke();
        string navigationBefore = Configuration.Id; bool dirtyBefore = dirty;
        for (int s = 0; s < Configuration.Shanks; s++)
        {
            shank.Value = s;
            for (int b = 0; b <= bank.Maximum; b++)
            {
                bank.Value = b;
                int first = b * 384, y = map.SiteCenter(s, first).Y;
                if (map.ViewedShank != s || firstSite.Value != first || lastSite.Value != Math.Min(first + 383, Configuration.SitesPerShank - 1) || y < 31 || y >= map.ClientSize.Height)
                    throw new Exception("Bank/shank navigation did not update immediately.");
            }
        }
        if (bank.Maximum > 0)
        {
            bank.Value = 0; bank.UpButton();
            if (firstSite.Value != 384 || map.AutoScrollPosition.Y >= 0) throw new Exception("Bank spinner did not navigate immediately.");
            bank.DownButton();
            if (firstSite.Value != 0 || map.AutoScrollPosition.Y != 0) throw new Exception("Bank down button did not return to the first page.");
            bank.Text = bank.Maximum.ToString();
            if (firstSite.Value != bank.Maximum * 384) throw new Exception("Typed bank did not navigate immediately.");
        }
        if (shank.Maximum > 0)
        {
            shank.Value = 0; shank.UpButton();
            if (map.ViewedShank != 1) throw new Exception("Shank spinner did not navigate immediately.");
            shank.Text = "3";
            if (map.ViewedShank != 3) throw new Exception("Typed shank did not navigate immediately.");
        }
        if (Configuration.Id != navigationBefore || dirty != dirtyBefore)
            throw new Exception("Browsing bank/shank changed the sampling configuration.");
        bank.Value = 0; shank.Value = 0;
        if (grid.Columns.Cast<DataGridViewColumn>().Any(c => c.DataPropertyName == "Export") ||
            !grid.Columns.Cast<DataGridViewColumn>().Any(c => c.DataPropertyName == "Sample"))
            throw new Exception("The table does not expose a single sampling selection control.");
        Button Find(Control parent, string text)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is Button b && b.Text == text) return b;
                if (child.HasChildren) { try { return Find(child,text); } catch (KeyNotFoundException) { } }
            }
            throw new KeyNotFoundException(text);
        }
        Find(this,"Disable all").PerformClick(); Application.DoEvents();
        if (Configuration.SamplingChannels.Length != 0) throw new Exception("Clear selection failed.");
        bank.Value = bank.Maximum; Find(this,"Select bank").PerformClick(); Application.DoEvents();
        Configuration.RequireValid();
        if (!save.Enabled) throw new Exception("Valid preset cannot be saved.");
        grid.ClearSelection(); grid.Rows[0].Selected = true;
        Find(this,"Sample off (D)").PerformClick(); Application.DoEvents();
        if (!Configuration.Channels[0].Standby) throw new Exception("Bulk sampling setting did not bind.");
        Find(this,"Sample on (E)").PerformClick(); Application.DoEvents();
        if (Configuration.Channels[0].Standby) throw new Exception("Bulk sampling setting did not bind.");
        if (Configuration.Profile != ProbeProfile.Nric1384)
        {
            grid.Rows[0].Cells[1].Value = Configuration.SitesPerShank + 1; FinishEdit(); UpdateState();
            if (save.Enabled) throw new Exception("Invalid routing not rejected by UI.");
            Find(this,"Disable all").PerformClick();
            if (!save.Enabled) throw new Exception("UI did not recover from invalid edit.");
        }
        Find(this,"Disable all").PerformClick(); map.GoToSite(0); Application.DoEvents();
        void Mouse(string method, Point point) => typeof(ProbeView).GetMethod(method,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(map,
                [new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)]);
        void Drag(int start, int end, int s = 0)
        {
            string unchanged = Configuration.Id;
            Mouse("OnMouseDown", map.SiteCenter(s, start));
            Mouse("OnMouseMove", map.SiteCenter(s, end));
            Mouse("OnMouseUp", map.SiteCenter(s, end));
            if (Configuration.Id != unchanged) throw new Exception("Mouse drag changed sampling before an explicit command.");
            ApplyRange(s, start, end, false);
        }
        Mouse("OnMouseDown", map.SiteCenter(0, 5)); Mouse("OnMouseUp", map.SiteCenter(0, 5));
        if (Configuration.SamplingChannels.Length != 0) throw new Exception("Single click changed sampling.");
        SetSampling(true);
        if (Configuration.SamplingChannels.Length != 1) throw new Exception("Single click did not select a site exactly once.");
        Mouse("OnMouseDown", map.SiteCenter(0, 5)); Mouse("OnMouseUp", map.SiteCenter(0, 5));
        if (Configuration.SamplingChannels.Length != 1) throw new Exception("Clicking an enabled electrode changed sampling.");
        SetSampling(null);
        if (Configuration.SamplingChannels.Length != 0) throw new Exception("Explicit toggle did not disable the highlighted site.");
        Drag(10, 30);
        if (Configuration.SamplingChannels.Length != 21 || grid.SelectedRows.Count != 21 || firstSite.Value != 10 || lastSite.Value != 30)
            throw new Exception("Drag did not select both endpoints or select the corresponding table rows.");
        Find(this,"Sample off (D)").PerformClick();
        if (Configuration.SamplingChannels.Length != 0) throw new Exception("Bulk editing the dragged range missed channels.");
        Find(this,"Sample on (E)").PerformClick();
        Drag(50, 40);
        if (Configuration.SamplingChannels.Length != 32) throw new Exception("Reverse drag did not preserve the previous range.");
        map.BeginSelection(map.SiteCenter(0, 60), true); Mouse("OnMouseUp", map.SiteCenter(0, 60));
        if (Configuration.SamplingChannels.Length != 32) throw new Exception("Shift-click changed sampling.");
        SetSampling(true);
        if (Configuration.SamplingChannels.Length != 42 || firstSite.Value != 50 || lastSite.Value != 60)
            throw new Exception("Shift-click did not extend from the previous anchor.");
        rangeMode.SelectedIndex = (int)RangeSelectionMode.Remove; Drag(20, 25);
        if (Configuration.SamplingChannels.Length != 36) throw new Exception("Remove-range gesture failed.");
        rangeMode.SelectedIndex = (int)RangeSelectionMode.ReplaceAll;
        firstSite.Value = 90; lastSite.Value = 80; Find(this,"Apply range").PerformClick();
        if (Configuration.SamplingChannels.Length != 11 || firstSite.Value != 80 || lastSite.Value != 90)
            throw new Exception("Typed reverse range failed.");
        rangeMode.SelectedIndex = (int)RangeSelectionMode.Add;
        string before = Configuration.Id;
        Mouse("OnMouseDown", map.SiteCenter(0, 80)); Mouse("OnMouseMove", map.SiteCenter(0, 100));
        if (Configuration.Id != before) throw new Exception("Dragging changed configuration before release.");
        var escape = typeof(ProbeView).GetMethod("ProcessCmdKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        escape.Invoke(map, [new Message(), Keys.Escape]); Mouse("OnMouseUp", map.SiteCenter(0, 100));
        if (Configuration.Id != before || map.Capture) throw new Exception("Escape failed to cancel range selection.");
        Mouse("OnMouseDown", map.SiteCenter(0, 80)); Mouse("OnMouseMove", map.SiteCenter(0, 100));
        map.Capture = false; Mouse("OnMouseUp", map.SiteCenter(0, 100));
        if (Configuration.Id != before) throw new Exception("Lost capture failed to cancel range selection.");
        int top = map.AutoScrollPosition.Y;
        Mouse("OnMouseDown", map.SiteCenter(0, 80));
        Mouse("OnMouseMove", new Point(map.SiteCenter(0, 80).X, map.ClientSize.Height - 5));
        map.ScrollSelection();
        if (map.AutoScrollPosition.Y >= top || Configuration.Id != before) throw new Exception("Drag auto-scroll failed or applied prematurely.");
        escape.Invoke(map, [new Message(), Keys.Escape]);
        map.GoToSite(0);
        before = Configuration.Id;
        Mouse("OnMouseDown", new Point(map.SiteCenter(0, 0).X, 15));
        Mouse("OnMouseUp", new Point(map.SiteCenter(0, 0).X, 15));
        if (Configuration.Id != before) throw new Exception("Sticky header was treated as an electrode.");
        if (Configuration.Profile != ProbeProfile.Nric1384)
        {
            firstSite.Value = 0; lastSite.Value = 384;
            Find(this,"Apply range").PerformClick();
            if (Configuration.Id != before || status.ForeColor != Color.Firebrick || !save.Enabled)
                throw new Exception("Conflicting range changed the selection or did not report the error.");
        }
        if (Configuration.Profile == ProbeProfile.Np1)
        {
            firstSite.Value = 180; lastSite.Value = 200; rangeMode.SelectedIndex = (int)RangeSelectionMode.ReplaceAll;
            Find(this,"Apply range").PerformClick();
            if (Configuration.SamplingChannels.Length != 20 || !status.Text.Contains("1 reference site skipped"))
                throw new Exception("Reserved-site range feedback is missing.");
        }
        if (Configuration.Profile == ProbeProfile.Np2Multi)
        {
            Find(this,"Disable all").PerformClick(); map.GoToSite(0); rangeMode.SelectedIndex = (int)RangeSelectionMode.Add;
            Drag(0, 15, 2);
            if (Configuration.SamplingChannels.Length != 16 || Configuration.Channels.Any(c => !c.Standby && c.Shank != 2))
                throw new Exception("Multi-shank drag selected the wrong shank.");
        }
        Configuration.RequireValid(); rangeMode.SelectedIndex = (int)RangeSelectionMode.Add;
        if (embeddedMode)
        {
            foreach (var c in Configuration.Channels) c.Export = false;
            UpdateState();
            if (!use.Enabled)
                throw new Exception("Retaining the requested sampling configuration depends on the legacy export mask.");
            Find(this,"Use configuration").PerformClick();
            if (!UseConfiguration || DialogResult != DialogResult.OK) throw new Exception("Requested configuration was not retained.");
            return;
        }
        dirty = false; Hide();
    }
    private void RunFormatImportSmoke()
    {
        if (Configuration.Profile == ProbeProfile.Nric1384) return;
        var original = Configuration; bool wasDirty = dirty; string? oldPath = currentPath;
        string path = Path.Combine(Path.GetTempPath(), "npconfig-ui-" + Guid.NewGuid().ToString("N") + ".imro");
        try
        {
            File.WriteAllText(path, StandardFormats.ToImro(ProbeConfiguration.Create(Configuration.Profile)));
            LoadFile(path);
            if (!dirty || currentPath != null || Configuration.Profile != original.Profile || !export.Visible)
                throw new Exception("IMRO import did not create an editable unsaved configuration.");
            if (ProbeConfiguration.IsNp2(Configuration.Profile))
            {
                model.Text = Configuration.Profile == ProbeProfile.Np2Single ? "NP2000" : "NP2010";
                Configuration.RequireValid();
                if (Configuration.Channels.Any(c => c.ApGain != 80) || apGain.SelectedItem?.ToString() != "80")
                    throw new Exception("Legacy NP2 fixed gain was not shown after changing the model.");
                File.WriteAllText(path, StandardFormats.ToImro(Configuration)); LoadFile(path);
                if (Configuration.FixedNp2Gain != 80) throw new Exception("Legacy IMRO import lost its gain.");
                model.Text = Configuration.Profile == ProbeProfile.Np2Single ? "NP2003" : "NP2013";
                Configuration.RequireValid();
                if (Configuration.Channels.Any(c => c.ApGain != 100) || apGain.SelectedItem?.ToString() != "100")
                    throw new Exception("Commercial NP2 fixed gain was not restored after changing the model.");
            }
            string before = Configuration.Id;
            File.WriteAllText(path, "(0,384)(0 0 0 500 250 1)");
            try { LoadFile(path); throw new Exception("Incomplete IMRO was accepted."); }
            catch (InvalidDataException) { }
            if (Configuration.Id != before) throw new Exception("Failed IMRO import replaced the current configuration.");
        }
        finally { File.Delete(path); Configuration = original; currentPath = oldPath; dirty = wasDirty; Bind(); }
    }
    private void RunMouseHighlightSmoke()
    {
        var original = Configuration.Clone(); bool wasDirty = dirty;
        void Gesture(int s, int first, int last, bool additive = false)
        {
            string before = Configuration.Id, countBefore = enabledCount.Text; bool dirtyBefore = dirty;
            map.GoToSite(first); map.BeginSelection(map.SiteCenter(s, first), false, additive);
            map.EnsureSiteVisible(last); map.MoveSelection(map.SiteCenter(s, last)); map.EndSelection(map.SiteCenter(s, last));
            if (Configuration.Id != before || dirty != dirtyBefore || enabledCount.Text != countBefore)
                throw new Exception("A mouse gesture changed assignments, sampling, dirty state, or the enabled count.");
        }
        void Key(Keys key)
        {
            map.Focus(); var message = Message.Create(map.Handle, 0x0100, (IntPtr)key, IntPtr.Zero);
            if (!map.PreProcessMessage(ref message)) throw new Exception("A sampling shortcut was not handled.");
        }
        void Highlighted(params (int Shank, int Site)[] expected)
        {
            if (map.HighlightedSites == null || !map.HighlightedSites.SetEquals(expected))
                throw new Exception("Physical electrode highlights do not match the mouse selection.");
        }
        try
        {
            Configuration.ClearSites(); BindRows(); dirty = false; UpdateState();
            Gesture(0, 5, 5); Highlighted((0, 5));
            using (var bitmap = new Bitmap(map.Width, map.Height))
            {
                map.DrawToBitmap(bitmap, map.ClientRectangle); var point = map.SiteCenter(0, 5);
                if (bitmap.GetPixel(point.X - 4, point.Y).ToArgb() != Color.FromArgb(34, 92, 205).ToArgb())
                    throw new Exception("An inactive/unassigned highlighted electrode has no blue outline.");
            }
            Gesture(0, 100, 100, true); Highlighted((0, 5), (0, 100));
            Gesture(0, 5, 5, true); Highlighted((0, 100));
            Gesture(0, 5, 5, true); Highlighted((0, 5), (0, 100));
            Key(Keys.E);
            if (Configuration.SamplingChannels.Length != 2 || !dirty) throw new Exception("E did not enable separate highlighted electrodes.");
            Highlighted((0, 5), (0, 100));
            Key(Keys.D);
            if (Configuration.SamplingChannels.Length != 0) throw new Exception("D did not disable both highlighted electrodes.");
            Highlighted((0, 5), (0, 100));
            Key(Keys.Space);
            if (Configuration.SamplingChannels.Length != 2) throw new Exception("Space did not re-enable highlighted electrodes.");
            foreach (var mode in Enum.GetValues<RangeSelectionMode>())
            {
                rangeMode.SelectedIndex = (int)mode;
                Gesture(0, 10, 30); Highlighted(Enumerable.Range(10, 21).Select(site => (0, site)).ToArray());
            }
            if (Configuration.Profile == ProbeProfile.Np1)
            {
                Gesture(0, 191, 191); string beforeReserved = Configuration.Id;
                Key(Keys.E);
                if (Configuration.Id != beforeReserved || !status.Text.Contains("1 reference sites skipped"))
                    throw new Exception("A highlighted reference electrode was not skipped on enable.");
            }
            if (Configuration.SitesPerShank > 384)
            {
                Configuration = ProbeConfiguration.Create(Configuration.Profile); Bind(); dirty = false; UpdateState();
                int s = Configuration.Shanks - 1;
                int channel = Catalog.Channel(Configuration.Profile, s, 384);
                var old = Configuration.Channels[channel]; var oldSite = (old.Shank, old.Site!.Value);
                Gesture(s, 384, 384); Highlighted((s, 384));
                string before = Configuration.Id;
                Key(Keys.D);
                if (Configuration.Id != before || dirty) throw new Exception("D on an unassigned electrode disabled its conflicting active electrode.");
                // Switching to the table explicitly targets its current assignment instead of the map's pending site.
                grid.Focus();
                if (map.HighlightedSites != null) throw new Exception("Table focus left a stale physical selection active.");
                map.Focus(); Gesture(s, 384, 384); Key(Keys.E);
                if (Configuration.Channels[channel].Site != 384 || Configuration.Channels[channel].Shank != s)
                    throw new Exception("E did not assign the highlighted physical site.");
                Gesture(oldSite.Shank, oldSite.Value, oldSite.Value, true);
                Highlighted((s, 384), oldSite);
                before = Configuration.Id;
                Key(Keys.E);
                if (Configuration.Id != before || status.ForeColor != Color.Firebrick)
                    throw new Exception("Two highlighted sites sharing a channel were partially enabled.");
                Gesture(0, 0, 384); before = Configuration.Id;
                Key(Keys.E);
                if (Configuration.Id != before) throw new Exception("An internally conflicting highlighted range changed sampling.");
            }
            Configuration.RequireValid();
        }
        finally { Configuration = original; Bind(); dirty = wasDirty; UpdateState(); }
    }
    private void RunAutomaticReplacementSmoke()
    {
        var original = Configuration.Clone(); bool wasDirty = dirty;
        int capacity = Configuration.Profile == ProbeProfile.Np1 ? 383 : 384;
        void CheckCount(int expected)
        {
            if (enabledCount.Text != $"Enabled electrodes: {expected} / {capacity}" ||
                !enabledCount.Visible || !enabledCount.Parent!.ClientRectangle.Contains(enabledCount.Bounds))
                throw new Exception($"Enabled count for {Configuration.Profile}: expected {expected}/{capacity}, got '{enabledCount.Text}', visible={enabledCount.Visible}, bounds={enabledCount.Bounds}, container={enabledCount.Parent!.ClientRectangle}.");
        }
        void Click(int s, int site)
        {
            map.GoToSite(site);
            var point = map.SiteCenter(s, site);
            string before = Configuration.Id;
            map.BeginSelection(point, false); map.EndSelection(point);
            if (Configuration.Id != before) throw new Exception("Mouse selection applied a replacement before a sampling command.");
            SetSampling(null);
        }
        try
        {
            CheckCount(capacity);
            Size = MinimumSize; Application.DoEvents(); CheckCount(capacity);
            Size = new Size(1450, 920); Application.DoEvents();
            Configuration.ClearSites(); BindRows(); CheckCount(0);
            Click(0, 5); Click(0, 100); CheckCount(2);
            if (!new HashSet<int> { 5, 100 }.SetEquals(Configuration.Channels.Where(c => !c.Standby).Select(c => c.Site!.Value)))
                throw new Exception("Separate electrode clicks did not retain a discontinuous selection.");
            SetSampling(false); CheckCount(1); SetSampling(true); CheckCount(2);
            int sampleColumn = grid.Columns.Cast<DataGridViewColumn>().Single(c => c.DataPropertyName == "Sample").Index;
            int channel100 = Catalog.Channel(Configuration.Profile, 0, 100);
            grid.Rows[channel100].Cells[sampleColumn].Value = false; CheckCount(1);
            grid.Rows[channel100].Cells[sampleColumn].Value = true; CheckCount(2);

            if (Configuration.SitesPerShank <= 384) return;
            for (int s = 0; s < Configuration.Shanks; s++)
            {
                Configuration = ProbeConfiguration.Create(Configuration.Profile); Bind();
                int channel = Catalog.Channel(Configuration.Profile, s, 384);
                var old = Configuration.Channels[channel]; int oldSite = old.Site!.Value, oldShank = old.Shank;
                old.ApGain = ProbeConfiguration.IsNp2(Configuration.Profile) ? 100 : 500;
                var others = Configuration.Channels.Where(c => c.Channel != channel).Select(c => (c.Site, c.Shank, c.Standby)).ToArray();
                Click(s, 384); CheckCount(capacity);
                var current = Configuration.Channels[channel];
                if (current.Site != 384 || current.Shank != s || current.Standby ||
                    current.ApGain != (ProbeConfiguration.IsNp2(Configuration.Profile) ? 100 : 500) ||
                    Configuration.Channels.Any(c => c.Site == oldSite && c.Shank == oldShank) ||
                    !others.SequenceEqual(Configuration.Channels.Where(c => c.Channel != channel).Select(c => (c.Site, c.Shank, c.Standby))) ||
                    !status.Text.Contains("1 previous electrodes disabled"))
                    throw new Exception("Enabling a conflicting electrode did not automatically replace only the old electrode.");
                if (!map.HighlightedChannels.SetEquals(new[] { channel })) throw new Exception("Replacement highlight did not follow the new electrode.");
                int Pixel(int shankNumber, int site)
                {
                    map.GoToSite(site);
                    using var bitmap = new Bitmap(map.Width, map.Height);
                    map.DrawToBitmap(bitmap, map.ClientRectangle);
                    var point = map.SiteCenter(shankNumber, site);
                    return bitmap.GetPixel(point.X, point.Y).ToArgb();
                }
                if (Pixel(s, 384) != Color.FromArgb(15, 133, 126).ToArgb() ||
                    Pixel(oldShank, oldSite) != Color.FromArgb(206, 213, 224).ToArgb())
                    throw new Exception("Map did not show the new electrode enabled and the displaced electrode inactive.");
                Click(s, 384); CheckCount(capacity - 1);
                if (!Configuration.Channels[channel].Standby) throw new Exception("Clicking the replacement electrode did not toggle it off.");
                Click(oldShank, oldSite); CheckCount(capacity);
                Configuration.RequireValid();
            }
            Configuration = ProbeConfiguration.Create(Configuration.Profile); Bind();
            var displaced = Enumerable.Range(384, 7).Select(site => Catalog.Channel(Configuration.Profile, 0, site)).ToHashSet();
            var unrelated = Configuration.Channels.Where(c => !displaced.Contains(c.Channel)).Select(c => (c.Site, c.Shank, c.Standby)).ToArray();
            ApplyRange(0, 380, 390, true); CheckCount(capacity);
            if (!Enumerable.Range(380, 11).All(site => Configuration.Channels.Any(c => c.Site == site && c.Shank == 0 && !c.Standby)) ||
                !unrelated.SequenceEqual(Configuration.Channels.Where(c => !displaced.Contains(c.Channel)).Select(c => (c.Site, c.Shank, c.Standby))) ||
                !status.Text.Contains("7 previous electrodes disabled"))
                throw new Exception("Adding a range did not automatically replace its conflicts while preserving unrelated electrodes.");
            string before = Configuration.Id;
            ApplyRange(0, 0, 384, true); CheckCount(capacity);
            if (Configuration.Id != before || status.ForeColor != Color.Firebrick)
                throw new Exception("Automatic replacement allowed two requested sites on one channel.");
        }
        finally { Configuration = original; Bind(); dirty = wasDirty; UpdateState(); }
    }
    private void RunCrossBankSelectionSmoke()
    {
        if (Configuration.SitesPerShank <= 384) return;
        var original = Configuration.Clone(); bool wasDirty = dirty;
        void Mouse(string method, int s, int site)
        {
            var point = map.SiteCenter(s, site);
            if (!map.ClientRectangle.Contains(point) || point.Y < 31) throw new Exception("Cross-bank test endpoint is outside the map.");
            typeof(ProbeView).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(map,
                [new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)]);
        }
        void Click(int s, int site, bool extend = false)
        {
            map.BeginSelection(map.SiteCenter(s, site), extend); Mouse("OnMouseUp", s, site);
        }
        void Clear(int s, int site)
        {
            Configuration.ClearSites(); map.ResetSelection(); BindRows(); SyncNavigation(s, site); map.GoToSite(site);
        }
        void CheckRange(int s, int first, int last, string action)
        {
            SetSampling(true);
            var sites = Configuration.Channels.Where(c => c.Site != null && !c.Standby).ToArray();
            var expected = Enumerable.Range(first, last - first + 1).ToHashSet();
            if (sites.Any(c => c.Shank != s) || !expected.SetEquals(sites.Select(c => c.Site!.Value)) ||
                !map.HighlightedChannels.SetEquals(sites.Select(c => c.Channel)) || grid.SelectedRows.Count != expected.Count)
                throw new Exception($"{Configuration.Profile} shank {s}: {action} failed for sites {first}–{last}.");
            Configuration.RequireValid();
        }
        try
        {
            rangeMode.SelectedIndex = (int)RangeSelectionMode.Add;
            for (int s = 0; s < Configuration.Shanks; s++)
            for (int b = 1; b <= bank.Maximum; b++)
            {
                int first = b * 384 - 4, last = b * 384 + 6;
                Clear(s, first); Click(s, first);
                string beforeNavigation = Configuration.Id;
                bank.Focus(); bank.UpButton();
                if (Configuration.Id != beforeNavigation) throw new Exception("Bank navigation changed sampling.");
                Click(s, last, true); CheckRange(s, first, last, "Shift-click after bank up");
                map.Focus();
                var key = Message.Create(map.Handle, 0x0100, (IntPtr)Keys.D, IntPtr.Zero);
                map.PreProcessMessage(ref key);
                if (Configuration.SamplingChannels.Length != 0 || grid.SelectedRows.Count != last - first + 1)
                    throw new Exception("D missed a bank in a cross-bank selection.");
                key = Message.Create(map.Handle, 0x0100, (IntPtr)Keys.E, IntPtr.Zero);
                map.PreProcessMessage(ref key); CheckRange(s, first, last, "Cross-bank enable");

                Clear(s, last); Click(s, last);
                bank.Focus(); bank.DownButton(); map.EnsureSiteVisible(first);
                Click(s, first, true); CheckRange(s, first, last, "Reverse Shift-click after bank down");

                Clear(s, first); Click(s, first);
                bank.Focus(); bank.Text = b.ToString();
                Click(s, last, true); CheckRange(s, first, last, "Shift-click after typed bank");

                Clear(s, first);
                Mouse("OnMouseDown", s, first); Mouse("OnMouseMove", s, last); Mouse("OnMouseUp", s, last);
                CheckRange(s, first, last, "Drag across bank boundary");

                Clear(s, first);
                firstSite.Value = last; lastSite.Value = first;
                ApplyRange(s, (int)firstSite.Value, (int)lastSite.Value, true);
                CheckRange(s, first, last, "Typed reverse cross-bank range");
            }
            // Disabling the old bank must release its channels for a new range without requiring replacement.
            Configuration.SelectRange(0, 0, 384); BindRows(); grid.SelectAll(); SetSampling(false);
            map.ResetSelection(); SyncNavigation(0, 380); map.GoToSite(380);
            Mouse("OnMouseDown", 0, 380); Mouse("OnMouseMove", 0, 390); Mouse("OnMouseUp", 0, 390);
            CheckRange(0, 380, 390, "Cross-bank range after disabling the old bank");
        }
        finally { Configuration = original; Bind(); dirty = wasDirty; UpdateState(); }
    }
    private void RunScrollPaintSmoke()
    {
        // DrawToBitmap forces a fresh paint and hides scroll-copy artifacts. Observe live WM_PAINT instead.
        var clips = new List<Rectangle>();
        PaintEventHandler painted = (_, e) => clips.Add(e.ClipRectangle);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var wheel = typeof(ProbeView).GetMethod("OnMouseWheel", flags)!;
        var windowMessage = typeof(ProbeView).GetMethod("WndProc", flags)!;
        void CheckScroll(Action action, string name)
        {
            int before = map.AutoScrollPosition.Y;
            clips.Clear(); action(); map.Update();
            if (map.AutoScrollPosition.Y == before) throw new Exception(name + " did not move the viewport.");
            if (!clips.Any(clip => clip.Contains(map.ClientRectangle)))
                throw new Exception(name + " reused old viewport pixels instead of repainting the fixed shank header and sites.");
        }
        void Scrollbar(int command) => windowMessage.Invoke(map,
            [Message.Create(map.Handle, 0x0115, (IntPtr)command, IntPtr.Zero)]); // WM_VSCROLL
        string beforeConfiguration = Configuration.Id;
        map.GoToSite(300); map.Refresh(); Application.DoEvents();
        map.Paint += painted;
        try
        {
            foreach (int delta in new[] { 120, 120, -120, -120, 120, -120 })
                CheckScroll(() => wheel.Invoke(map, [new MouseEventArgs(MouseButtons.None, 0, map.Width / 2, map.Height / 2, delta)]), "Mouse-wheel scroll");
            foreach (int command in new[] { 0, 1, 2, 3 }) // line up/down and page up/down
                CheckScroll(() => Scrollbar(command), "Scrollbar scroll");
            if (Configuration.Id != beforeConfiguration) throw new Exception("Scrolling changed the channel selection.");
        }
        finally { map.Paint -= painted; map.GoToSite(0); map.Refresh(); }
    }
    private void RunSelectionShortcutSmoke()
    {
        var original = Configuration.Clone(); bool wasDirty = dirty;
        bool Key(Control target, Keys key)
        {
            var message = Message.Create(target.Handle, 0x0100, (IntPtr)key, IntPtr.Zero);
            return target.PreProcessMessage(ref message);
        }
        try
        {
            Configuration.SelectRange(0, 0, 21); BindRows(); map.GoToSite(0);
            var chosen = Configuration.Channels.Where(c => c.Site != null && !c.Standby).Take(3).ToArray();
            var ids = chosen.Select(c => c.Channel).ToHashSet();
            grid.Focus(); grid.CurrentCell = grid.Rows[chosen[0].Channel].Cells[0]; grid.ClearSelection();
            foreach (int id in ids) grid.Rows[id].Selected = true;
            if (!map.HighlightedChannels.SetEquals(ids)) throw new Exception("Map does not highlight all selected rows.");
            using (var bitmap = new Bitmap(map.Width, map.Height))
            {
                map.DrawToBitmap(bitmap, map.ClientRectangle);
                foreach (var c in chosen)
                {
                    var point = map.SiteCenter(c.Shank, c.Site!.Value);
                    if (bitmap.GetPixel(point.X - 4, point.Y).ToArgb() != Color.FromArgb(34, 92, 205).ToArgb())
                        throw new Exception("A selected electrode's blue outline was not rendered.");
                }
            }
            int enabled = Configuration.SamplingChannels.Length;
            if (!Key(grid, Keys.D) || chosen.Any(c => !c.Standby) || Configuration.SamplingChannels.Length != enabled - 3)
                throw new Exception("D did not disable exactly the selected table rows.");
            if (!map.HighlightedChannels.SetEquals(ids)) throw new Exception("Disabled electrodes lost their selection highlight.");
            map.Focus();
            if (!Key(map, Keys.E) || chosen.Any(c => c.Standby)) throw new Exception("E did not enable the selected rows from map focus.");
            chosen[0].Standby = true; grid.Refresh();
            grid.Focus();
            if (!Key(grid, Keys.Space) || chosen.Any(c => c.Standby)) throw new Exception("Space did not enable a mixed group.");
            if (!Key(grid, Keys.Space) || chosen.Any(c => !c.Standby)) throw new Exception("Space did not disable an enabled group.");
            string beforeRepeat = Configuration.Id;
            var repeat = Message.Create(grid.Handle, 0x0100, (IntPtr)Keys.Space, (IntPtr)(1L << 30));
            if (!grid.PreProcessMessage(ref repeat) || Configuration.Id != beforeRepeat) throw new Exception("Holding Space repeatedly toggled sampling.");
            string beforeTyping = Configuration.Id;
            model.Focus(); Key(model, Keys.E);
            firstSite.Focus(); Key(firstSite, Keys.Space);
            if (Configuration.Id != beforeTyping) throw new Exception("Typing outside the table/map triggered sampling shortcuts.");
            grid.Focus();
            grid.CurrentCell = grid.Rows[chosen[0].Channel].Cells[grid.Columns.Cast<DataGridViewColumn>().Single(c => c.DataPropertyName == "Reference").Index];
            if (!grid.BeginEdit(false) || grid.EditingControl == null) throw new Exception("Cell editing smoke setup failed.");
            Key(grid.EditingControl, Keys.E);
            if (Configuration.Id != beforeTyping) throw new Exception("Typing in a cell editor triggered sampling shortcuts.");
            grid.CancelEdit(); grid.EndEdit();
            map.Focus();
            var selectAll = Message.Create(map.Handle, 0x0100, (IntPtr)Keys.A, IntPtr.Zero);
            if (!ProcessCmdKey(ref selectAll, Keys.Control | Keys.A) || grid.SelectedRows.Count != 384 || map.HighlightedChannels.Count != 384)
                throw new Exception("Ctrl+A did not select and highlight all rows from the map.");
            int connected = Configuration.Channels.Count(c => c.Site != null);
            if (!Key(map, Keys.E) || Configuration.SamplingChannels.Length != connected)
                throw new Exception("Enable-all failed to skip unconnected channels.");
            Configuration.RequireValid();
            grid.ClearSelection();
            if (map.HighlightedChannels.Count != 0) throw new Exception("Clearing row selection left stale highlights.");
            string empty = Configuration.Id; Key(map, Keys.D);
            if (Configuration.Id != empty) throw new Exception("A shortcut with no selection changed the configuration.");
            if (Configuration.SitesPerShank > 384)
            {
                int s = Configuration.Shanks - 1;
                Configuration.SelectRange(s, 384, 20); BindRows(); map.GoToSite(0); grid.Focus();
                SelectChannel(Catalog.Channel(Configuration.Profile, s, 384));
                int y = map.SiteCenter(s, 384).Y;
                if (map.ViewedShank != s || bank.Value != 1 || y < 36 || y >= map.ClientSize.Height)
                    throw new Exception("Table selection did not reveal the mapped electrode on the correct shank/bank.");
            }
        }
        finally { grid.CancelEdit(); Configuration = original; Bind(); dirty = wasDirty; UpdateState(); }
    }
}
