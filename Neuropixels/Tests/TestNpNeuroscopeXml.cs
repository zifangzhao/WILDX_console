using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using USB512_interface;

static class TestNpNeuroscopeXml
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static string Folder(string root, string name) { string path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
    static byte[] Selection(int port)
    {
        byte[] b = new byte[1576]; b[0]=78; b[1]=80; b[2]=66; b[3]=49; b[4]=2; b[5]=(byte)(port-1);
        for (int ch=0; ch<384; ch++)
        {
            // Deliberately mix banks: electrode depth differs from DAT order.
            int site = ch == 191 ? 65535 : ch + (ch % 2 == 0 ? 384 : 0);
            b[40+4*ch]=(byte)site; b[41+4*ch]=(byte)(site>>8); b[42+4*ch]=(byte)(ch==191?128:0);
        }
        return b;
    }
    static int[] Order(XDocument xml) { return xml.Descendants("anatomicalDescription").Descendants("channel").Select(x=>(int)x).ToArray(); }
    static void Validate(XDocument xml, int count, double rate)
    {
        int[] ids=Order(xml);
        Check((string)xml.Root.Attribute("version")=="1.0", "Neuroscope parameters version");
        Check((int)xml.Root.Element("acquisitionSystem").Element("nBits")==16, "signed int16 container");
        Check((int)xml.Root.Element("acquisitionSystem").Element("nChannels")==count, "DAT channel count");
        Check(Math.Abs((double)xml.Root.Element("acquisitionSystem").Element("samplingRate")-rate)<1e-9, "actual saved sample rate");
        Check(ids.Length==count && ids.Distinct().Count()==count && ids.Min()==0 && ids.Max()==count-1, "all columns exactly once, no shifted or duplicated IDs");
        Check(xml.Root.Element("acquisitionSystem").Element("amplification")==null, "do not fabricate voltage calibration");
    }
    static void Dat(string folder, string name) { File.WriteAllBytes(Path.Combine(folder,name),new byte[]{0,255,16,240,127,128}); }
    static int Main(string[] args)
    {
        try
        {
            CultureInfo.CurrentCulture=new CultureInfo("fr-FR");
            string root=Folder(args[0],"xml-tests");
            var maps=new Dictionary<int,byte[]> {{1,Selection(1)},{3,Selection(3)}};
            string merged=Folder(root,"merged-p1-p3"); Dat(merged,"neuropixels_ap.dat"); Dat(merged,"neuropixels_lfp.dat"); Dat(merged,"analogin.dat"); Dat(merged,"misc.dat");
            byte[] before=File.ReadAllBytes(Path.Combine(merged,"neuropixels_ap.dat"));
            Check(NpNeuroscopeXml.WriteFolder(merged,5,true,20000,15000/12.0,maps).Count==2,"only AP/LFP XML, not analogin or camera");
            var ap=XDocument.Load(Path.Combine(merged,"neuropixels_ap.xml")); Validate(ap,768,20000);
            Validate(XDocument.Load(Path.Combine(merged,"neuropixels_lfp.xml")),768,1250);
            Check((double)ap.Root.Element("fieldPotentials").Element("lfpSamplingRate")==1250,"upsampling AP does not upsample LFP");
            int[] order=Order(ap); Check(order[0]==1 && order[1]==3,"electrode order, not hardware order");
            Check(order[384]==385,"P3 merged block starts at DAT column384, not port3*384");
            Check(ap.Descendants("group").Count()==2,"one anatomical group per probe");
            Check(ap.Descendants("channel").Where(x=>(string)x.Attribute("skip")=="1").Select(x=>(int)x).OrderBy(x=>x).SequenceEqual(new[]{191,575}),"disabled contacts retained as skipped DAT columns");
            Check(before.SequenceEqual(File.ReadAllBytes(Path.Combine(merged,"neuropixels_ap.dat"))),"XML generation never modifies samples");
            NpNeuroscopeXml.WriteFolder(merged,5,true,20000,20000/12.0,maps);
            Validate(XDocument.Load(Path.Combine(merged,"neuropixels_lfp.xml")),768,20000/12.0);
            Check(!Directory.GetFiles(merged,"*.tmp").Any(),"atomic replacement leaves no temporary files");
            for(int port=1;port<=3;port++)
            {
                string single=Folder(root,"single-p"+port); Dat(single,"neuropixels_ap.dat"); Dat(single,"neuropixels_lfp.dat");
                NpNeuroscopeXml.WriteFolder(single,1<<(port-1),false,30000,2500,maps);
                Validate(XDocument.Load(Path.Combine(single,"neuropixels_ap.xml")),384,30000);
                Validate(XDocument.Load(Path.Combine(single,"neuropixels_lfp.xml")),384,2500);
            }
            string sd=Folder(root,"sd-p1-p3");Dat(sd,"np1_ap.dat");Dat(sd,"np3_lfp.dat");Dat(sd,"np2_ap.dat");
            Check(NpNeuroscopeXml.WriteFolder(sd,5,false,7500,625,null).Count==2,"SD prefixes select physical port and reject inactive port");
            Validate(XDocument.Load(Path.Combine(sd,"np3_lfp.xml")),384,625);
            Check(Order(XDocument.Load(Path.Combine(sd,"np1_ap.xml"))).SequenceEqual(Enumerable.Range(0,384)),"unknown historical selection uses explicit hardware fallback");
            string partial=Folder(root,"np2-incomplete");Dat(partial,"neuropixels_probe2_ap.dat.partial");Dat(partial,"neuropixels_probe2_lfp.dat.partial");
            Check(NpNeuroscopeXml.WriteFolder(partial,3,false,30000,null,null).Count==1,"NP2 emits no invented LFP, including partial files");
            var incomplete=XDocument.Load(Path.Combine(partial,"neuropixels_probe2_ap.xml"));Validate(incomplete,384,30000);
            Check(incomplete.DescendantNodes().OfType<XComment>().Any(x=>x.Value.Contains("INCOMPLETE")),"incomplete data marked in XML");
            Check(incomplete.Root.Element("fieldPotentials")==null,"NP2 does not acquire an LFP rate");
            var bad=Selection(1);bad[40]=10;maps[1]=bad;
            NpNeuroscopeXml.WriteFolder(merged,5,true,15000,1250,maps);
            Check(Order(XDocument.Load(Path.Combine(merged,"neuropixels_ap.xml"))).Take(384).SequenceEqual(Enumerable.Range(0,384)),"invalid physical mapping cannot silently reorder geometry");
            string integrated=Folder(root,"settings-integration");Dat(integrated,"neuropixels_ap.dat");Dat(integrated,"neuropixels_lfp.dat");
            var settings=new NpRecordingSettings(integrated,null,"live_usb",false,DateTime.UtcNow,null,null);
            settings.Actual(15000,20000,1,false,false,"offline test");settings.Write();
            Validate(XDocument.Load(Path.Combine(integrated,"neuropixels_ap.xml")),384,20000);
            Validate(XDocument.Load(Path.Combine(integrated,"neuropixels_lfp.xml")),384,1250);
            byte[] confirmed=Selection(1);settings.Request(1,"{}",confirmed);settings.Write();
            Check(Order(XDocument.Load(Path.Combine(integrated,"neuropixels_ap.xml")))[0]==0,"unconfirmed editor request cannot establish geometry");
            settings.ConfirmRequest(1);confirmed[40]=10;settings.Write();
            Check(Order(XDocument.Load(Path.Combine(integrated,"neuropixels_ap.xml")))[0]==1,"matching confirmed request supplies immutable geometry");
            string children=Folder(root,"settings-children");Folder(children,"probe1");Folder(children,"probe3");Dat(Path.Combine(children,"probe1"),"neuropixels_ap.dat");Dat(Path.Combine(children,"probe3"),"neuropixels_lfp.dat");
            settings=new NpRecordingSettings(children,null,"live_usb",false,DateTime.UtcNow,null,null);settings.Actual(20000,20000,5,false,true,"offline test");settings.Write();
            Validate(XDocument.Load(Path.Combine(children,"probe1","neuropixels_ap.xml")),384,20000);
            Validate(XDocument.Load(Path.Combine(children,"probe3","neuropixels_lfp.xml")),384,20000/12.0);
            string raw=Folder(root,"raw-only");NpRecordingSettings.Saved(raw,false,15000,1,4,1,"sd_download",null,false).Write();
            Check(!Directory.GetFiles(raw,"*.xml").Any(),"raw archive does not pretend to be a decoded DAT");
            Console.WriteLine("PASS Neuroscope XML: "+checks+" assertions; fixtures="+root);return 0;
        }
        catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
    }
}
