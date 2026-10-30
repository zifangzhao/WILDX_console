using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class NpSdUsbTestUiTests {
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    const string Uid1="001D00065942500B20313354",Uid2="001A0004434250062039314E";
    static Assembly app;static string output;static int checks,removedEvents,stoppedTimers;
    static readonly List<string> failures=new List<string>();
    static readonly List<object> renders=new List<object>();
    static readonly List<Font> scaledFonts=new List<Font>();
    static readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=8*1024*1024};
    static Type T(string name){return app.GetType(name.Contains(".")?name:"USB512_interface."+name,true);}
    static object Get(object o,string n){var f=o.GetType().GetField(n,All);return f!=null?f.GetValue(o):o.GetType().GetProperty(n,All).GetValue(o,null);}
    static void Set(object o,string n,object v){o.GetType().GetField(n,All).SetValue(o,v);}
    static object Call(object o,string n,params object[] args){var t=o as Type??o.GetType();var m=t.GetMethods(All).Single(x=>x.Name==n&&x.GetParameters().Length==args.Length);try{return m.Invoke(o is Type?null:o,args);}catch(TargetInvocationException e){throw e.InnerException??e;}}
    static void Need(bool ok,string message){checks++;if(!ok)failures.Add(message);}
    static IEnumerable<Control> Tree(Control c){yield return c;foreach(Control child in c.Controls)foreach(var descendant in Tree(child))yield return descendant;}
    static void NoEvents(Form f){
        var list=(EventHandlerList)typeof(Component).GetProperty("Events",All).GetValue(f,null);
        foreach(string name in new[]{"EVENT_LOAD","EVENT_SHOWN","EVENT_ACTIVATED"}){
            var field=typeof(Form).GetField(name,All);if(field==null)continue;var key=field.GetValue(null);var handler=list[key];if(handler!=null){list.RemoveHandler(key,handler);removedEvents++;}
        }
    }
    static void StopTimers(object owner){
        foreach(var f in owner.GetType().GetFields(All)){
            if(f.IsStatic)continue;var value=f.GetValue(owner);
            var ui=value as System.Windows.Forms.Timer;if(ui!=null){ui.Stop();stoppedTimers++;}
            var background=value as System.Threading.Timer;if(background!=null){background.Change(Timeout.Infinite,Timeout.Infinite);stoppedTimers++;}
            var container=value as IContainer;if(container!=null)foreach(IComponent c in container.Components){var timer=c as System.Windows.Forms.Timer;if(timer!=null){timer.Stop();stoppedTimers++;}}
        }
    }
    static void Put(byte[] b,int at,uint x){Buffer.BlockCopy(BitConverter.GetBytes(x),0,b,at,4);}
    static uint Crc(byte[] b,int count){uint c=UInt32.MaxValue;for(int i=0;i<count;i++){c^=b[i];for(int n=0;n<8;n++)c=(c>>1)^((0U-(c&1))&0xedb88320U);}return ~c;}
    static object Report(int family,uint mask,uint cap=0x10000,uint maxSeconds=120,uint? supportedRates=null){
        var b=Enumerable.Repeat((byte)255,256).ToArray();Put(b,0,0x3152514e);Put(b,4,1);Put(b,8,256);Put(b,12,cap);Put(b,16,0);Put(b,20,0);Put(b,24,0x2f);Put(b,28,0);
        Put(b,32,0);Put(b,36,0);Put(b,48,(uint)family);string uid=family==1?Uid1:Uid2;
        for(int p=0;p<3;p++)Put(b,68+p*4,Convert.ToUInt32(uid.Substring(p*8,8),16));
        Put(b,80,448585456);for(int p=0;p<4;p++)Put(b,84+p*4,0x12345678U+(uint)p);Put(b,100,1056768);Put(b,104,0);
        uint rates=supportedRates??(family==1?0x0000f0f0U:0x7ffffff0U);Put(b,108,rates);Put(b,112,maxSeconds);Put(b,120,mask);Put(b,124,family==1&&mask==7?15000U:20000U);Put(b,252,Crc(b,252));
        return Call(T("NpSdUsbTestWire"),"ParseReport",b,family,uid);
    }
    static Form Sd(){
        var f=(Form)Activator.CreateInstance(T("NpSdTestForm"),All,null,new object[]{null,null},null);NoEvents(f);
        Need(f.Controls.Count>0,"SD form constructed without BLE");return f;
    }
    static void Inject(Form f,object report){
        Set(f,"discovering",true);var combo=(ComboBox)Get(f,"usb");combo.Items.Clear();
        var target=Activator.CreateInstance(f.GetType().GetNestedType("UsbTarget",All),true);Set(target,"Path","software-only-no-device");combo.Items.Add(target);combo.SelectedIndex=0;Set(f,"discovering",false);
        Call(f,"ApplyInspection",report,"software-only-no-device");
    }
    static uint[] Rates(Form f){return ((ListView)Get(f,"rates")).Items.Cast<ListViewItem>().Select(x=>(uint)x.Tag).ToArray();}
    static void Select(Form f,uint mask){var probes=(CheckBox[])Get(f,"probes");for(int p=0;p<probes.Length;p++)probes[p].Checked=(mask&(1U<<p))!=0;}
    static void Model(){
        foreach(int family in new[]{1,2})foreach(uint mask in (family==1?new[]{1U,3U}:new[]{1U,3U,7U}))using(var f=Sd()){
            var tabs=(TabControl)Get(f,"tabs");var duration=(NumericUpDown)Get(f,"seconds");
            Need(tabs.SelectedIndex<=0&&tabs.TabPages[0].Text=="USB logger"&&tabs.TabPages[1].Text=="PC card reader","USB logger tab first before native handle creation");
            Need(duration.Minimum==10&&duration.Maximum==120&&duration.Value==60,"duration10..120/default60");
            var report=Report(family,mask);Inject(f,report);var probes=(CheckBox[])Get(f,"probes");
            var view=(ListView)Get(f,"rates");Need(view.Columns.Count==6&&view.Columns[2].Text=="Max write"&&view.Columns[3].Text=="Gaps"&&view.Columns[4].Text=="Max NP gap","gap statistics columns are present");
            Need(view.Items.Cast<ListViewItem>().All(row=>row.SubItems[2].Text=="—"&&row.SubItems[3].Text=="—"&&row.SubItems[4].Text=="—"),"preparing rows show unknown statistics, not zero");
            Need((uint)Get(f,"Mask")==mask&&((Button)Get(f,"start")).Enabled,"supported report enables selected mask without BLE");
            Need(probes[2].Enabled==(family==2),"port3 availability follows this fixture's advertised masks");
            uint[] expected=family==2&&mask==7?new[]{20000U,15000U,7500U}:new[]{30000U,20000U,15000U,7500U};Need(Rates(f).SequenceEqual(expected),"descending physically legal rates for V"+family+" mask"+mask);
            Select(f,1);Need((uint)Get(f,"Mask")==1&&Rates(f).First()==30000,"checkbox changes fixed mask and applicable rates");
            Select(f,0);Need((uint)Get(f,"Mask")==0&&Rates(f).Length==0&&!((Button)Get(f,"start")).Enabled,"all unchecked disables Start without exception");
            if(family==2){Select(f,5);Need((uint)Get(f,"Mask")==5&&Rates(f).Length==0&&!((Button)Get(f,"start")).Enabled,"nonprefix V2 mask does not invent a physical profile");Select(f,3);Need(Rates(f).SequenceEqual(new[]{30000U,20000U,15000U,7500U}),"fewer V2 probes include advertised30k");}
            Need((uint?)Get(report,"PriorMask")==mask,"checkboxes do not mutate report/native prior mask");
        }
        using(var f=Sd()){
            Inject(f,Report(1,7,0x10000,120,0xf000f0f0));var probes=(CheckBox[])Get(f,"probes");
            Need(probes.All(p=>p.Enabled)&&(uint)Get(f,"Mask")==7&&((Button)Get(f,"start")).Enabled,"V1 advertised three-probe mask enables probe3");
            Need(Rates(f).SequenceEqual(new[]{15000U,7500U}),"V1 three-probe policy permits15k then7.5k, not20/30k");
            Select(f,3);Need(Rates(f).SequenceEqual(new[]{30000U,20000U,15000U,7500U})&&probes[2].Enabled,"fewer V1 probes permit higher rates without hiding advertised probe3");
            Inject(f,Report(1,3,0x10000,120,0x0000f0f0));Need(!probes[2].Enabled&&(uint)Get(f,"Mask")==3,"V1 prefix1/3-only firmware disables unavailable probe3");
        }
        using(var f=Sd()){
            Inject(f,Report(2,7,0x10000,120,0xf000f0f0));
            Need((uint)Get(f,"Mask")==7&&((Button)Get(f,"start")).Enabled,"allrates V2 three-probe report enabled without BLE");
            Need(Rates(f).SequenceEqual(new[]{30000U,20000U,15000U,7500U}),"V2 three probes30k first when firmware advertises all four rates");
        }
        using(var f=Sd()){
            Inject(f,Report(2,7,0x40));Need(Rates(f).Length==0&&!((Button)Get(f,"start")).Enabled,"old APP-update cap is not SD-test support");
            var action=new Func<Task>(()=>Task.FromException(new NotSupportedException("USB SD recording tests need matching APP and USB firmware. Update the full bundled firmware in Advanced.")));
            ((Task)Call(f,"Execute",action)).GetAwaiter().GetResult();string text=((Label)Get(f,"status")).Text;
            Need(text.Contains("matching APP and USB")&&text.Contains("full bundled")&&!((Button)Get(f,"start")).Enabled,"unsupported-firmware presentation is explicit and non-starting");
            bool rejected=false;try{Call(f,"ApplyInspection",Report(2,7,0x10000,9),"software-only-no-device");}catch(NotSupportedException){rejected=true;}Need(rejected,"firmware maximum below10 seconds rejected");
            var badMaximum=new Func<Task>(()=>{Call(f,"ApplyInspection",Report(2,7,0x10000,9),"software-only-no-device");return Task.CompletedTask;});
            ((Task)Call(f,"Execute",badMaximum)).GetAwaiter().GetResult();Need(!((Button)Get(f,"start")).Enabled&&Rates(f).Length==0&&Get(f,"inspected")==null,"unsupported duration remains disabled after actual Execute.finally");
        }
    }
    static object Profile(int kind){
        var profile=Activator.CreateInstance(T("NpSdUsbProfileResult"),true);Set(profile,"SampleHz",30000U);Set(profile,"Status","Software-only presentation fixture");var gaps=Get(profile,"Gaps");
        Need(gaps!=null,"profile always owns a non-null initially-unknown gap object");
        if(kind==1||kind==2){
            Set(gaps,"CardMaximumWriteMs",kind==1?0d:12.5d);Set(gaps,"CardMeanWriteMs",kind==1?0d:2.125d);Set(gaps,"MaximumServiceMs",kind==1?0d:3.25d);
            Set(gaps,"NeuralDistributionKnown",true);Set(gaps,"NeuralGapCount",kind==1?0U:3U);Set(gaps,"NeuralTotalMissingMs",kind==1?0m:5.5m);Set(gaps,"NeuralMaximumGapMs",kind==1?0m:3.25m);Set(gaps,"NeuralMeanGapMs",kind==1?0m:1.833m);Set(gaps,"NeuralP95GapMs",kind==1?0m:2.75m);Set(gaps,"NeuralP99GapMs",kind==1?0m:3.1m);Set(gaps,"MissingStoredBlocks",kind==1?0U:8U);Set(gaps,"StorageBreaks",kind==1?0U:2U);
        }else if(kind==3){
            Set(gaps,"V1LossEventsLowerBound",4U);Set(gaps,"V1SkippedLaneBytesLowerBound",new ulong[]{4096,1024});
            Set(gaps,"NeuralGapError","Exact V1 gap durations and positions are unavailable. Skipped lane-byte counts are lower bounds; no missing sample times were invented.");
        }
        return profile;
    }
    static void Stats(){
        using(var f=Sd()){
            Inject(f,Report(2,7,0x10000,120,0xf000f0f0));var view=(ListView)Get(f,"rates");var row=view.Items[0];
            var unknown=Profile(0);Call(f,"ApplyProfileResult",unknown);string text=(string)Call(T("NpSdTestForm"),"GapDescription",Get(unknown,"Gaps"));
            Need(row.SubItems[2].Text=="—"&&row.SubItems[3].Text=="—"&&row.SubItems[4].Text=="—"&&text.Contains("not reported"),"null statistics are explicitly unknown");
            var zero=Profile(1);Call(f,"ApplyProfileResult",zero);text=(string)Call(T("NpSdTestForm"),"GapDescription",Get(zero,"Gaps"));
            Need(row.SubItems[2].Text=="0 ms"&&row.SubItems[3].Text=="0"&&row.SubItems[4].Text=="0 ms","known zero statistics remain numeric zero");
            Need(text.Contains("0 events")&&text.Contains("p95 0 ms")&&text.Contains("p99 0 ms")&&!text.Contains("not reported"),"known zero distribution is distinct from missing distribution");
            var populated=Profile(2);Call(f,"ApplyProfileResult",populated);text=(string)Call(T("NpSdTestForm"),"GapDescription",Get(populated,"Gaps"));
            Need(row.SubItems[2].Text=="12.5 ms"&&row.SubItems[3].Text=="3"&&row.SubItems[4].Text=="3.25 ms","actual populated row formatting and units");
            Need(text.Contains("mean 1.833 ms")&&text.Contains("p95 2.75 ms")&&text.Contains("p99 3.1 ms"),"gap percentile and mean labels");
            Need(row.ToolTipText==text&&((Label)Get(f,"gapDetails")).Text==text,"detail label and row tooltip use the actual formatter");
        }
        using(var f=Sd()){
            Inject(f,Report(1,3));var profile=Profile(3);Call(f,"ApplyProfileResult",profile);var row=((ListView)Get(f,"rates")).Items[0];string text=(string)Call(T("NpSdTestForm"),"GapDescription",Get(profile,"Gaps"));
            Need(row.SubItems[3].Text=="≥4"&&row.SubItems[4].Text=="—","V1 lower-bound counts do not fabricate an exact maximum gap");
            Need(text.Contains("at least 4 loss events")&&text.Contains("lower bounds")&&text.Contains("4096 / 1024")&&text.Contains("not reported"),"V1 skipped bytes and exact-duration limits are explicit");
        }
    }
    static Rectangle InForm(Control c,Form f){return new Rectangle(f.PointToClient(c.PointToScreen(Point.Empty)),c.Size);}
    static string Label(Control c){return c.GetType().Name+":"+(String.IsNullOrEmpty(c.Name)?c.Text:c.Name);}
    static List<string> Fit(Form f){
        var issues=new List<string>();
        foreach(var c in Tree(f).Where(c=>c!=f&&c.Visible&&!c.HasChildren)){
            if(c.Parent==null||c.Width==0||c.Height==0)continue;
            if(!c.Parent.ClientRectangle.Contains(c.Bounds))issues.Add(Label(c)+" outside parent "+c.Bounds+" / "+c.Parent.ClientRectangle);
            var label=c as Label;if(label!=null&&!label.AutoEllipsis&&label.GetPreferredSize(new Size(Math.Max(1,label.Width),0)).Height>label.Height+2)issues.Add(Label(c)+" preferred text height exceeds actual");
        }
        return issues;
    }
    static void Offscreen(Form f){f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-20000,-20000);f.Show();f.PerformLayout();Application.DoEvents();}
    static void Save(Form f,string name){using(var bmp=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));bmp.Save(Path.Combine(output,name));}}
    static void Render(int width,int height,float scale,bool allRates=false,bool stress=false,bool v1Three=false){
        using(var f=Sd()){
            Inject(f,v1Three?Report(1,7,0x10000,120,0xf000f0f0):stress?Report(1,3):allRates?Report(2,7,0x10000,120,0xf000f0f0):Report(2,7));f.AutoScaleMode=AutoScaleMode.None;
            if(scale!=1){var originals=Tree(f).Select(c=>new{Control=c,Font=c.Font}).ToArray();f.Scale(new SizeF(scale,scale));foreach(var x in originals){var font=new Font(x.Font.FontFamily,x.Font.SizeInPoints*scale,x.Font.Style,GraphicsUnit.Point);scaledFonts.Add(font);x.Control.Font=font;}}
            f.ClientSize=new Size((int)Math.Round(width*scale),(int)Math.Round(height*scale));((Label)Get(f,"status")).Text="Ready. The selected probes stay fixed throughout the rate sweep. Stored readback is required before a pass.";
            if(stress){string longPath=@"C:\Recording reports\animal_identifier_with_a_long_name\experiment_2026-10-30_session_1234567890\SD_test_reports_and_stored_readback\sd-test.json";((TextBox)Get(f,"reportRoot")).Text=Path.GetDirectoryName(longPath);((Label)Get(f,"status")).Text="Inconclusive. Exact source-gap durations are unavailable; the stored recording and report remain preserved.\r\nReport: "+longPath;Call(f,"ApplyProfileResult",Profile(3));}
            Offscreen(f);Need(((TabControl)Get(f,"tabs")).SelectedIndex==0,"USB logger selected after offscreen Show");var list=(ListView)Get(f,"rates");int columns=list.Columns.Cast<ColumnHeader>().Sum(x=>x.Width);var issues=Fit(f);
            int fullRows=list.Items.Cast<ListViewItem>().Count(item=>{var rect=list.GetItemRect(item.Index);return rect.Top>=0&&rect.Height>0&&rect.Bottom<=list.ClientSize.Height;});
            Need(columns<=list.ClientSize.Width,"result columns fit at "+width+"x"+height+" scale"+scale);Need(fullRows>=1,"at least one complete native result row at "+width+"x"+height+" scale"+scale);
            var statistics=Tree(f).OfType<Button>().Single(b=>b.Text=="Statistics…");Rectangle statsVisible=InForm(statistics,f);for(Control p=statistics.Parent;p!=null&&p!=f;p=p.Parent)statsVisible=Rectangle.Intersect(statsVisible,new Rectangle(f.PointToClient(p.PointToScreen(Point.Empty)),p.ClientSize));
            Need(statistics.Visible&&statsVisible.Size==statistics.Size,"Statistics button fully visible at "+width+"x"+height+" scale"+scale);
            foreach(var issue in issues)Need(false,"layout "+width+"x"+height+" scale"+scale+": "+issue);
            string image="sd-test-"+width+"x"+height+"-"+(int)(scale*100)+(allRates?"-allrates":"")+(stress?"-stats-longpath":"")+(v1Three?"-v1-three":"")+".png";Save(f,image);
            renders.Add(new{image,logical_width=width,logical_height=height,modeled_scale=scale,advertised_rates=((uint)Get(Get(f,"inspected"),"SupportedRates")).ToString("X8"),family=Get(Get(f,"inspected"),"Family"),actual_device_dpi=f.DeviceDpi,physical_width=f.Width,physical_height=f.Height,list_client_width=list.ClientSize.Width,list_client_height=list.ClientSize.Height,fully_visible_native_rows=fullRows,rate_rows=list.Items.Count,column_widths=list.Columns.Cast<ColumnHeader>().Select(x=>x.Width).ToArray(),statistics_button_bounds=InForm(statistics,f).ToString(),gap_details=((Label)Get(f,"gapDetails")).Text,status_text=((Label)Get(f,"status")).Text,issues});
        }
    }
    static void MainPlacement(){
        using(var f=(Form)Activator.CreateInstance(T("Form1"),true)){
            NoEvents(f);StopTimers(f);var ble=(Form)Get(f,"bleWorkspace");NoEvents(ble);StopTimers(ble);var download=(Form)Get(f,"downloadWorkspace");NoEvents(download);StopTimers(download);
            var idle=Get(f,"idleNpSensorTimer") as System.Threading.Timer;if(idle!=null)idle.Change(Timeout.Infinite,Timeout.Infinite);
            f.ClientSize=new Size(1180,850);Offscreen(f);var tabs=(TabControl)Get(f,"workspaceTabs");tabs.SelectedIndex=0;Application.DoEvents();
            var buttons=Tree(f).Where(c=>c.Name=="sdCardTest").ToArray();Need(buttons.Length==1,"one SD test button in main form");
            var button=(Button)Get(f,"sdCardTestButton");var usb=(TabPage)Get(f,"usbPage");var blePage=(TabPage)Get(f,"blePage");var advanced=(TabPage)Get(f,"advancedPage");
            Need(Tree(usb).Contains(button)&&!Tree(blePage).Contains(button)&&!Tree(advanced).Contains(button),"SD test located only in USB mode");
            Rectangle visible=InForm(button,f);for(Control p=button.Parent;p!=null&&p!=f;p=p.Parent)visible=Rectangle.Intersect(visible,new Rectangle(f.PointToClient(p.PointToScreen(Point.Empty)),p.ClientSize));
            Need(button.Visible&&visible.Size==button.Size,"main USB SD test button is visible and unclipped");Save(f,"main-usb-sd-test.png");
            renders.Add(new{image="main-usb-sd-test.png",button_bounds=InForm(button,f).ToString(),visible_bounds=visible.ToString(),placement="USB only"});
            if(idle!=null)idle.Dispose();f.Hide();
        }
    }
    static string Sha(string p){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(p))).Replace("-","");}
    [STAThread]static int Main(string[] args){
        try{
            if(args.Length!=2)throw new ArgumentException("actual215 app path + fresh output directory");string path=Path.GetFullPath(args[0]);output=Path.GetFullPath(args[1]);if(Directory.Exists(output))throw new IOException("Fresh output required");Directory.CreateDirectory(output);
            Thread.CurrentThread.CurrentCulture=CultureInfo.InvariantCulture;string directory=Path.GetDirectoryName(path);AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{string p=Path.Combine(directory,new AssemblyName(e.Name).Name+".dll");return File.Exists(p)?Assembly.LoadFrom(p):null;};
            app=Assembly.LoadFrom(path);if(app.GetName().Version.ToString()!="1.0.0.215"||Sha(path)!="DA0A71C9AE732F9EF1C15405EEE33F6B498A0C10B3121BA36663CF58F1BA1CCC")throw new InvalidDataException("Only exact actual215 may be checked by this fixture");Need(true,"actual215 required");Application.EnableVisualStyles();
            var settings=(ApplicationSettingsBase)T("USB512_interface.Properties.Settings").GetProperty("Default",All).GetValue(null,null);SettingsSavingEventHandler noSave=(s,e)=>e.Cancel=true;settings.SettingsSaving+=noSave;
            try{Model();Stats();Render(940,650,1);Render(780,560,1);Render(940,650,1.25f);Render(780,560,1.25f);Render(940,650,1.25f,true);Render(780,560,1.25f,true,true);Render(940,650,1,false,false,true);MainPlacement();}finally{settings.SettingsSaving-=noSave;foreach(var font in scaledFonts)font.Dispose();}
            File.WriteAllText(Path.Combine(output,"receipt.json"),json.Serialize(new{status=failures.Count==0?"PASS":"FAIL",checks,failures,app_sha256=Sha(path),app_version=app.GetName().Version.ToString(),removed_discovery_event_handlers=removedEvents,timers_stopped=stoppedTimers,statistics_dialogs_opened=0,detail_scope="Pure GapDescription/row tooltip/full gapDetails.Text asserted; Statistics button fully visible; no popup opened",hardware_calls=0,device_discovery_calls=0,run_or_reader_buttons_invoked=false,preferences_saved=false,dpi_scope="125% is modeled font/control scaling; actual DeviceDpi retained per image; OS settings unchanged",renders}));
            Console.WriteLine((failures.Count==0?"PASS ":"FAIL ")+checks+" actual215 SD UI checks; "+failures.Count+" findings; no hardware/discovery");foreach(string f in failures)Console.Error.WriteLine(f);return failures.Count==0?0:1;
        }catch(Exception e){Console.Error.WriteLine(e);return 2;}
    }
}
