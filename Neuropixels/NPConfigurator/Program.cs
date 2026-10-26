namespace NPConfigurator;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if(args.Length==4 && args[0]=="--scan-plan") {
                ProbeBankScan.Write(args[1],args[2],int.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture));return 0;
            }
            if(args.Length==3 && args[0]=="--recording-imro") {
                var bytes=StandardFormats.RecordingImro(StandardFormats.Load(args[1]),out string origin);
                File.WriteAllBytes(args[2],bytes);Console.WriteLine(origin);return 0;
            }
            if(args.Length==3 && args[0]=="--snapshot-v1") {
                DeviceSelection.FromPayload(File.ReadAllBytes(args[1])).Save(args[2]);return 0;
            }
            if(args.Length==5 && args[0]=="--prepare-v1") {
                var c=DeviceSelection.LoadForPort(args[1],int.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture));
                var bytes=DeviceSelection.Compile(c);
                c.Save(args[2]);
                using var output=new FileStream(args[3],FileMode.CreateNew,FileAccess.Write);
                output.Write(bytes);return 0;
            }
            if(args.Length==5 && args[0]=="--prepare-v2") {
                if (File.Exists(args[2]) || File.Exists(args[3])) throw new IOException("Prepared output already exists.");
                var c=Np2DeviceSelection.LoadForPort(args[1],int.Parse(args[4],System.Globalization.CultureInfo.InvariantCulture));
                var bytes=Np2DeviceSelection.Compile(c);
                c.Save(args[2]);
                using var output=new FileStream(args[3],FileMode.CreateNew,FileAccess.Write);
                output.Write(bytes);return 0;
            }
            if(args.Length==3 && args[0]=="--device-selection") {
                var bytes=DeviceSelection.Compile(ProbeConfiguration.FromJson(File.ReadAllText(args[1])));
                using var output=new FileStream(args[2],FileMode.CreateNew,FileAccess.Write);
                output.Write(bytes);return 0;
            }
            // File-only bridge for the .NET Framework USB console. No USB or
            // hardware access; cancellation never writes a returned request.
            if (args.Length is 3 or 4 && (args[0] == "--host-edit" || args[0] == "--host-edit-np2"))
            {
                if (File.Exists(args[2])) throw new IOException("Host result already exists.");
                bool np2=args[0]=="--host-edit-np2";
                var initial = args[1] == "-" ? ProbeConfiguration.Create(np2?ProbeProfile.Np2Multi:ProbeProfile.Np1)
                    : ProbeConfiguration.FromJson(File.ReadAllText(args[1]));
                ApplicationConfiguration.Initialize();
                int? connector=args.Length==4?int.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture):null;
                if(connector is <1 or >3)throw new ArgumentException("Connected probe must be 1, 2 or 3.");
                using var editor = new ConfigurationForm(initial, embeddedMode: true, targetConnector:connector);
                editor.Text = "Requested NP configuration (NOT applied to hardware)";
                if (editor.ShowDialog() != DialogResult.OK || !editor.UseConfiguration) return 2;
                if(np2 && editor.Configuration.Profile!=ProbeProfile.Np2Single && editor.Configuration.Profile!=ProbeProfile.Np2Multi)
                    throw new IOException("V2 NP2 requires a single- or four-shank NP2 configuration.");
                editor.Configuration.Save(args[2]);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--validate")
            { StandardFormats.Load(args[1]); return 0; }
            if (args.Length == 3 && args[0] == "--import-imro")
            { StandardFormats.Load(args[1]).Save(args[2]); return 0; }
            if (args.Length is 3 or 4 && args[0] == "--export-imro")
            { ProbeConfiguration.AtomicWrite(args[2], StandardFormats.ToImro(StandardFormats.Load(args[1]), args.Length == 4 ? args[3] : null)); return 0; }
            if (args.Length is 3 or 4 && args[0] == "--export-probeinterface")
            {
                if (args.Length == 4 && args[3] != "--selected-channels") throw new ArgumentException("Expected --selected-channels for a compact channel stream.");
                ProbeConfiguration.AtomicWrite(args[2], StandardFormats.ToProbeInterface(StandardFormats.Load(args[1]), args.Length == 4)); return 0;
            }
            if (args.Length == 3 && args[0] == "--export")
            { ConfigurationCompiler.ExportBundle(ProbeConfiguration.FromJson(File.ReadAllText(args[1])), args[2]); return 0; }
            ApplicationConfiguration.Initialize();
            if (args.Length == 2 && (args[0] == "--render" || args[0] == "--ui-smoke"))
            {
                Directory.CreateDirectory(args[1]);
                foreach (var profile in Enum.GetValues<ProbeProfile>())
                { using var form = new ConfigurationForm(ProbeConfiguration.Create(profile)); if(args[0] == "--ui-smoke") form.RunUiSmoke(); form.RenderPreview(Path.Combine(args[1], profile + ".png")); }
                if (args[0] == "--ui-smoke")
                {
                    using var embeddedForm = new ConfigurationForm(ProbeConfiguration.Create(ProbeProfile.Nric1384), embeddedMode: true);
                    embeddedForm.RunUiSmoke();
                }
                Console.WriteLine("UI checks/render completed for all four profiles.");
                return 0;
            }
            if (args.Length > 1) throw new ArgumentException("Usage: NPConfigurator [configuration.json|table.imro] | --prepare-v2 input normalized.json selection.bin connector | --validate file | --import-imro file.imro output.json | --export-imro input output.imro [type] | --export-probeinterface input output.json [--selected-channels] | --export file directory | --render directory");
            Application.Run(new ConfigurationForm(args.Length == 1 ? StandardFormats.Load(args[0]) : null));
            return 0;
        }
        catch (Exception ex)
        {
            if (args.Length == 0) MessageBox.Show(ex.Message, "NP Configuration", MessageBoxButtons.OK, MessageBoxIcon.Error);
            else Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
