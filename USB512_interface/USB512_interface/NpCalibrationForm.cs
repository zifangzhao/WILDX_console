using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USB512_interface
{
    public partial class Form1 { NpCalibrationSet npRunCalibration; Button calibrationButton; }
    // Selections belong to the chosen USB peer/BLE address or explicit offline
    // workspace. Active runs retain their immutable factory-file snapshot.
    internal static class NpCalibrationStore
    {
        static readonly Dictionary<string, NpCalibrationSet> selections = new Dictionary<string, NpCalibrationSet>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string,Dictionary<int,byte[]>> imros=new Dictionary<string,Dictionary<int,byte[]>>(StringComparer.OrdinalIgnoreCase);
        internal static string LibraryFolder {get{return Properties.Settings.Default.NpCalibrationFolder;}set{Properties.Settings.Default.NpCalibrationFolder=value;Properties.Settings.Default.Save();}}
        static string Key(string source) { if (String.IsNullOrWhiteSpace(source)) return "offline"; int at = source.LastIndexOf('#'); return at > 0 ? source.Substring(0, at) : source; }
        internal static NpCalibrationSet Get(string source) { NpCalibrationSet value; return selections.TryGetValue(Key(source), out value) ? value.Copy() : new NpCalibrationSet(); }
        internal static void Set(string source, NpCalibrationSet value) { selections[Key(source)] = value.Copy();imros.Remove(Key(source)); }
        internal static void SetImros(string source,Dictionary<int,byte[]> value){imros[Key(source)]=value.ToDictionary(p=>p.Key,p=>(byte[])p.Value.Clone());}
        internal static void SaveImros(string source,string folder){Dictionary<int,byte[]> selected;if(!imros.TryGetValue(Key(source),out selected))return;
            Directory.CreateDirectory(Path.Combine(folder,"calibration"));foreach(var pair in selected)File.WriteAllBytes(Path.Combine(folder,"calibration","probe"+pair.Key+".imro"),pair.Value);}
        internal static void Show(IWin32Window owner, string source, bool? np2 = null,IDictionary<int,string> configurations=null) { using (var form = new NpCalibrationForm(source, np2,configurations)) form.ShowDialog(owner); }
    }

    internal sealed class NpCalibrationManualForm : Form
    {
        readonly string source; readonly bool? np2; readonly NpCalibrationSet calibration;
        readonly TableLayoutPanel layout;
        readonly ComboBox port = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        readonly TextBox serial = new TextBox(), gain = new TextBox(), adc = new TextBox();
        readonly ComboBox apGain = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 }, lfpGain = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        readonly ComboBox encoding = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 }, band = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        readonly TextBox order = new TextBox { Text = "1", Width = 85 };
        readonly Label status = new Label { Dock = DockStyle.Fill, AutoSize = true };
        readonly Button apply = new Button { Text = "Load for probe", AutoSize = true }, correct = new Button { Text = "Correct DAT…", AutoSize = true }, batch = new Button { Text = "Calibrate folders…", AutoSize = true }, close = new Button { Text = "Done", AutoSize = true };
        bool busy; volatile bool cancelled;
        internal NpCalibrationManualForm(string source, bool? np2)
        {
            this.source = source; this.np2 = np2; calibration = NpCalibrationStore.Get(source);
            Text = "WILD-X — Calibration"; ClientSize = new Size(950, 430); MinimumSize = new Size(850, 430); StartPosition = FormStartPosition.CenterParent;
            layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 8 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int p = 1; p <= 3; p++) port.Items.Add(p); port.SelectedIndex = 0;
            foreach (int value in new[] { 50, 125, 250, 500, 1000, 1500, 2000, 3000 }) { apGain.Items.Add(value); lfpGain.Items.Add(value); }
            encoding.Items.AddRange(new object[] { "Signed nominal counts", "Legacy unsigned ADC codes" }); encoding.SelectedIndex = 0;
            band.Items.AddRange(new object[] { "AP", "LFP" }); band.SelectedIndex = 0;
            Row(layout, 0, "Probe", port); Row(layout, 1, "Probe serial", serial);
            Row(layout, 2, "Gain CSV", FileRow(gain)); Row(layout, 3, "ADC CSV (NP1)", FileRow(adc));
            var gains = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; gains.Controls.AddRange(new Control[] { new Label { Text = "NP1 AP gain", AutoSize = true }, apGain, new Label { Text = "LFP gain", AutoSize = true }, lfpGain }); Row(layout, 4, "Acquired gains", gains);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; var clear = new Button { Text = "Clear probe", AutoSize = true }; actions.Controls.AddRange(new Control[] { apply, clear, close }); Row(layout, 5, "", actions);
            var offline = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; offline.Controls.AddRange(new Control[] { correct, batch, encoding, band, new Label { Text = "Probe order", AutoSize = true }, order }); Row(layout, 6, "Offline", offline);
            Row(layout, 7, "", status); Controls.Add(layout); WildXTheme.Apply(this); WildXTheme.Button(apply, true); WildXTheme.Button(correct, true);
            port.SelectedIndexChanged += delegate { SelectPort(); };
            apply.Click += delegate { try { LoadProfile(); } catch (Exception ex) { status.Text = ex.Message; } };
            clear.Click += delegate { if (busy) return; calibration.Remove(SelectedPort); NpCalibrationStore.Set(source, calibration); SelectPort(); };
            close.Click += delegate { if (busy) { cancelled = true; return; } Close(); };
            correct.Click += async delegate { await CorrectDat(); };
            batch.Click += delegate { if(!busy)using(var form=new NpCalibrationBatchForm(calibration))form.ShowDialog(this); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { cancelled = true; e.Cancel = true; } };
            SelectPort();
        }
        int SelectedPort { get { return (int)port.SelectedItem; } }
        static void Row(TableLayoutPanel layout, int row, string title, Control control) { layout.Controls.Add(new Label { Text = title, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row); control.Dock = DockStyle.Fill; layout.Controls.Add(control, 1, row); }
        Control FileRow(TextBox text) {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 }; row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            text.Dock=DockStyle.Fill;
            var browse = new Button { Text = "Browse…", AutoSize = true }; row.Controls.Add(text, 0, 0); row.Controls.Add(browse, 1, 0);
            browse.Click += delegate { if (busy) return; using (var file = new OpenFileDialog { Filter = "Factory calibration CSV|*.csv" }) if (file.ShowDialog(this) == DialogResult.OK) text.Text = file.FileName; }; return row;
        }
        void SelectPort() {
            batch.Enabled=calibration.Count!=0;
            var p = calibration.Get(SelectedPort); serial.Text = p == null ? "" : p.Serial; gain.Text = p == null ? "" : p.GainPath; adc.Text = p == null ? "" : p.AdcPath;
            apGain.SelectedItem = p == null || p.Np2 ? null : (object)p.ApGain; lfpGain.SelectedItem = p == null || p.Np2 ? null : (object)p.LfpGain;
            order.Text = SelectedPort.ToString(); status.Text = p == null ? "No calibration loaded for this probe." : p.Model + " · serial " + p.Serial + " · host correction loaded";
            FactoryRows(p);
        }
        void FactoryRows(NpAdcCalibration p){bool visible=np2!=true && (p==null || !p.Np2);foreach(int row in new[]{3,4})for(int column=0;column<2;column++)layout.GetControlFromPosition(column,row).Visible=visible;}
        void LoadProfile() {
            if (busy) return;
            var p = new NpAdcCalibration(gain.Text, adc.Text, serial.Text.Trim(), apGain.SelectedItem == null ? 0 : (int)apGain.SelectedItem, lfpGain.SelectedItem == null ? 0 : (int)lfpGain.SelectedItem);
            if (np2.HasValue && p.Np2 != np2.Value) throw new InvalidDataException("Calibration type does not match this device.");
            calibration.Set(SelectedPort, p); NpCalibrationStore.Set(source, calibration);
            batch.Enabled=true;
            FactoryRows(p);
            status.Text = p.Model + " · serial " + p.Serial + " · loaded. " + (p.DigitalAdc ? "ADC register trims are retained, not written to hardware." : "Gain correction only.");
        }
        async Task CorrectDat() {
            if (busy) return;
            string input, output;
            using (var file = new OpenFileDialog { Filter = "Neuropixels DAT|*.dat" }) { if (file.ShowDialog(this) != DialogResult.OK) return; input = file.FileName; }
            using (var file = new SaveFileDialog { Filter = "Corrected DAT|*.dat", FileName = Path.GetFileNameWithoutExtension(input) + ".calibrated.dat" }) { if (file.ShowDialog(this) != DialogResult.OK) return; output = file.FileName; }
            try{await CorrectFile(input,output,order.Text.Split(',').Select(s=>Int32.Parse(s.Trim())).ToArray(),band.SelectedIndex==1,encoding.SelectedIndex==1);}
            catch(Exception ex){status.Text=ex.Message;}
        }
        async Task CorrectFile(string input,string output,int[] ports,bool lfp,bool legacy)
        {
            if(busy)return;var estimate=new NpProgressEstimate();
            try {
                busy = true; cancelled = false; apply.Enabled = correct.Enabled = batch.Enabled = port.Enabled = false; close.Text = "Cancel"; status.Text = "Correcting DAT · "+estimate.Percent(0);
                var snapshot = calibration.Copy();
                var ui=new Progress<Tuple<int,string>>(p=>status.Text=p.Item2);
                if(Directory.Exists(output+".source-metadata") || File.Exists(output+".source-metadata"))throw new IOException("Choose a fresh output and source-metadata destination.");
                await Task.Run(() => {
                    long total=new FileInfo(input).Length;var update=System.Diagnostics.Stopwatch.StartNew();
                    NpCalibrationDat.Correct(input, output, snapshot, ports, lfp, legacy, () => {
                        if(update.ElapsedMilliseconds>=250){update.Restart();var partial=new FileInfo(output+".partial");long done=partial.Exists?Math.Min(total,partial.Length):0;
                            ((IProgress<Tuple<int,string>>)ui).Report(Tuple.Create(total<=0?0:(int)(done*100.0/total),"Correcting DAT: "+(total<=0?0:(int)(done*100.0/total))+"% · "+estimate.Describe(done,total)));}
                        return cancelled;
                    });
                    ((IProgress<Tuple<int,string>>)ui).Report(Tuple.Create(100,"Saving calibration metadata · "+estimate.Percent(100)));
                    NpRecordingSettings.CopySourceMetadata(input,output);
                });
                status.Text = "Corrected DAT saved · "+estimate.Elapsed+": " + output;
            } catch (OperationCanceledException) { status.Text="Calibration cancelled · "+estimate.Elapsed+". Partial output retained.";
            } catch (Exception ex) { status.Text = ex.Message; }
            finally { busy = false; apply.Enabled = correct.Enabled = port.Enabled = true; batch.Enabled=calibration.Count!=0; close.Text = "Done"; }
        }
    }
}
