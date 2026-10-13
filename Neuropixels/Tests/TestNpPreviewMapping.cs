using System;
using System.Collections.Generic;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Diagnostics;
using System.Web.Script.Serialization;
using USB512_interface;

internal static class NpPreviewMappingTests
{
    static int checks;
    const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static byte[] Selection(int probe)
    {
        var b=new byte[1576];b[0]=78;b[1]=80;b[2]=66;b[3]=49;b[4]=2;b[5]=(byte)(probe-1);
        for(int i=0;i<32;i++)b[8+i]=(byte)(probe*11+i);
        for(int ch=0;ch<384;ch++) {
            int site=ch+(ch%2==0 && ch<192?768:ch%2==0?384:0),at=40+ch*4;
            b[at]=(byte)site;b[at+1]=(byte)(site>>8);
            if(ch==191 || ch%(probe+3)==0)b[at+2]=128;
        }
        return b;
    }
    static short[] Rows(int rows,int offset)
    {
        var values=new short[rows*384];
        for(int row=0;row<rows;row++)for(int ch=0;ch<384;ch++)values[row*384+ch]=(short)(offset+ch-row*3);
        return values;
    }
    static byte[] BankZeroSelection(int probe)
    {
        var selection=Selection(probe);
        for(int ch=0;ch<384;ch++){
            int at=40+ch*4;selection[at]=(byte)ch;selection[at+1]=(byte)(ch>>8);selection[at+2]=(byte)(ch==191?128:0);
        }
        return selection;
    }
    static List<int> ExpectedSites(byte[] selection)
    {
        // Independent physical scan: highest row first, left contact then right.
        // This deliberately differs from a numeric descending-site sort.
        var selected=new bool[960];
        for(int ch=0;ch<384;ch++){int at=40+ch*4;if((selection[at+2]&128)==0)selected[selection[at]|selection[at+1]<<8]=true;}
        var order=new List<int>();
        for(int row=479;row>=0;row--)for(int side=0;side<2;side++){int site=row*2+side;if(selected[site])order.Add(site);}
        return order;
    }
    static void Colors(NpPreviewDefinition definition,NpPreviewPage page,int pageIndex,int probe)
    {
        var palettes=NpPreviewPalette.CreateAll();var palette=palettes[1];
        try{
            const int mapHeight=10000;
            using(var map=new Bitmap(136,mapHeight)){
                using(var g=Graphics.FromImage(map))NeuropixelsPreviewForm.DrawElectrodeMap(g,definition,page,palette,map.Width,map.Height);
                for(int i=0;i<page.Count;i++){
                    int site=page.Sites[i].Site,row=site/2;
                    int x=(int)Math.Round(40+(site%2*32+(row%2==0?16:0))*1.1);
                    int y=(int)Math.Round(mapHeight-63-row*20*(mapHeight-63-70)/9580.0);
                    Check(map.GetPixel(x,y).ToArgb()==palette.Brushes[i].Color.ToArgb(),"Map color did not follow reordered site on page "+pageIndex);
                }
            }
            var samples=new short[8*384];for(int row=0;row<8;row++)for(int ch=0;ch<384;ch++)samples[row*384+ch]=(short)ch;
            var original=(short[])samples.Clone();var references=new float[64];for(int i=0;i<page.Count;i++)references[i]=page.Channels[i];
            foreach(bool lfp in new[]{false,true}){
                var queue=new NpPreviewQueue(8);queue.Select(lfp,pageIndex,probe,page.Channels);
                var sweep=new NpSweepBuffer(8);queue.Push(samples,8,samples,8,probe);queue.DrainTo(sweep,queue.Generation);
                using(var trace=new Bitmap(500,2560)){
                    using(var g=Graphics.FromImage(trace))NeuropixelsPreviewForm.DrawSweepBank(g,sweep,0,1,0,512,references,trace.Width,trace.Height,null,page,palette);
                    // Sample beyond the deliberately blank sweep boundary.
                    for(int i=0;i<page.Count;i++)Check(trace.GetPixel(350,i*40+20).ToArgb()==palette.Brushes[i].Color.ToArgb(),"AP/LFP trace color did not match reordered map on page "+pageIndex);
                }
            }
            for(int i=0;i<samples.Length;i++)Check(samples[i]==original[i],"Rendering changed source channel buffers");
        }finally{foreach(var item in palettes)item.Dispose();}
    }
    static void ShankAndTieOrder()
    {
        // Constructor-only ordering fixture; no NP2 routing is inferred.
        var sites=new List<NpPreviewSite>();var expected=new List<int>();
        for(int shank=1;shank>=0;shank--)for(int site=0;site<80;site++)sites.Add(new NpPreviewSite(shank*80+site,site,shank));
        sites.Add(new NpPreviewSite(350,79,0));
        for(int shank=0;shank<2;shank++)for(int row=39;row>=0;row--)for(int side=0;side<2;side++){
            expected.Add(shank*80+row*2+side);if(shank==0 && row==39 && side==1)expected.Add(350);
        }
        var definition=(NpPreviewDefinition)Activator.CreateInstance(typeof(NpPreviewDefinition),Private,null,new object[]{sites,true,"ordering-test"},null);
        int at=0;
        foreach(var page in definition.Pages){
            Check(page.Count==Math.Min(64,expected.Count-at),"Multi-shank partial page count changed");
            for(int i=0;i<page.Count;i++)Check(page.Channels[i]==expected[at++],"Shanks, row contacts, ties or page boundary out of physical order");
        }
        Check(at==expected.Count,"Multi-shank fixture lost sites");
    }
    static void MappingAndQueue()
    {
        var ap=Rows(2,-200);var lfp=Rows(1,100);var apOriginal=(short[])ap.Clone();var lfpOriginal=(short[])lfp.Clone();
        bool noncontiguous=false,crossBank=false;
        foreach(bool bankZero in new[]{false,true})for(int probe=1;probe<=3;probe++) {
            byte[] requested=bankZero?BankZeroSelection(probe):Selection(probe);var definition=NpPreviewDefinition.FromSelection(requested);
            var expectedSites=ExpectedSites(requested);
            Check(definition!=null && definition.Physical,"NP1 cached definition not accepted");
            if(bankZero)Check(definition.Pages[0].Sites[0].Site==382 && definition.Pages[0].Sites[1].Site==383,"Equal-Y contacts must remain left-to-right, not whole-site descending");
            Check(definition.Pages.Length==(expectedSites.Count+63)/64,"Physical page count changed");
            int count=0;
            for(int pageIndex=0;pageIndex<definition.Pages.Length;pageIndex++) {
                var page=definition.Pages[pageIndex];
                Check(page.Count==Math.Min(64,expectedSites.Count-pageIndex*64),"Full/partial page extent changed");
                for(int i=0;i<page.Count;i++) {
                    var site=page.Sites[i];int at=40+site.Channel*4;
                    Check(site.Site==expectedSites[count++],"Exact top-to-bottom/left-to-right order changed across page "+pageIndex);
                    Check(site.Channel==site.Site%384 && page.Channels[i]==site.Channel,"Site/hardware routing changed");
                    Check((requested[at]|requested[at+1]<<8)==site.Site && (requested[at+2]&128)==0,"Disabled or different requested site displayed");
                    Check(site.Label.Contains("E"+site.Site) && site.Label.Contains("C"+site.Channel),"Site/hardware labels missing");
                    Check(site.XUm==site.Site%2*32+(site.Site/2%2==0?16:0) && site.YUm==site.Site/2*20,"Configurator NP1 geometry differs");
                    if(i>0){noncontiguous|=page.Channels[i]!=page.Channels[i-1]+1;crossBank|=page.Channels[i]/64!=page.Channels[i-1]/64;}
                }
                for(int i=page.Count;i<64;i++)Check(page.Channels[i]==-1 && page.Sites[i]==null,"Partial page invented a contact");
                Colors(definition,page,pageIndex,probe);
                foreach(bool band in new[]{false,true}) {
                    var queue=new NpPreviewQueue(8);queue.Select(band,pageIndex,probe,page.Channels);
                    int generation=queue.Generation;
                    var sweep=new NpSweepBuffer(8);
                    queue.Push(ap,2,lfp,1,probe);queue.DrainTo(sweep,generation);
                    Check(sweep.CopiedRows==(band?1:2),"Mapped AP/LFP row count changed");
                    for(int row=0;row<(band?1:2);row++)for(int i=0;i<64;i++)
                        Check(sweep.Samples[row*64+i]==(i<page.Count?(band?lfp:ap)[row*384+page.Channels[i]]:0),"Gather copied wrong channel/band or unfilled slot");
                    int other=probe==3?1:probe+1;queue.Push(ap,2,lfp,1,other);
                    Check(!queue.DrainTo(sweep,generation) && queue.Written(false,other)==2,"Unselected probe entered display");
                    queue.InvalidateView();int next=queue.Generation;queue.Push(ap,2,lfp,1,probe);
                    sweep.Reset(8);queue.DrainTo(sweep,next);
                    Check(sweep.CopiedRows>0,"Invalidation lost mapped selection");
                }
            }
            Check(count==expectedSites.Count,"Configured active electrode count differs");
            // Parsed pages own their labels/indices: later payload mutation cannot reroute a running view.
            int first=definition.Pages[0].Channels[0];Array.Clear(requested,0,requested.Length);
            Check(definition.Pages[0].Channels[0]==first,"Definition retained mutable payload routing");
        }
        Check(noncontiguous && crossBank,"Fixture did not exercise noncontiguous/cross-bank sites");
        ShankAndTieOrder();
        for(int i=0;i<ap.Length;i++)Check(ap[i]==apOriginal[i],"Preview changed AP recording input");
        for(int i=0;i<lfp.Length;i++)Check(lfp[i]==lfpOriginal[i],"Preview changed LFP recording input");
        for(int page=0;page<6;page++)for(int i=0;i<64;i++)
            Check(NpPreviewDefinition.Hardware.Pages[page].Channels[i]==page*64+i,"Fallback hardware order changed");
        var invalid=Selection(1);invalid[40]=2;invalid[41]=0;invalid[42]=0;
        Check(NpPreviewDefinition.FromSelection(invalid)==null,"Invalid channel/site mapping accepted");
        invalid=Selection(1);invalid[4]=1;Check(NpPreviewDefinition.FromSelection(invalid)==null,"NRIC geometry invented");
        Check(NpPreviewDefinition.FromSelection(null)==null,"Missing mapping was invented");
        var empty=Selection(1);for(int ch=0;ch<384;ch++)empty[42+ch*4]|=128;
        var emptyPage=NpPreviewDefinition.FromSelection(empty).Pages[0];
        Check(emptyPage.Count==0,"Disabled definition should show no active sites");
        var emptyQueue=new NpPreviewQueue(8);emptyQueue.Select(false,0,1,emptyPage.Channels);
        var emptySweep=new NpSweepBuffer(8);emptyQueue.Push(ap,2,lfp,1);emptyQueue.DrainTo(emptySweep,emptyQueue.Generation);
        for(int i=0;i<128;i++)Check(emptySweep.Samples[i]==0,"All-disabled page exposed hardware values");
    }
    static object Field(object value,string name){return value.GetType().GetField(name,Private).GetValue(value);}
    static NpPreviewDefinition[] Np2Mapping(string output,string helper,string catalogPath,string sourcePath)
    {
        var serializer=new JavaScriptSerializer {MaxJsonLength=1024*1024};
        var catalog=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(catalogPath));
        var routing=(Dictionary<string,object>)catalog["Np2Mapping"];
        var definitions=new NpPreviewDefinition[4];
        string fixtures=Path.Combine(output,"np2-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixtures);
        for(int probe=1;probe<=3;probe++) {
            var channels=new List<object>();
            for(int ch=383;ch>=0;ch--) { // Deliberately unordered JSON; hardware channels are not site numbers.
                int shank=probe==1?0:probe==2?(ch/96+1)%4:3;
                var map=(IList)routing["Multi-shank Shank "+shank];int start=probe==1?0:probe==2?384:896,site=-1;
                for(int i=start;i<1280;i++)if((int)map[i]==ch){site=i;break;}
                if(site<0)for(int i=0;i<start;i++)if((int)map[i]==ch){site=i;break;}
                channels.Add(new {Channel=ch,Site=site,Shank=shank,Standby=false,Export=true,Reference="Ground",ApGain=100,LfpGain=100,HighPass=false});
            }
            string input=Path.Combine(fixtures,"p"+probe+".json"),normalized=input+".normalized.json",payload=input+".n2b1",geometry=input+".geometry.json";
            File.WriteAllText(input,serializer.Serialize(new {SchemaVersion=1,Numbering="zero-based",Profile="Np2Multi",Connector=probe,ModelRevision="2013",SerialNumber="",MappingId="imec-np2-sha256:"+catalog["Np2Sha256"],InternalReferenceBank=0,CalibrationIdentity="",HardwareState="NotApplied",Channels=channels}));
            foreach(string command in new[]{"--prepare-v2 \""+input+"\" \""+normalized+"\" \""+payload+"\" "+probe,"--export-probeinterface \""+normalized+"\" \""+geometry+"\""}) {
                using(var process=Process.Start(new ProcessStartInfo(helper,command){UseShellExecute=false,RedirectStandardError=true,CreateNoWindow=true})) {
                    string error=process.StandardError.ReadToEnd();process.WaitForExit();Check(process.ExitCode==0,"Shared NP2013 compiler/geometry fixture failed: "+error);
                }
            }
            byte[] bytes=File.ReadAllBytes(payload);string json=File.ReadAllText(normalized);
            var definition=NpPreviewDefinition.FromNp2Request(json,bytes);definitions[probe]=definition;
            Check(definition!=null && definition.Physical && definition.Np2,"NP2 compiled map was not accepted");
            var doc=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(geometry));
            var oracle=(Dictionary<string,object>)((IList)doc["probes"])[0];
            var indices=(IList)oracle["device_channel_indices"];var ids=(IList)oracle["contact_ids"];var positions=(IList)oracle["contact_positions"];
            int count=0,lastShank=-1;float lastY=0,lastX=0;
            var ap=Rows(3,0);var original=(short[])ap.Clone();
            for(int p=0;p<definition.Pages.Length;p++) {
                var page=definition.Pages[p];var queue=new NpPreviewQueue(8);queue.Select(false,p,probe,page.Channels);
                var sweep=new NpSweepBuffer(8);queue.Push(ap,3,null,0,probe);queue.DrainTo(sweep,queue.Generation);
                for(int i=0;i<page.Count;i++) {
                    var site=page.Sites[i];int at=indices.IndexOf(site.Channel);var xy=(IList)positions[at];
                    Check((string)ids[at]=="s"+site.Shank+"e"+site.Site,"NP2 site/channel differs from shared geometry export");
                    Check(Convert.ToSingle(xy[0])==site.XUm && Convert.ToSingle(xy[1])==site.YUm,"NP2013 geometry must be aligned 32 um columns / 15 um rows");
                    Check(site.Shank>lastShank || site.Shank==lastShank && (site.YUm<lastY || site.YUm==lastY && site.XUm>lastX),"NP2 shank/row/column display order changed");
                    for(int row=0;row<3;row++)Check(sweep.Samples[row*64+i]==ap[row*384+site.Channel],"NP2 preview gathered a different hardware channel");
                    lastShank=site.Shank;lastY=site.YUm;lastX=site.XUm;count++;
                }
                queue.Push(ap,3,null,0,probe==3?1:probe+1);Check(!queue.DrainTo(sweep,queue.Generation),"Different NP2 connector entered preview");
            }
            Check(count==384,"NP2 map lost channels");
            for(int i=0;i<ap.Length;i++)Check(ap[i]==original[i],"NP2 display changed recording input");
            byte saved=bytes[5];bytes[5]=(byte)((saved+1)%3);Check(NpPreviewDefinition.FromNp2Request(json,bytes)==null,"NP2 map accepted a different physical connector");bytes[5]=saved;
            byte[] missing=(byte[])bytes.Clone();Array.Clear(missing,40,644);Check(NpPreviewDefinition.FromNp2Request(json,missing)==null,"JSON site not present in N2B1 accepted");
            int first=definition.Pages[0].Channels[0];Array.Clear(bytes,0,bytes.Length);Check(definition.Pages[0].Channels[0]==first,"NP2 map retained mutable payload");
        }
        using(var form=new NeuropixelsPreviewForm(20000,7,true,12)) {
            form.PrepareForCapture();form.PrepareProbeDefinitions(definitions);
            Check(ReferenceEquals(Field(Field(form,"view"),"Definition"),NpPreviewDefinition.Hardware),"Unapplied NP2 request mislabeled as applied");
            form.ConfirmProbeDefinitions();
            for(int probe=1;probe<=3;probe++) {
                ((ComboBox)Field(form,"probeBox")).SelectedItem=probe;
                Check(ReferenceEquals(Field(Field(form,"view"),"Definition"),definitions[probe]),"Probe selector did not follow its own NP2 map");
                Check(((Label)Field(form,"report")).Text.Contains("applied NP2 IMRO"),"NP2 receipt provenance is not visible");
                typeof(NeuropixelsPreviewForm).GetMethod("RenderFrame",Private).Invoke(form,null);
                using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(output,"np2-applied-p"+probe+"-preview.png"),ImageFormat.Png);}
            }
            form.PrepareForCapture();Check(ReferenceEquals(Field(Field(form,"view"),"Definition"),NpPreviewDefinition.Hardware),"New recording leaked previous applied NP2 map");
            form.KeepAliveWhileRecording=false;form.Close();
        }
        string source=File.ReadAllText(sourcePath);
        int parse=source.IndexOf("NpPreviewDefinition.FromNp2Request",StringComparison.Ordinal),prepare=source.IndexOf("neuropixelsPreview.PrepareProbeDefinitions(previewDefinitions)",StringComparison.Ordinal);
        int run=source.IndexOf("NpV2RecordingResult result = NpV2Recording.RunContinuousInFolder",StringComparison.Ordinal),confirm=source.IndexOf("Np2ConfigurationUsb.ConfirmApplied",StringComparison.Ordinal),display=source.IndexOf("neuropixelsPreview.ConfirmProbeDefinitions",StringComparison.Ordinal);
        Check(parse>=0 && parse<prepare && prepare<run && run<confirm && confirm<display,"NP2 UI exposed mapping before preparation/matching applied receipt");
        Check(NpPreviewDefinition.FromNp2Request(null,null)==null,"Missing NP2 mapping invented");
        return definitions;
    }
    static void AcceptedMaskShrink(string mainPath)
    {
        var definitions=new NpPreviewDefinition[4];for(int p=1;p<=3;p++)definitions[p]=NpPreviewDefinition.FromSelection(Selection(p));
        using(var form=new NeuropixelsPreviewForm(15000,2)) {
            form.PrepareForCapture();form.PrepareProbeDefinitions(definitions);form.ConfirmProbeDefinitions();
            Check((int)((ComboBox)Field(form,"probeBox")).SelectedItem==2,"Accepted P2-only selector lost physical port");
            Check(ReferenceEquals(definitions[2],Field(Field(form,"view"),"Definition")),"Accepted mask shrink lost cached P2 definition");
            form.KeepAliveWhileRecording=false;form.Close();
        }
        string source=File.ReadAllText(mainPath);
        string call="neuropixelsPreview.PrepareProbeDefinitions(previewDefinitions);";
        int first=source.IndexOf(call,StringComparison.Ordinal),second=source.IndexOf(call,first+call.Length,StringComparison.Ordinal);
        Check(first>=0 && second>source.IndexOf("if(admitted.ActiveMask!=modeCaps.ActiveMask)",StringComparison.Ordinal),"Cached definitions not reused in both preview preparation branches");
        Check(source.IndexOf("NpPreviewDefinition.FromSelection",StringComparison.Ordinal)<source.IndexOf("profile=device.Start(XeNpMcoDivider)",StringComparison.Ordinal),"Site mapping parsed after START");
        int applied=source.IndexOf("NpConfigurationUsb.ConfirmApplied",StringComparison.Ordinal),display=source.IndexOf("neuropixelsPreview.ConfirmProbeDefinitions",StringComparison.Ordinal),release=source.IndexOf("device.CompleteStartupAdmission()",StringComparison.Ordinal);
        Check(applied>=0 && applied<display && display<release,"Display mapping exposed before existing applied receipt or after capture release");
    }
    static void Render(string output,bool np2,bool bankZero=false)
    {
        using(var form=new NeuropixelsPreviewForm(15000,np2?1:7,np2,np2?12:10)) {
            Check(form.Icon!=null && form.Icon.Size==new Size(32,32),"Preview window icon missing");
            form.PrepareForCapture();
            var definitions=new NpPreviewDefinition[4];for(int p=1;p<=3;p++)definitions[p]=NpPreviewDefinition.FromSelection(bankZero?BankZeroSelection(p):Selection(p));
            if(!np2) {
                form.PrepareProbeDefinitions(definitions);
                Check((NpPreviewDefinition)Field(Field(form,"view"),"Definition")==NpPreviewDefinition.Hardware,"Unconfirmed request exposed as applied mapping");
                form.ConfirmProbeDefinitions();
                ((ComboBox)Field(form,"probeBox")).SelectedIndex=1;
                ((NumericUpDown)Field(form,"bankBox")).Value=bankZero?0:1;
            }
            ((NumericUpDown)Field(form,"durationBox")).Value=0.10M;
            ((ComboBox)Field(form,"paletteBox")).SelectedIndex=np2?2:1;
            var queue=(NpPreviewQueue)Field(form,"previewQueue");
            var samples=new short[2048*384];
            for(int row=0;row<2048;row++)for(int ch=0;ch<384;ch++)
                samples[row*384+ch]=(short)(24*Math.Sin(row*0.12+ch*.2)+((row+ch*7)%173==0?50:0));
            queue.Push(samples,2048,null,0,np2?1:2);
            typeof(NeuropixelsPreviewForm).GetMethod("RenderFrame",Private).Invoke(form,null);
            using(var image=new Bitmap(form.Width,form.Height)) {
                form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));
                // Native edit/combo text is omitted by WM_PRINT on a hidden
                // form. Overlay its actual selected values for offline QA.
                using(Graphics g=Graphics.FromImage(image)) {
                    Point origin=form.PointToScreen(Point.Empty)-new Size(form.Location);
                    foreach(string name in new[]{"bandBox","probeBox","paletteBox","bankBox","durationBox","gainBox"}) {
                        Control control=(Control)Field(form,name);
                        string text=control is NumericUpDown?((NumericUpDown)control).Value.ToString():control.Text;
                        if(control is ComboBox) {
                            var box=(ComboBox)control;
                            text=name=="probeBox"?"P"+box.SelectedItem:Convert.ToString(box.SelectedItem);
                        }
                        g.DrawString(text,form.Font,Brushes.Black,origin.X+control.Left+3,origin.Y+control.Top+4);
                    }
                }
                image.Save(Path.Combine(output,np2?"np2-hardware-fallback.png":bankZero?"np1-bank0-topdown.png":"np1-electrode-preview.png"),ImageFormat.Png);
                Check(image.Width>1000 && image.Height>700,"Offline preview image is incomplete");
            }
            var view=Field(form,"view");var definition=(NpPreviewDefinition)Field(view,"Definition");
            Check(definition.Physical!=np2,"NP2 must retain explicit unverified hardware fallback");
            var paletteList=(NpPreviewPalette[])Field(form,"palettes");var palette=paletteList[np2?2:1];
            for(int i=0;i<64;i++)Check(palette.Pens[i].Color==palette.Brushes[i].Color,"Trace/map palette differs");
            var beforeMap=(Bitmap)Field(form,"mapBuffer");
            queue.AdvanceClock(1,0,np2?1:2);
            typeof(NeuropixelsPreviewForm).GetMethod("RenderFrame",Private).Invoke(form,null);
            Check(ReferenceEquals(beforeMap,Field(form,"mapBuffer")),"Static map rebuilt for sample-only frame");
            int sameGeneration=queue.Generation;var sameSweep=(NpSweepBuffer)Field(form,"sweep");
            long sameWritten=sameSweep.Written,sameCopied=sameSweep.CopiedRows;
            ((ComboBox)Field(form,"paletteBox")).SelectedIndex=3;
            typeof(NeuropixelsPreviewForm).GetMethod("RenderFrame",Private).Invoke(form,null);
            Check(!ReferenceEquals(beforeMap,Field(form,"mapBuffer")),"Palette change did not invalidate static map");
            Check(queue.Generation==sameGeneration && sameSweep.Written==sameWritten && sameSweep.CopiedRows==sameCopied,"Palette-only change reset display history");
            if(!np2) {
                var page=(NpPreviewPage)Field(Field(form,"view"),"Page");
                var bandMap=(Bitmap)Field(form,"mapBuffer");
                ((ComboBox)Field(form,"bandBox")).SelectedIndex=1;
                queue.Push(null,0,Rows(2048,10),2048,2);
                typeof(NeuropixelsPreviewForm).GetMethod("RenderFrame",Private).Invoke(form,null);
                Check(ReferenceEquals(page,Field(Field(form,"view"),"Page")),"Band change altered physical mapping");
                Check(ReferenceEquals(bandMap,Field(form,"mapBuffer")),"Band change rebuilt unchanged static geometry");
            }
            form.KeepAliveWhileRecording=false;form.Close();
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try{
            Directory.CreateDirectory(args[0]);MappingAndQueue();AcceptedMaskShrink(args[1]);Render(args[0],false);Render(args[0],false,true);Render(args[0],true);
            Np2Mapping(args[0],args[2],args[3],args[4]);
            Console.WriteLine("PASS: "+checks+" NP1/NP2 per-connector electrode geometry, compiled payload, applied-receipt gate, gather/input-invariance and render checks");
            Console.WriteLine("Offline PNGs rendered from the real preview form; no device access.");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
