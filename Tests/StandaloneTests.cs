using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using CancellationToken = System.Threading.CancellationToken;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class StandaloneTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    static Assembly app; static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static object Call(Type type,object target,string method,params object[] arguments){
        try{return type.GetMethod(method,All).Invoke(target,arguments);}
        catch(TargetInvocationException e){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException??e).Throw();throw;}
    }
    static Type T(string name){return app.GetType(name,true);}
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static uint Crc(byte[] b,int count){uint c=~0U;for(int i=0;i<count;i++){c^=b[i];for(int n=0;n<8;n++)c=(c>>1)^((c&1)==0?0:0xedb88320U);}return ~c;}
    static void Seal(byte[] b){Put(b,b.Length-4,Crc(b,b.Length-4));}
    static byte[] Reply(uint magic=0x31524c4e,uint op=0,uint id=91){var b=new byte[64];Put(b,0,magic);Put(b,4,1);Put(b,8,op);Put(b,12,id);b[20]=0x3c;b[21]=0x3e;Seal(b);return b;}
    static byte[] Frame(byte[] b,bool legacy=false){var result=new byte[legacy?68:67];result[0]=legacy?(byte)0xad:(byte)0x3c;result[1]=0xb9;Buffer.BlockCopy(b,0,result,2,64);result[66]=legacy?(byte)0xda:(byte)0x3e;if(legacy)result[67]=0xda;return result;}
    static byte[] StatusReply(int version,uint id=1){
        var reply=Reply(version==1?0x31524c4eU:0x32524c4eU,0,id);Put(reply,4,(uint)version);
        Put(reply,20,1);Put(reply,28,3);Put(reply,32,20000);Put(reply,52,128|17);Seal(reply);return reply;
    }
    static void Reject(Action action,string message){bool rejected=false;try{action();}catch(IOException){rejected=true;}Check(rejected,message);}
    static void BleDetection()
    {
        Type type=T("USB512_interface.NpBleConnection");var discovery=new byte[]{0x81,0,0};
        foreach(int version in new[]{1,2}){
            var identity=StatusReply(version);
            Check((int)Call(type,null,"DeviceVersion",identity,1U)==version,"Device-reported version was not detected");
            Check((bool)Call(type,null,"Matches",(byte)0x82,discovery,(byte)0xb9,identity),"Common discovery receipt rejected");
            Check(!(bool)Call(type,null,"Matches",(byte)0x82,discovery,(byte)0xb8,identity),"Wrong discovery command accepted");
            Check(!(bool)Call(type,null,"Matches",(byte)0x82,discovery,(byte)0xb9,StatusReply(version,91)),"Unrelated discovery transaction accepted");
            int calls=0;
            Func<byte,byte[],CancellationToken,Task<byte[]>> exchange=(command,request,cancel)=>{
                calls++;
                if(calls==1){Check(command==0x82 && request.Length==3 && request[0]==0x81 && request[1]==0 && request[2]==0,"Detection sent a mutation");return Task.FromResult(identity);}
                Check(calls==2 && command==0xb9 && BitConverter.ToUInt32(request,8)==0,"Detection did not use read-only STATUS");
                Check(BitConverter.ToUInt32(request,0)==(version==1?0x31444c4eU:0x32444c4eU),"Status protocol did not follow device identity");
                Check(BitConverter.ToUInt32(request,12)==91,"Identity was not confirmed with a fresh transaction");
                return Task.FromResult(StatusReply(version,91));
            };
            var operation=(Task)Call(type,null,"ReadDevice",exchange,(Func<uint>)(()=>91));operation.GetAwaiter().GetResult();
            var result=operation.GetType().GetProperty("Result").GetValue(operation,null);
            Check((int)result.GetType().GetProperty("Item1").GetValue(result,null)==version && calls==2,"Automatic protocol selection failed");
            foreach(string failure in new[]{"stale","generation","crc","abi","error"}){
                calls=0;
                exchange=(command,request,cancel)=>{
                    if(++calls==1)return Task.FromResult(identity);
                    var body=StatusReply(failure=="generation"?3-version:version,failure=="stale"?90U:91U);
                    if(failure=="crc")body[60]^=1;
                    if(failure=="abi"){Put(body,4,99);Seal(body);}
                    if(failure=="error"){Put(body,16,1);Seal(body);}
                    return Task.FromResult(body);
                };
                operation=(Task)Call(type,null,"ReadDevice",exchange,(Func<uint>)(()=>91));
                Reject(()=>operation.GetAwaiter().GetResult(),"Invalid identity confirmation accepted: "+failure);
                Check(calls==2,"Failed discovery replayed a command");
            }
        }
        var unknown=StatusReply(2);Put(unknown,0,0x33524c4e);Put(unknown,4,3);Seal(unknown);
        Reject(()=>Call(type,null,"DeviceVersion",unknown,1U),"Unknown protocol defaulted to a supported version");
        Check(!(bool)Call(type,null,"Matches",(byte)0x82,new byte[]{0x81,1,0},(byte)0xb9,StatusReply(1)),"Non-read-only discovery request admitted");
        using(var form=(Form)Activator.CreateInstance(T("USB512_interface.NpBleForm"),true)){
            var profiles=(ComboBox)form.GetType().GetField("profiles",All).GetValue(form);
            Check(profiles.Items.Count==0 && !profiles.Enabled,"Profiles were guessed before connecting");
            Check(!((Control)form.GetType().GetField("actions",All).GetValue(form)).Enabled,"Device actions enabled before protocol confirmation");
            var request=(byte[])Call(T("CE32_console.NpSdControl"),null,"Request",2U,91U,3U,20000U,0U,0U,0U,0U,0U);
            Reject(()=>((Task)Call(form.GetType(),form,"Send",request)).GetAwaiter().GetResult(),"Mutation admitted without confirmed protocol");
            form.GetType().GetField("recorderVersion",All).SetValue(form,2);Call(form.GetType(),form,"Profiles",new object[]{null});
            Check(profiles.Items.Count==2,"Detected protocol did not populate matching profiles");
            Call(form.GetType(),form,"ResetDevice");
            Check(profiles.Items.Count==0 && (int)form.GetType().GetField("recorderVersion",All).GetValue(form)==0,"Disconnect retained the previous device version");
        }
    }
    static void Wire()
    {
        Type type=T("USB512_interface.NpBleWire");var frame=Frame(Reply());
        for(int split=1;split<frame.Length;split++){
            var parser=Activator.CreateInstance(type,true);var a=new byte[split];var b=new byte[frame.Length-split];Buffer.BlockCopy(frame,0,a,0,a.Length);Buffer.BlockCopy(frame,split,b,0,b.Length);
            Check(((IList)Call(type,parser,"Feed",a)).Count==0,"Partial frame emitted");Check(((IList)Call(type,parser,"Feed",b)).Count==1,"Fragmented reply lost");
        }
        var wire=Activator.CreateInstance(type,true);var damaged=Frame(Reply());damaged[15]^=1;
        var legacy=Frame(Reply(),true);var joined=new byte[damaged.Length+legacy.Length+frame.Length+5];joined[0]=0xad;joined[1]=0xb9;
        Buffer.BlockCopy(damaged,0,joined,5,damaged.Length);Buffer.BlockCopy(legacy,0,joined,5+damaged.Length,legacy.Length);Buffer.BlockCopy(frame,0,joined,5+damaged.Length+legacy.Length,frame.Length);
        Check(((IList)Call(type,wire,"Feed",joined)).Count==2,"Corrupt/noisy/coalesced/legacy framing");
        var request=Reply(0x31444c4e,2,91);Type connection=T("USB512_interface.NpBleConnection");
        Check((bool)Call(connection,null,"Matches",(byte)0xb9,request,(byte)0xb9,Reply(0x31524c4e,2,91)),"Matching receipt not admitted");
        Check(!(bool)Call(connection,null,"Matches",(byte)0xb9,request,(byte)0xb9,Reply(0x31524c4e,2,90)),"Old transaction admitted");
        Check(!(bool)Call(connection,null,"Matches",(byte)0xb9,request,(byte)0xb9,Reply(0x31524c4e,3,91)),"Wrong operation admitted");
        Check(!(bool)Call(connection,null,"Matches",(byte)0xb9,request,(byte)0xb8,Reply(0x31524c4e,2,91)),"Wrong command admitted");
    }
    static MemoryStream Card(string tag,bool badFooter=false,bool shortExtent=false)
    {
        const uint start=8192,payload=16384,end=32768,blocks=8;
        var card=new MemoryStream(new byte[(payload+blocks*91)*512]);
        Action<uint,byte[]> write=(lba,sector)=>{card.Position=(long)lba*512;card.Write(sector,0,512);};
        var id=new byte[512];Encoding.ASCII.GetBytes("CE_FILESYSTEM_V1").CopyTo(id,0);Encoding.ASCII.GetBytes(tag).CopyTo(id,32);write(16,id);
        var directory=new byte[512];Put(directory,0,1);uint committed=payload+blocks*91;Put(directory,4,shortExtent?committed-1:committed);Put(directory,256,shortExtent?committed-1:committed);write(128,directory);
        var d=new byte[512];Encoding.ASCII.GetBytes("N2SDLOG1").CopyTo(d,0);uint[] fields={11,7,20000,32000000,3,63,4,6,46592,61440,91,4,start,payload,payload+((end-payload)/364)*364,end,65536,0};for(int n=0;n<fields.Length;n++)Put(d,8+n*4,fields[n]);Put(d,96,15);Put(d,100,1);Seal(d);write(start+3,d);
        var f=(byte[])d.Clone();Encoding.ASCII.GetBytes("N2SDEND1").CopyTo(f,0);Put(f,100,badFooter?4U:3U);Put(f,104,badFooter?1U:0U);Put(f,108,blocks);Put(f,112,blocks/4);Put(f,116,committed);Put(f,120,blocks*61440);Seal(f);write(start+4,f);card.Position=0;return card;
    }
    static void Catalog()
    {
        var type=T("CE32_console.NpDownloadCatalog");
        foreach(string tag in new[]{"NP_CE64_1","NP_PSSI_6","NP_PSSI_7"})using(var card=Card(tag)){
            var rows=(IList)Call(type,null,"Read",card);Check(rows.Count==1,"NP2 catalog not listed: "+tag);object row=rows[0];
            Check((uint)row.GetType().GetField("ApHz",All).GetValue(row)==20000,"NP2 rate decoded using NP1 offsets");
            Check((bool)row.GetType().GetProperty("V2",All).GetValue(row,null),"NP2 generation not preserved");Check(row.ToString().Contains("NP2 probes 1 + 2"),"NP2 shown as AP/LFP");
            Check((bool)row.GetType().GetProperty("Downloadable",All).GetValue(row,null),"Valid complete NP2 footer rejected");
            Check((long)Call(T("CE32_console.NpDownloadForm"),null,"RequiredBytes",row)==308L*1536,"NP2 free-space check used NP1 chunks");
        }
        foreach(bool shortExtent in new[]{false,true})using(var card=Card("NP_PSSI_7",!shortExtent,shortExtent)){
            var rows=(IList)Call(type,null,"Read",card);Check(rows.Count==1,"Invalid allocated entry hidden");Check(!(bool)rows[0].GetType().GetProperty("Downloadable",All).GetValue(rows[0],null),"Failed/out-of-bounds NP2 entry downloadable");
        }
        using(var card=Card("UNKNOWN00")){bool rejected=false;try{Call(type,null,"Read",card);}catch(InvalidDataException){rejected=true;}Check(rejected,"Unknown card identity accepted");}
        using(var card=Card("NP_PSSI_7")){card.Position=112*512;card.WriteByte(1);bool rejected=false;try{Call(type,null,"Read",card);}catch(InvalidDataException){rejected=true;}Check(rejected,"Torn journal accepted");}
        var target=T("USB512_interface.NpV2FirmwareForm");const string uid="001B0023434250062039314E";
        Check((string)Call(target,null,"Uid","usb#vid_1209&pid_b386#npv2-"+uid+"#{g}")==uid,"NP2 exact UID not extracted");
        Check(Call(target,null,"Uid","usb#vid_1209&pid_b386#npv1-"+uid+"#{g}")==null,"NP1 admitted into NP2 manager");
        Check(Call(target,null,"Uid","usb#vid_1209&pid_b386#XENP0004#{g}")==null,"Placeholder identity admitted");
        var download=T("CE32_console.NpDownloadForm");
        Check((bool)Call(download,null,"SameSource","usb#b386#npv2-"+uid+"#{old}","usb#b386#npv2-"+uid+"#{new}"),"USB interface GUID changed the bound source");
        Check(!(bool)Call(download,null,"SameSource","usb#b386#npv2-"+uid+"#{old}","usb#b386#npv2-"+uid+"1#{new}"),"Another board substituted as download source");
    }
    sealed class FormatStream : MemoryStream
    {
        internal readonly List<long> Writes=new List<long>();
        internal FormatStream(byte[] bytes):base(bytes){}
        public override void Write(byte[] bytes,int offset,int count){Writes.Add(Position);base.Write(bytes,offset,count);}
    }
    static bool Equal(byte[] first,byte[] second){return StructuralComparisons.StructuralEqualityComparer.Equals(first,second);}
    static void CatalogFormat()
    {
        Type catalog=T("CE32_console.NpDownloadCatalog"),format=T("CE32_console.NpCardFormat");
        foreach(string tag in new[]{"NP_CE64_1","NP_PSSI_6","NP_PSSI_7"})using(var seed=Card(tag)){
            // Reset only the directory head and two journal sectors; retain all other bytes.
            seed.Position=20*512;seed.WriteByte(42);seed.Position=112*512;seed.WriteByte(1);seed.Position=113*512;seed.WriteByte(2);
            var before=seed.ToArray();var expected=(byte[])before.Clone();foreach(int lba in new[]{128,112,113})Array.Clear(expected,lba*512,512);
            using(var card=new FormatStream((byte[])before.Clone())){
                var identity=(byte[])Call(catalog,null,"Sector",card,16L);
                Check((bool)Call(catalog,null,"IsCatalogIdentity",identity),"Supported format identity refused: "+tag);
                Check((bool)Call(catalog,null,"IsNpIdentity",identity)==(tag=="NP_CE64_1"),"Legacy writer identity policy was broadened");
                Call(format,null,"QuickFormat",card,card.Length,identity);
                Check(Equal(card.ToArray(),expected),"Quick Format changed identity/settings/payload or failed to clear the catalog: "+tag);
                Check(((IList)Call(catalog,null,"Read",card)).Count==0,"Formatted catalog not empty: "+tag);
                Check(card.Writes.Count==5 && card.Writes[0]==16*512 && card.Writes[1]==128*512 && card.Writes[2]==112*512 && card.Writes[3]==113*512 && card.Writes[4]==16*512,"Catalog identity was not invalidated first/published last");
            }
        }
        foreach(string failure in new[]{"unknown","future","magic","changed","capacity","unconfirmed"})using(var seed=Card(failure=="unknown"?"UNKNOWN00":failure=="future"?"NP_PSSI_8":"NP_PSSI_7")){
            if(failure=="magic"){seed.Position=16*512;seed.WriteByte(0);}
            var before=seed.ToArray();using(var card=new FormatStream((byte[])before.Clone())){
                var identity=(byte[])Call(catalog,null,"Sector",card,16L);var confirmed=(byte[])identity.Clone();
                if(failure=="changed")confirmed[200]^=1;if(failure=="unconfirmed")confirmed=null;
                Reject(()=>Call(format,null,"QuickFormat",card,failure=="capacity"?card.Length-512:card.Length,confirmed),"Unsafe format admitted: "+failure);
                Check(card.Writes.Count==0 && Equal(card.ToArray(),before),"Refused format wrote to the card: "+failure);
            }
        }
    }
    static IEnumerable<Control> Descendants(Control owner){foreach(Control child in owner.Controls){yield return child;foreach(var next in Descendants(child))yield return next;}}
    static void Controls()
    {
        Type status=T("CE32_console.NpSdStatus");var s=Activator.CreateInstance(status,true);
        Action<string,uint> set=(name,value)=>status.GetField(name,All).SetValue(s,value);
        set("State",0);set("Mask",3);set("Hz",20000);set("Start",8192);set("Capacity",1000000);set("Flags",1048576|17);
        Type v1=T("CE32_console.NpV1BleProtocol");Check((bool)Call(v1,null,"CanStart",s),"Ready NP1 gated");
        set("Flags",1048576|17|8);Check(!(bool)Call(v1,null,"CanStart",s),"Pending NP1 mutation permits Start");
        set("Flags",1048576|17);set("Fault",1);Check(!(bool)Call(v1,null,"CanStart",s),"Failed NP1 permits Start");
        set("Fault",0);set("State",1);set("Flags",128|512|8192);Type v2=T("CE32_console.NpV2SdControl");
        Check((bool)Call(v2,null,"CanStart",s),"Ready NP2 gated");set("State",4);Check(!(bool)Call(v2,null,"CanStart",s),"Draining NP2 permits Start");Check(!(bool)Call(v2,null,"CanSwitchMode",s),"Draining NP2 permits mode handoff");
        set("State",5);Check((bool)Call(v2,null,"CanSwitchMode",s),"Complete NP2 mode handoff unavailable");set("Flags",128|512|8192|8);Check(!(bool)Call(v2,null,"CanSwitchMode",s),"Pending NP2 mutation permits mode handoff");
        Type download=T("CE32_console.NpDownloadForm");
        using(var form=(Form)Activator.CreateInstance(download,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{"usb#b386#bound#{a}"},null)){
            var list=(ComboBox)download.GetField("sources",All).GetValue(form);Type source=download.GetNestedType("Source",BindingFlags.NonPublic);
            Func<string,object> item=path=>{var row=Activator.CreateInstance(source,true);source.GetField("Path",All).SetValue(row,path);source.GetField("Label",All).SetValue(row,path);return row;};
            list.Items.Add(item("usb#b386#neighbor#{a}"));Call(download,form,"SelectBoundSource");Check(list.SelectedIndex==-1,"Missing download target replaced with a neighbor");
            list.Items.Add(item("usb#b386#bound#{b}"));Call(download,form,"SelectBoundSource");Check(list.SelectedIndex==1,"Exact download target not retained across GUID change");
            list.Items.Clear();list.Items.Add(item("usb#b386#neighbor#{a}"));Call(download,form,"SelectBoundSource");Check(list.SelectedIndex==-1,"Unplugged download target replaced silently");
        }
    }
    static void Workspace()
    {
        Type type=T("USB512_interface.Form1");
        using(var form=(Form)Activator.CreateInstance(type,true)){
            var tabs=(TabControl)type.GetField("workspaceTabs",All).GetValue(form);
            var nativeTab=tabs.Handle; // Selection cancellation is a native TabControl event.
            Check(tabs.TabPages.Count==4 && tabs.TabPages[0].Text=="USB Mode" && tabs.TabPages[1].Text=="BLE Mode" && tabs.TabPages[2].Text=="Data Download" && tabs.TabPages[3].Text=="Advanced","USB/BLE/download/advanced pages missing");
            var firmwareButton=(Control)type.GetField("btnDfu",All).GetValue(form);
            while(firmwareButton!=null && !(firmwareButton is TabPage))firmwareButton=firmwareButton.Parent;
            Check(firmwareButton==tabs.TabPages[3],"Firmware management not isolated in Advanced");
            type.GetField("running",All).SetValue(form,true);tabs.SelectedIndex=1;Check(tabs.SelectedIndex==0,"Workspace changed during USB capture");
            tabs.SelectedIndex=3;Check(tabs.SelectedIndex==0,"Advanced tools reachable during USB capture");
            type.GetField("running",All).SetValue(form,false);
            type.GetField("firmwareManagementActive",All).SetValue(form,true);tabs.SelectedIndex=1;Check(tabs.SelectedIndex==0,"Workspace changed during firmware management");
            type.GetField("firmwareManagementActive",All).SetValue(form,false);
            type.GetField("deviceRefreshActive",All).SetValue(form,1);tabs.SelectedIndex=1;Check(tabs.SelectedIndex==0,"Workspace changed during USB discovery");
            type.GetField("deviceRefreshActive",All).SetValue(form,0);
            tabs.SelectedIndex=2;Check(tabs.SelectedIndex==2,"Idle Data Download page inaccessible");
            var download=type.GetField("downloadWorkspace",All).GetValue(form);Call(download.GetType(),download,"SetBusy",true);
            tabs.SelectedIndex=0;Check(tabs.SelectedIndex==2,"Workspace changed during download");
            Check(!((Button)type.GetField("btnDfu",All).GetValue(form)).Enabled,"Firmware allowed during download");
            var close=new FormClosingEventArgs(CloseReason.UserClosing,false);
            typeof(Form).GetMethod("OnFormClosing",All).Invoke(form,new object[]{close});Check(close.Cancel,"Main window closed during download");
            Call(download.GetType(),download,"SetBusy",false);tabs.SelectedIndex=0;Check(tabs.SelectedIndex==0,"USB page not restored after download");
            Check(!((Timer)download.GetType().GetField("enumerationTimer",All).GetValue(download)).Enabled,"Embedded layout started hardware enumeration");
            tabs.SelectedIndex=1;
            var ble=(Form)type.GetField("bleWorkspace",All).GetValue(form);
            Check(!ble.TopLevel && ble.Parent!=null,"BLE Mode did not embed its controls");
            Check(!(bool)ble.GetType().GetProperty("SessionOwned",All).GetValue(ble,null),"Entering BLE Mode started a connection or scan");
            Check(ble.GetType().GetProperty("DialogOwner",All).GetValue(ble,null)==form,"Embedded BLE dialogs do not belong to the main window");
            var done=new TaskCompletionSource<object>();
            var command=(Task)Call(ble.GetType(),ble,"Run",(Func<Task>)(()=>done.Task));
            tabs.SelectedIndex=0;Check(tabs.SelectedIndex==1,"USB Mode accessible during a BLE command");
            tabs.SelectedIndex=3;Check(tabs.SelectedIndex==1,"Advanced accessible during a BLE command");
            Check(!((Button)type.GetField("btnDfu",All).GetValue(form)).Enabled,"Firmware allowed during a BLE command");
            close=new FormClosingEventArgs(CloseReason.UserClosing,false);typeof(Form).GetMethod("OnFormClosing",All).Invoke(form,new object[]{close});Check(close.Cancel,"Window closed during a BLE command");
            done.SetResult(null);var deadline=DateTime.UtcNow.AddSeconds(5);while(!command.IsCompleted && DateTime.UtcNow<deadline)Application.DoEvents();
            Check(command.IsCompleted,"BLE operation gate did not release");command.GetAwaiter().GetResult();
            tabs.SelectedIndex=0;Check(tabs.SelectedIndex==0,"USB Mode not restored after BLE command");
        }
    }
    static void Snapshot(string typeName,string output,string filename,params object[] args)
    {
        using(var form=(Form)Activator.CreateInstance(T(typeName),BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,args,null)){
            if(typeName=="USB512_interface.Form1"){
                Check(form.Text.StartsWith("WILD-X"),"Independent branding missing");
                bool ble=false,download=false;foreach(var child in Descendants(form)){ble|=child.Text=="BLE Control";download|=child.Text=="SD Download";}Check(ble&&download,"Required top-level controls missing");
            }
            if(typeName=="USB512_interface.NpBleForm"){
                bool maintenance=args.Length==1 && (bool)args[0];bool loader=false,dfu=false;
                foreach(var child in Descendants(form)){loader|=child.Text=="Firmware loader";dfu|=child.Text=="ROM DFU";}
                Check(loader==maintenance && dfu==maintenance,"BLE firmware/recovery handoffs leaked into the routine workflow");
            }
            // Reparent only the controls to a hidden offscreen host. The product
            // Load/Shown handlers (USB/BLE discovery) are never invoked.
            var controls=new List<Control>();foreach(Control child in form.Controls)controls.Add(child);
            using(var host=new Form{Text=form.Text,AutoScaleMode=AutoScaleMode.None,Font=form.Font,BackColor=form.BackColor,ForeColor=form.ForeColor,
                ClientSize=form.ClientSize,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)}){
                foreach(var child in controls)host.Controls.Add(child);
                host.Show();Application.DoEvents();
                using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(Path.Combine(output,filename+".png"),ImageFormat.Png);}
                if(typeName=="USB512_interface.Form1"){
                    var tabs=(TabControl)T(typeName).GetField("workspaceTabs",All).GetValue(form);
                    tabs.SelectedIndex=1;Application.DoEvents();
                    using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(Path.Combine(output,"ble-mode.png"),ImageFormat.Png);}
                    tabs.SelectedIndex=2;Application.DoEvents();
                    using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(Path.Combine(output,"data-download.png"),ImageFormat.Png);}
                    tabs.SelectedIndex=3;Application.DoEvents();
                    using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(Path.Combine(output,"advanced.png"),ImageFormat.Png);}
                    host.ClientSize=new Size(1044,741);tabs.SelectedIndex=0;Application.DoEvents();
                    var sdInfo=(Button)T(typeName).GetField("btnSdInfo",All).GetValue(form);
                    Check(sdInfo.Bottom<=sdInfo.Parent.ClientSize.Height,"SD Info clipped at minimum width");
                    Check(((Control)T(typeName).GetField("imuTelemetry",All).GetValue(form)).Height>=136,"Live telemetry clipped at minimum window size");
                    using(var bitmap=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bitmap,new Rectangle(Point.Empty,host.Size));bitmap.Save(Path.Combine(output,"main-minimum.png"),ImageFormat.Png);}
                }
                host.Hide();foreach(var child in controls)form.Controls.Add(child);
            }
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);Application.EnableVisualStyles();
            if(args.Length==3 && args[2]=="--catalog-format-only"){
                CatalogFormat();Console.WriteLine("PASS "+checks+" focused catalog-format checks: all three supported V1/V2 identities, exact identity preservation, common reset sequence, unchanged settings/payload/legacy writer policy, unknown or changed identity/capacity refusal before writes; memory streams only.");return 0;
            }
            Wire();BleDetection();Catalog();CatalogFormat();Controls();Workspace();Snapshot("USB512_interface.Form1",output,"main");Snapshot("CE32_console.NpDownloadForm",output,"download",new object[]{null});Snapshot("USB512_interface.NpV2FirmwareForm",output,"firmware",new object[]{null});Snapshot("USB512_interface.NpBleForm",output,"ble");Snapshot("USB512_interface.NpBleForm",output,"ble-advanced",true);
            Console.WriteLine("PASS "+checks+" standalone checks: fragmented/corrupt/stale BLE receipts, NP2 native/shared catalogs, failed extents, torn journal, exact generation/UID selection and offline UI rendering; no hardware scan or commands.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
