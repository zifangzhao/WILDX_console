using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using USB512_interface;

internal static class NpAdcCalibrationTests
{
    const string Serial = "12345678901234567890";
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static int checks;
    static Assembly app, fixture;
    static void Need(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
    static void Refuse(Action action, string reason) { bool refused = false; try { action(); } catch (Exception error) when (error is IOException || error is ArgumentException || error is InvalidDataException) { refused = true; } Need(refused, reason); }
    static object Call(object target, string method, params object[] args) { try { return target.GetType().GetMethod(method, All).Invoke(target, args); } catch (TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException ?? e).Throw(); throw; } }
    static string Gain(string root, bool np2, int rows = 384) {
        string path = Path.Combine(root, (np2 ? "np2" : "np1") + "_gain_" + rows + ".csv"); var text = new StringBuilder(Serial + "\n");
        for (int ch = 0; ch < rows; ch++) {
            text.Append(ch);
            for (int gain = 0; gain < (np2 ? 1 : 16); gain++) text.Append(',').Append((np2 ? 1.25 : gain < 8 ? 1.0 + gain * 0.25 : 0.5 + (gain - 8) * 0.125).ToString("R", CultureInfo.InvariantCulture));
            text.Append('\n');
        }
        File.WriteAllText(path, text.ToString()); return path;
    }
    static string Adc(string root) {
        string path = Path.Combine(root, "np1_adc.csv"); var text = new StringBuilder(Serial + "\n");
        for (int adc = 0; adc < 32; adc++) text.Append(adc).Append(",16,16,0,0,0,0,").Append(adc % 7 + 1).Append(',').Append(512 + adc % 3).Append('\n');
        File.WriteAllText(path, text.ToString()); return path;
    }
    static void Parser(string root, out NpAdcCalibration p1, out NpAdcCalibration p2) {
        string g1 = Gain(root, false), g2 = Gain(root, true), adc = Adc(root);
        p1 = new NpAdcCalibration(g1, adc, Serial, 500, 250); p2 = new NpAdcCalibration(g2, null, Serial);
        var np2Profile=p2;
        for (int ch = 0; ch < 384; ch++) {
            int physicalAdc = (ch / 24) * 2 + (ch & 1), threshold = 512 + physicalAdc % 3, offset = physicalAdc % 7 + 1;
            foreach (int raw in new[] { 0, threshold - 1, threshold, threshold + 1, 1023 }) {
                int digital = raw > threshold ? raw - offset : raw;
                Need(p1.Correct((short)(512 - raw), ch) == (short)Math.Round((512 - digital) * 1.75, MidpointRounding.AwayFromZero), "NP1 AP threshold/ADC routing/polarity");
                Need(p1.Correct((short)(512 - raw), ch, true) == (short)Math.Round((512 - digital) * 0.75, MidpointRounding.AwayFromZero), "NP1 LFP gain column");
            }
        }
        for (int raw = 0; raw < 4096; raw++) Need(p2.Correct((short)(2048 - raw), raw % 384) == (short)Math.Round((2048 - raw) * 1.25, MidpointRounding.AwayFromZero), "NP2 all codes/polarity/rounding");
        Refuse(() => new NpAdcCalibration(g2, null, "1"), "Wrong serial accepted");
        Refuse(() => new NpAdcCalibration(g2, adc, Serial), "NP1 ADC file accepted for NP2");
        Refuse(() => new NpAdcCalibration(g1, adc, Serial, 80, 250), "Unsupported acquired gain accepted");
        Refuse(() => np2Profile.Correct(0, 0, true), "Invented NP2 LFP");
        Refuse(() => np2Profile.Correct(Int16.MaxValue, 0), "Invalid nominal code accepted");
        foreach (string bad in new[] { "nan", "duplicate", "missing", "extra", "clip", "serial" }) {
            string path = Path.Combine(root, bad + ".csv"), text = File.ReadAllText(g2);
            if (bad == "nan") text = text.Replace("0,1.25", "0,NaN");
            if (bad == "duplicate") text = text.Replace("\n1,1.25", "\n0,1.25");
            if (bad == "missing") text = text.Substring(0, text.LastIndexOf("383,", StringComparison.Ordinal));
            if (bad == "extra") text += "384,1.25\n";
            if (bad == "clip") text = text.Replace("1.25", "100");
            if (bad == "serial") text = text.Replace(Serial, "0");
            File.WriteAllText(path, text); Refuse(() => new NpAdcCalibration(path, null, Serial), "Malformed factory CSV accepted: " + bad);
        }
        string invalidAdc = Path.Combine(root, "bad_adc.csv"); File.WriteAllText(invalidAdc, File.ReadAllText(adc).Replace(",16,16,", ",32,16,"));
        Refuse(() => new NpAdcCalibration(g1, invalidAdc, Serial, 500, 250), "Out-of-range comparator trim accepted");
        var electrodes = new NpAdcCalibration(Gain(root, false, 960), adc, Serial, 500, 250);
        Need(electrodes.Correct(100, 0) == p1.Correct(100, 0), "Uniform 960-electrode file support");
        var culture = CultureInfo.CurrentCulture; try { CultureInfo.CurrentCulture = new CultureInfo("fr-FR"); Need(new NpAdcCalibration(g2, null, Serial).Correct(2, 0) == 3, "CSV decimal parsing depends on culture"); } finally { CultureInfo.CurrentCulture = culture; }
        var otherGain=new NpAdcCalibration(g1,null,Serial,250,3000);
        Need(otherGain.Correct(100,0)==150 && otherGain.Correct(100,0,true)==138,"Selected NP1 gain columns were ignored");
    }
    static void Dat(string root, NpAdcCalibration p1, NpAdcCalibration p2) {
        var set = new NpCalibrationSet(); set.Set(1, p1); set.Set(2, p2);
        var nominal = new short[768 * 3];
        for (int row = 0; row < 3; row++) for (int ch = 0; ch < 384; ch++) { nominal[row * 768 + ch] = (short)(512 - ch); nominal[row * 768 + 384 + ch] = (short)(2048 - ch * 7); }
        byte[] bytes = new byte[nominal.Length * 2]; Buffer.BlockCopy(nominal, 0, bytes, 0, bytes.Length);
        string input = Path.Combine(root, "merged.dat"), output = Path.Combine(root, "merged.corrected.dat"); File.WriteAllBytes(input, bytes);
        NpCalibrationDat.Correct(input, output, set, new[] { 1, 2 }); var corrected = File.ReadAllBytes(output);
        for (int at = 0; at < nominal.Length; at++) Need(BitConverter.ToInt16(corrected, at * 2) == ((at / 384) % 2 == 0 ? p1 : p2).Correct(nominal[at], at % 384), "Merged offline channel/probe order");
        Need(File.ReadAllBytes(input).SequenceEqual(bytes), "Offline input was altered");
        Refuse(() => NpCalibrationDat.Correct(output, output + ".again", set, new[] { 1, 2 }), "Double correction accepted");
        Refuse(() => NpCalibrationDat.Correct(input, input, set, new[] { 1, 2 }), "Input overwrite accepted");
        Refuse(() => NpCalibrationDat.Correct(input, output, set, new[] { 1, 2 }), "Existing output overwritten");
        string bad = Path.Combine(root, "partialrow.dat"); File.WriteAllBytes(bad, new byte[769]); Refuse(() => NpCalibrationDat.Correct(bad, bad + ".out", set, new[] { 1 }), "Partial row accepted");
        string cancel = Path.Combine(root, "cancelled.dat"); bool cancelled = false; try { NpCalibrationDat.Correct(input, cancel, set, new[] { 1, 2 }, false, false, () => true); } catch (OperationCanceledException) { cancelled = true; }
        Need(cancelled && !File.Exists(cancel), "Cancellation promoted a DAT");
        string snapshot = Path.Combine(root, "snapshot"); set.Save(snapshot); var loaded = NpCalibrationSet.Read(snapshot); Need(loaded.Get(1).Serial == Serial && loaded.Get(2).GainHash == p2.GainHash, "Saved CSV/hash/64-bit serial round trip");
        File.AppendAllText(Path.Combine(snapshot, "calibration", "probe2_gain.csv"), "\n"); Refuse(() => NpCalibrationSet.Read(snapshot), "Changed saved calibration accepted");
        string resampled=Path.Combine(root,"resampled");Directory.CreateDirectory(resampled);File.WriteAllBytes(Path.Combine(resampled,"ap.dat"),new byte[768]);
        File.WriteAllText(Path.Combine(resampled,"recording_settings.json"),"{\"output\":{\"ap_upsampled_15_to_20\":true}}");
        Refuse(()=>NpCalibrationDat.Correct(Path.Combine(resampled,"ap.dat"),Path.Combine(root,"resampled_calibrated.dat"),set,new[]{1}),"Post-resampling ADC correction accepted");
        var legacy = new short[384]; for (int ch = 0; ch < 384; ch++) legacy[ch] = (short)(ch * 7); var raw = new byte[768]; Buffer.BlockCopy(legacy, 0, raw, 0, raw.Length);
        string legacyPath = Path.Combine(root, "legacy.dat"); File.WriteAllBytes(legacyPath, raw); NpCalibrationDat.Correct(legacyPath, legacyPath + ".corrected", set, new[] { 2 }, false, true);
        byte[] legacyOut = File.ReadAllBytes(legacyPath + ".corrected"); for (int ch = 0; ch < 384; ch++) Need(BitConverter.ToInt16(legacyOut, ch * 2) == p2.Correct((short)(2048 - legacy[ch]), ch), "Legacy unsigned conversion applied wrong/twice");
    }
    static void ActualRecorder(string root, NpAdcCalibration p2) {
        Type recorderType = app.GetType("USB512_interface.NpV2DatRecorder", true), buffersType = app.GetType("USB512_interface.NpV2BufferPolicy", true), verifierType = app.GetType("USB512_interface.NpV2WireVerifier", true);
        var make = fixture.GetType("NpV2UsbConsoleTests", true).GetMethod("MakeV3Block", All);
        int[] phases=null;
        foreach (bool calibrated in new[] { false, true }) {
            string folder = Path.Combine(root, calibrated ? "online_corrected" : "online_nominal"); Directory.CreateDirectory(folder);
            var buffers = Activator.CreateInstance(buffersType, All, null, new object[] { 64 }, null); long previewRows = 0;
            var previewBytes=new MemoryStream[4];for(int p=1;p<=3;p++)previewBytes[p]=new MemoryStream();
            Action<int, short[]> preview = (probe, row) => {var bytes=new byte[768];Buffer.BlockCopy(row,0,bytes,0,bytes.Length);previewBytes[probe].Write(bytes,0,bytes.Length);System.Threading.Interlocked.Increment(ref previewRows);};
            Action<int, short[]> correction = calibrated ? (Action<int, short[]>)((probe, row) => { if (probe == 1) p2.CorrectBand(row, 1); }) : null;
            var recorder = Activator.CreateInstance(recorderType, All, null, new object[] { folder, preview, buffers, correction }, null);
            var verifier = Activator.CreateInstance(verifierType, true);
            try {
                for (ulong seq = 0; seq < 8; seq++) {
                    var block = (byte[])make.Invoke(null, new object[] { seq, seq * 61440, 3U, 3U, 20000U, true, UInt32.MaxValue });
                    Call(verifier, "Feed", block, 0); Call(recorder, "Enqueue", block, 0, verifier);
                }
                Call(recorder, "Complete");
                if(calibrated)phases=new[]{(int)Call(verifier,"PhaseForProbe",0),(int)Call(verifier,"PhaseForProbe",1),(int)Call(verifier,"PhaseForProbe",2)};
                Need((string)recorderType.GetProperty("DecodeError", All).GetValue(recorder, null) == null && previewRows > 0, "Actual recorder failed to decode or preview");
                for(int p=1;p<=2;p++)Need(File.ReadAllBytes(Path.Combine(folder,"probe"+p,"neuropixels_ap.dat")).SequenceEqual(previewBytes[p].ToArray()),"Online preview differs from calibrated DAT");
            } finally { ((IDisposable)recorder).Dispose(); }
        }
        string a = Path.Combine(root, "online_nominal"), b = Path.Combine(root, "online_corrected");
        Need(File.ReadAllBytes(Path.Combine(a, "neuropixels_np2usb.raw")).SequenceEqual(File.ReadAllBytes(Path.Combine(b, "neuropixels_np2usb.raw"))), "Calibration altered native RAW bytes");
        for (int probe = 1; probe <= 2; probe++) {
            string file = Path.Combine("probe" + probe, "neuropixels_ap.dat"); byte[] nominal = File.ReadAllBytes(Path.Combine(a, file)), calibrated = File.ReadAllBytes(Path.Combine(b, file));
            Need(nominal.Length == calibrated.Length && nominal.Length > 0, "Actual online row count changed");
            for (int i = 0; i < nominal.Length / 2; i++) Need(BitConverter.ToInt16(calibrated, i * 2) == (probe == 1 ? p2.Correct(BitConverter.ToInt16(nominal, i * 2), i % 384) : BitConverter.ToInt16(nominal, i * 2)), "Actual online DAT correction/probe scope");
        }
        var set=new NpCalibrationSet();set.Set(1,p2);set.Save(b);
        var merge=app.GetType("USB512_interface.NpV2DatMerge",true).GetMethod("Run",All);
        long rows=(long)merge.Invoke(null,new object[]{b,3U,20000U,phases,true,(Func<bool>)(()=>false)});
        byte[] merged=File.ReadAllBytes(Path.Combine(b,"neuropixels_ap.dat"));
        Need(merged.Length==rows*1536,"Calibrated merge extent changed");
        for(int probe=1;probe<=2;probe++){
            byte[] original=File.ReadAllBytes(Path.Combine(b,"probe"+probe,"neuropixels_ap.dat"));
            for(int row=0;row<rows;row++)for(int ch=0;ch<384;ch++)Need(BitConverter.ToInt16(merged,(row*768+(probe-1)*384+ch)*2)==BitConverter.ToInt16(original,(row*384+ch)*2),"Merge reapplied calibration or changed corrected rows");
        }
        Need(File.ReadAllText(Path.Combine(b,"np2_merge_status.txt")).Contains("sample_encoding=signed_calibrated_nominal_adc_counts"),"Calibrated merge claimed nominal encoding");
        set.Set(2,p2);
        Refuse(()=>NpCalibrationDat.Correct(Path.Combine(b,"neuropixels_ap.dat"),Path.Combine(root,"merged_again.dat"),set,new[]{1,2}),"Calibrated merge allowed double correction");
    }
    static void ActualNp1(string root,NpAdcCalibration profile) {
        string folder=Path.Combine(root,"np1_writer");Directory.CreateDirectory(folder);
        Type type=app.GetType("USB512_interface.NpProbeRecording",true),layout=app.GetType("USB512_interface.XeNpLaneLayout",true);
        var recorder=Activator.CreateInstance(type,All,null,new object[]{folder,1,Enum.Parse(layout,"Gpio")},null);
        Action<int,short[],int,short[],int> correction=(probe,a,na,l,nl)=>{profile.CorrectBand(a,na);profile.CorrectBand(l,nl,true);};
        type.GetField("SampleCorrection",All).SetValue(recorder,correction);
        var ap=new short[384];var lfp=new short[384];for(int ch=0;ch<384;ch++){ap[ch]=(short)(512-ch*2);lfp[ch]=(short)(512-ch);}
        var originalAp=(short[])ap.Clone();var originalLfp=(short[])lfp.Clone();
        try{Call(recorder,"OnSamples",ap,1,lfp,1);}finally{((IDisposable)recorder).Dispose();}
        byte[] apBytes=File.ReadAllBytes(Path.Combine(folder,"neuropixels_ap.dat")),lfpBytes=File.ReadAllBytes(Path.Combine(folder,"neuropixels_lfp.dat"));
        for(int ch=0;ch<384;ch++)Need(BitConverter.ToInt16(apBytes,ch*2)==profile.Correct(originalAp[ch],ch) && BitConverter.ToInt16(lfpBytes,ch*2)==profile.Correct(originalLfp[ch],ch,true),"Actual NP1 writer bypassed correction");
    }
    static void BlePlot(NpAdcCalibration profile) {
        var plotType=app.GetType("USB512_interface.NpBlePreviewForm",true).GetNestedType("Plot",All);
        using(var plot=(Control)Activator.CreateInstance(plotType,true)) {
            Call(plot,"Clear",7);plotType.GetField("CorrectSample",All).SetValue(plot,(Func<int,short,short>)((ch,value)=>profile.Correct(value,ch)));
            var raw=new ushort[16,3];for(int row=0;row<16;row++)for(int lane=0;lane<3;lane++)raw[row,lane]=(ushort)(1000+row*20+lane*100);
            Call(plot,"Append",raw,0UL,64000UL,65535U);var values=(short[,])plotType.GetField("values",All).GetValue(plot);
            for(int row=0;row<16;row++)for(int lane=0;lane<3;lane++)Need(values[row,lane]==profile.Correct((short)(2048-raw[row,lane]),7+lane*128),"Actual BLE plot correction/polarity/channel routing");
        }
    }
    static void ActualSd(string root, NpAdcCalibration p2) {
        string image = Path.Combine(root, "v12.img"), a = Path.Combine(root, "sd_nominal"), b = Path.Combine(root, "sd_calibrated");
        var makeImage=fixture.GetType("NpV2SdDatExportTests",true).GetMethod("MakeImage",All);
        var arguments=makeImage.GetParameters().Select(p=>p.DefaultValue).ToArray();arguments[0]=image;arguments[1]=20000U;arguments[2]="";arguments[3]=86016U;
        makeImage.Invoke(null,arguments);
        using (var card = File.OpenRead(image)) NpV2SdDatExport.Export(card, 8192, a);
        var set = new NpCalibrationSet(); set.Set(1, p2); using (var card = File.OpenRead(image)) NpV2SdDatExport.ExportCalibrated(card, 8192, b, set);
        string empty=Path.Combine(root,"sd_empty_selection"); using(var card=File.OpenRead(image))NpV2SdDatExport.ExportCalibrated(card,8192,empty,new NpCalibrationSet());
        for (int probe = 1; probe <= 2; probe++) {
            string name = "neuropixels_probe" + probe + "_ap.dat"; byte[] nominal = File.ReadAllBytes(Path.Combine(a, name)), calibrated = File.ReadAllBytes(Path.Combine(b, name));
            Need(nominal.Length == calibrated.Length, "SD row count changed");
            Need(nominal.SequenceEqual(File.ReadAllBytes(Path.Combine(empty,name))),"Empty calibration selection changed default DAT");
            for (int i = 0; i < nominal.Length / 2; i++) Need(BitConverter.ToInt16(calibrated, i * 2) == (probe == 1 ? p2.Correct(BitConverter.ToInt16(nominal, i * 2), i % 384) : BitConverter.ToInt16(nominal, i * 2)), "Actual SD calibration differs from online formula");
        }
        foreach (string name in new[] { "descriptor.bin", "footer.bin", "utc.bin" }) Need(File.ReadAllBytes(Path.Combine(a, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(b, name))), "Calibration altered native metadata");
        Need(File.ReadAllText(Path.Combine(a,"np2_sd_export_status.txt"))==File.ReadAllText(Path.Combine(empty,"np2_sd_export_status.txt")) && !File.Exists(Path.Combine(empty,NpCalibrationSet.ManifestName)),"Empty selection changed default provenance");
        Need(File.ReadAllText(Path.Combine(b,"np2_sd_export_status.txt")).Contains("sample_encoding=signed_calibrated_nominal_adc_counts"),"Calibrated SD encoding not recorded");
    }
    static void Ui(string root) {
        Application.EnableVisualStyles(); Type formType = app.GetType("USB512_interface.NpCalibrationForm", true);
        using (var form = (Form)Activator.CreateInstance(formType, All, null, new object[] { "offline-test", null }, null)) {
            form.StartPosition = FormStartPosition.Manual; form.Location = new System.Drawing.Point(-30000, -30000); form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size)); bitmap.Save(Path.Combine(root, "calibration.png")); }
            Need(form.Controls.Count != 0, "Calibration dialog missing controls");
            foreach(string name in new[]{"gain","adc"})Need(((Control)formType.GetField(name,All).GetValue(form)).Width>300,"Calibration file input does not fill its row");
            form.Close();
        }
    }
    [STAThread] static int Main(string[] args) {
        try {
            app = Assembly.LoadFrom(Path.GetFullPath(args[0])); fixture = Assembly.LoadFrom(Path.GetFullPath(args[1]));
            string root = Path.Combine(Path.GetFullPath(args[2]), "run_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            NpAdcCalibration p1, p2; Parser(root, out p1, out p2); Dat(root, p1, p2); ActualRecorder(root, p2); ActualNp1(root,p1); ActualSd(root, p2); BlePlot(p2); Ui(root);
            Console.WriteLine("PASS {0} factory ADC/gain checks: actual online recorder/RAW, strict v12 SD export, offline DAT, serial/hash/CSV/threshold/rounding/probe order/legacy/cancel/double-correction and offscreen UI; no hardware.", checks); Console.WriteLine("Evidence: " + root); return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
