using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;

static class NpFirmwareBundleTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Assembly app;static Type identity,catalog;static int checks;
    static void Check(bool ok,string text){checks++;if(!ok)throw new Exception(text);}
    static object Get(object value,string name){var p=value.GetType().GetProperty(name,All);return p!=null?p.GetValue(value,null):value.GetType().GetField(name,All).GetValue(value);}
    static void Set(object value,string name,object data){var p=value.GetType().GetProperty(name,All);if(p!=null)p.SetValue(value,data,null);else value.GetType().GetField(name,All).SetValue(value,data);}
    static object Call(object value,string name,params object[] args){return value.GetType().GetMethod(name,All).Invoke(value,args);}
    static object Static(Type type,string name,params object[] args){return type.GetMethod(name,All).Invoke(null,args);}
    static void Reject(Action action,string label){try{action();}catch(Exception e){if(e is TargetInvocationException)e=e.InnerException;if(e is IOException||e is InvalidDataException||e is ArgumentException){checks++;return;}throw;}throw new Exception("Accepted "+label);}
    static void Put(byte[] b,int at,uint n){Buffer.BlockCopy(BitConverter.GetBytes(n),0,b,at,4);}
    static uint Crc(byte[] b,int count){uint c=~0U;for(int i=0;i<count;i++){c^=b[i];for(int j=0;j<8;j++)c=(c>>1)^(0xedb88320U&(0U-(c&1)));}return ~c;}
    static byte[] Hex(string s){return Enumerable.Range(0,s.Length/2).Select(i=>Convert.ToByte(s.Substring(i*2,2),16)).ToArray();}
    static byte[] Seal(byte[] b){Put(b,188,Crc(b,188));return b;}
    static void Build(byte[] b,int at,object x){Put(b,at,(uint)Get(x,"imageId"));Put(b,at+4,(uint)Get(x,"baseAddress"));Put(b,at+8,(uint)Get(x,"bytes"));Put(b,at+12,Convert.ToUInt32((string)Get(x,"codeCrc32"),16));Buffer.BlockCopy(Hex((string)Get(x,"buildIdHex")),0,b,at+16,32);Put(b,at+48,(uint)Get(x,"manifestFlags"));Put(b,at+52,(uint)Get(x,"minimumLoaderAbi"));Put(b,at+56,(uint)Get(x,"generation"));}
    static byte[] Wire(object release){var b=new byte[192];Put(b,0,0x3146504e);Put(b,4,0x00c00001);Put(b,8,(uint)Get(release,"family"));Put(b,12,((uint[])Get(release,"boardIds"))[0]);Put(b,16,0x001d0006);Put(b,20,0x5942500b);Put(b,24,0x20313354);Put(b,28,0x11f);Put(b,168,2);Build(b,32,Get(release,"appIdentity"));Build(b,96,Get(release,"usbIdentity"));return Seal(b);}
    static object Parse(byte[] b,uint family){return Static(identity,"Parse",b,family,"001D00065942500B20313354");}
    static object CloneBuild(object build){var x=Activator.CreateInstance(build.GetType(),true);foreach(var p in build.GetType().GetProperties(All))if(p.CanWrite)p.SetValue(x,p.GetValue(build,null),null);return x;}
    static void UsbSnapshots(byte[] identityBody,uint family){
        foreach(bool advertised in new[]{false,true})foreach(bool busy in new[]{false,true})foreach(bool malformed in new[]{false,true}){
            int identityReads=0,sdReads=0;var descriptor=new byte[18];descriptor[0]=18;descriptor[1]=1;descriptor[8]=9;descriptor[9]=0x12;descriptor[10]=0x86;descriptor[11]=0xb3;descriptor[12]=3;descriptor[13]=1;
            var service=new byte[32];Put(service,0,family==1?0x3153504eU:0x3253504eU);Put(service,4,family==1?258U:4U);Put(service,8,2);Put(service,12,15U+(advertised?128U:0U));Put(service,16,busy?1U:0U);Put(service,24,0x001d0006);Put(service,28,0x5942500b^0x20313354);
            Func<byte,byte,ushort,int,byte[]> read=(type,command,value,length)=>{
                Check((type&128)!=0,"Version probe sent OUT");if(command==6)return descriptor;if(command==0x6e)return service;
                if(command==0x6a){identityReads++;Check(length==192&&value==0,"Wrong identity IN shape");var b=(byte[])identityBody.Clone();if(malformed)b[191]^=1;return b;}
                if(command==0x4b){sdReads++;throw new IOException("No cached card in software fixture");}throw new Exception("Unexpected command");
            };
            string path=@"\\?\usb#vid_1209&pid_b386#npv"+family+"-001D00065942500B20313354#{fixture}";
            var snapshot=Static(app.GetType("USB512_interface.NpSystemInfo",true),"ReadSnapshot",path,family==1?"NP V1":"NP V2",read);
            Check(identityReads==(advertised&&!busy?1:0),"Unadvertised or busy firmware was probed");
            Check((Get(snapshot,"Firmware")!=null)==(advertised&&!busy&&!malformed),"Invalid or invented firmware identity");
            Check(sdReads==(busy?0:1),"Optional firmware query broke passive SD information");
        }
    }
    static void Data(string root){
        var bundle=Static(catalog,"Load",root);var notice=app.GetType("USB512_interface.NpFirmwareNotice",true);
        var installedBundle=catalog.GetProperty("Bundled",All).GetValue(null,null);Check(Get(installedBundle,"Error")==null,"Bundled firmware invalid: "+Get(installedBundle,"Error"));
        foreach(uint family in new uint[]{1,2}){
            var release=Call(bundle,"For",family);Check(release!=null,"Family package missing");
            // V2 target IDs are not frozen; exercise the matcher with a local
            // fixture ID without enabling that target in the shipping manifest.
            if(family==2){Check(((uint[])Get(release,"boardIds")).Length==0,"Unagreed V2 target ID enabled");Set(release,"boardIds",new uint[]{0x00020001});}
            foreach(var asset in (Array)Get(release,"files"))Check(File.Exists((string)Call(bundle,"CheckedPath",asset)),"Asset SHA check");
            byte[] body=Wire(release);var value=Parse(body,family);
            UsbSnapshots(body,family);
            var frame=new byte[196];frame[0]=0xad;frame[1]=0xbc;Buffer.BlockCopy(body,0,frame,2,192);frame[194]=frame[195]=0xda;
            foreach(int cut in new[]{0,1,2,3,32,64,128,188,194,195,196}){
                var wire=Activator.CreateInstance(app.GetType("USB512_interface.NpBleWire",true),true);
                var first=(System.Collections.IList)Call(wire,"Feed",frame.Take(cut).ToArray());var second=(System.Collections.IList)Call(wire,"Feed",frame.Skip(cut).ToArray());
                Check(first.Count+second.Count==1,"Fragmented firmware frame lost or split");
            }
            Check((string)Get(value,"Uid")=="001D00065942500B20313354","Full UID");
            Check((bool)Static(notice,"IsCurrent",release,value),"Current version prompted");
            Check(!(bool)Call(release,"HasUpdate",value),"Current version called older");
            string shown=(string)Call(release,"Installed",value);Check(shown.Contains((string)Get(release,"appVersion"))&&shown.Contains((string)Get(release,"usbVersion")),"Exact build version mapping");
            Check(shown.Contains("Boot Not reported"),"Invented installed boot version");
            for(int i=0;i<192;i++){var bad=(byte[])body.Clone();bad[i]^=1;Reject(()=>Parse(bad,family),"response CRC byte"+i);}
            Reject(()=>Parse(body,family==1?2U:1U),"wrong family");
            Reject(()=>Static(identity,"Parse",body,family,"00190027434250062039314E"),"wrong UID");
            foreach(int offset in new[]{0,4,8,16,28,32,36,40,92,96,100,104,156,160,164,168,172}){
                var bad=(byte[])body.Clone();Put(bad,offset,offset==28?0x8000011fU:0xffffffffU);Seal(bad);Reject(()=>Parse(bad,family),"malformed field"+offset);
            }
            var zero=(byte[])body.Clone();Put(zero,44,0);Seal(zero);Check((string)Get(Parse(zero,family),"AppCrc32")=="00000000","CRC zero treated unknown");
            var unknown=(byte[])body.Clone();unknown[48]^=1;Seal(unknown);var changed=Parse(unknown,family);
            Check(!(bool)Static(notice,"IsCurrent",release,changed),"CRC alone matched current build");
            Check(!(bool)Call(release,"HasUpdate",changed),"Unknown/new private build called older");
            Check(!((string)Call(release,"Installed",changed)).Contains("APP "+(string)Get(release,"appVersion")),"Wrong build mapped to friendly version");
            var old=CloneBuild(Get(release,"appIdentity"));Set(old,"codeCrc32","12345678");var previous=Array.CreateInstance(old.GetType(),1);previous.SetValue(old,0);Set(release,"previousApps",previous);
            var older=(byte[])body.Clone();Build(older,32,old);Seal(older);var oldValue=Parse(older,family);
            Check((bool)Call(release,"HasUpdate",oldValue),"Exact qualified predecessor not recognized");
            Put(older,28,0x11b);Seal(older);Check(!(bool)Call(release,"HasUpdate",Parse(older,family)),"Unverified code used to order firmware");
            Put(older,28,0x11f);older[48]^=1;Seal(older);Check(!(bool)Call(release,"HasUpdate",Parse(older,family)),"CRC-only predecessor accepted");
            var missing=(byte[])body.Clone();Array.Clear(missing,32,64);Put(missing,28,0x11a);Seal(missing);Check(Get(Parse(missing,family),"App")==null,"Invalid APP descriptor synthesized");
            string review=(string)Static(notice,"ReviewText",release,null);Check(review.Contains("newer version cannot be confirmed")&&review.Contains((string)Get(release,"limits")),"Unknown/development notice misleading");
            var file=Get(release,"app");string hash=(string)Get(file,"sha256");Set(file,"sha256",new string('0',64));Reject(()=>Call(bundle,"CheckedPath",file),"bad package SHA");Set(file,"sha256",hash);
            string path=(string)Get(file,"path");Set(file,"path","../escape.hex");Reject(()=>Call(bundle,"CheckedPath",file),"path traversal");Set(file,"path",path);
        }
    }
    static void Ui(string output){
        foreach(string type in new[]{"USB512_interface.NpFirmwareForm","USB512_interface.NpV2FirmwareForm"})using(var form=(Form)Activator.CreateInstance(app.GetType(type,true),All,null,new object[]{null},null)){
            var text=(Label)Get(form,"status");text.Text="Selected device / UID 001D00065942500B20313354"+(string)Call(form,"FirmwareVersions");
            Check(!text.Text.Contains("Bundled: Unavailable"),"Bundled versions absent in firmware manager");
            var controls=form.Controls.Cast<Control>().ToArray();
            using(var host=new Form{ClientSize=form.ClientSize,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)}){
                host.Controls.AddRange(controls);host.Show();Application.DoEvents();
                Check(text.Visible&&text.Height>=30,"Firmware versions clipped/hidden");
                Check(text.Text.Contains("Installed:")&&text.Text.Contains("Bundled:"),"Installed/bundled labels missing");
                using(var bmp=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(bmp,new Rectangle(Point.Empty,host.Size));bmp.Save(Path.Combine(output,type.EndsWith("NpFirmwareForm")?"v1-firmware.png":"v2-firmware.png"));}
                host.Hide();form.Controls.AddRange(controls);
            }
        }
    }
    [STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));identity=app.GetType("USB512_interface.NpFirmwareIdentity",true);catalog=app.GetType("USB512_interface.NpFirmwareCatalog",true);Directory.CreateDirectory(args[2]);Application.EnableVisualStyles();Data(Path.GetFullPath(args[1]));Ui(args[2]);Console.WriteLine("PASS "+checks+" firmware bundle/identity/prompt/offline UI checks; no hardware or firmware writes.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
