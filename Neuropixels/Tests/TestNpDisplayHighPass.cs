using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

// Actual GUI assembly, display-owned samples only; no device/network access.
static class NpDisplayHighPassTests {
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
 static Assembly gui;static int checks;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static object Get(object o,string name){return o.GetType().GetField(name,F).GetValue(o);}
 static void Set(object o,string name,object value){o.GetType().GetField(name,F).SetValue(o,value);}
 static object New(string name,params object[] args){var t=gui.GetType("USB512_interface."+name,true);return t.GetConstructor(F,null,Array.ConvertAll(args,x=>x.GetType()),null).Invoke(args);}
 static object Call(object o,string name,params object[] args){try{return o.GetType().GetMethod(name,F).Invoke(o,args);}catch(TargetInvocationException e){throw e.InnerException??e;}}
 static Action<short[],int,short[],int,long> Apply(object filter){return (Action<short[],int,short[],int,long>)Delegate.CreateDelegate(typeof(Action<short[],int,short[],int,long>),filter,filter.GetType().GetMethod("Apply",F));}
 static void Kernel(){
  object f=New("NpDisplayHighPass",3);var apply=Apply(f);short[] source={short.MinValue,1234,short.MaxValue},target=new short[3];
  Call(f,"Configure",20000.0,0.0);apply(source,0,target,0,0);for(int c=0;c<3;c++)Check(target[c]==source[c],"Off isn't byte-exact bypass");
  Call(f,"Configure",20000.0,300.0);apply(source,0,target,0,0);for(int c=0;c<3;c++)Check(target[c]==0,"First row created a DC transient");
  for(int row=1;row<100;row++)apply(source,0,target,0,row);for(int c=0;c<3;c++)Check(target[c]==0,"Constant DC not removed");
  source[1]+=100;apply(source,0,target,0,100);Check(target[1]>0&&target[0]==0&&target[2]==0,"Sign inversion or channel-state leakage");
  source[1]+=100;apply(source,0,target,0,500);Check(target[1]==0,"Filter joined across missing rows");
  source[1]+=100;Call(f,"Reset");apply(source,0,target,0,501);Check(target[1]==0,"View reset retained old state");
  foreach(double invalid in new[]{-1.0,10000.0,Double.NaN,Double.PositiveInfinity}){try{Call(f,"Configure",20000.0,invalid);throw new Exception("Invalid cutoff accepted");}catch(ArgumentOutOfRangeException){Check(true,"cutoff boundary");}}
  foreach(var profile in new[]{new[]{500.0,10.0},new[]{625.0,1.0},new[]{15000.0,300.0},new[]{20000.0,10.0},new[]{30000.0,300.0}})
   foreach(double freq in new[]{profile[1]/10,profile[1],Math.Min(profile[1]*10,profile[0]*.4)}){
    double rate=profile[0],cutoff=profile[1];Call(f,"Configure",rate,cutoff);Call(f,"Reset");var x=new short[3];var y=new short[3];int count=(int)Math.Min(600000,Math.Ceiling(rate/freq*20));double xx=0,yy=0;
    for(int row=0;row<count;row++){x[0]=(short)Math.Round(1200+600*Math.Sin(2*Math.PI*freq*row/rate));x[1]=1000;x[2]=-1000;apply(x,0,y,0,row);if(row>count/2){xx+=(x[0]-1200.0)*(x[0]-1200.0);yy+=(double)y[0]*y[0];}}
    double k=Math.Tan(Math.PI*cutoff/rate),a=(1-k)/(1+k),b=1/(1+k),cos=Math.Cos(2*Math.PI*freq/rate);
    double expected=b*Math.Sqrt((2-2*cos)/(1+a*a-2*a*cos));Check(Math.Abs(Math.Sqrt(yy/xx)-expected)<.02,"Wrong rate/cutoff response");Check(y[1]==0&&y[2]==0,"Constant neighbouring channel leaked");
   }
 }
 static void Queue(bool lfp){
  object queue=New("NpPreviewQueue",16),sweep=New("NpSweepBuffer",16,16),filter=New("NpDisplayHighPass",64);
  int[] map=new int[64];for(int i=0;i<64;i++)map[i]=383-i;
  Call(queue,"Select",lfp,0,2,map);Call(filter,"Configure",lfp?1250.0:20000.0,100.0);Set(sweep,"HighPass",filter);
  var ap=new short[384*4];for(int row=0;row<4;row++)for(int ch=0;ch<384;ch++)ap[row*384+ch]=(short)(1000+row*(ch%30));var original=(short[])ap.Clone();
  Call(queue,"Push",ap,4,ap,4,2);int generation=(int)queue.GetType().GetProperty("Generation",F).GetValue(queue,null);Call(queue,"DrainTo",sweep,generation);
  object oracle=New("NpDisplayHighPass",64);Call(oracle,"Configure",lfp?1250.0:20000.0,100.0);var apply=Apply(oracle);var selected=new short[64];var output=new short[64];var actual=(short[])Get(sweep,"Samples");
  for(int row=0;row<4;row++){for(int c=0;c<64;c++)selected[c]=ap[row*384+map[c]];apply(selected,0,output,0,row);for(int c=0;c<64;c++)Check(actual[row*64+c]==output[c],"USB mapped channel/band doesn't use display filter");}
  for(int i=0;i<ap.Length;i++)Check(ap[i]==original[i],"Recording input modified by preview filter");
  Call(sweep,"Reset",16);var dc=new short[64];for(int c=0;c<64;c++)dc[c]=2000;Call(sweep,"AddRow",dc,0,100L);for(int c=0;c<64;c++)Check(((short[])Get(sweep,"Samples"))[(100%16)*64+c]==0,"Sweep reset carried channel state");
 }
 static void Ui(string output){
  foreach(bool np2 in new[]{false,true})using(var form=(Form)New("NeuropixelsPreviewForm",7500,7,np2,np2?12:10)){
   var box=(ComboBox)Get(form,"highPassBox");Check(box.SelectedIndex==0&&box.Items.Count==5,"USB filter default/options");
   box.SelectedIndex=4;object view=Get(form,"view");Check((double)Get(view,"HighPassHz")==300,"UI selection not published");
   if(!np2)((ComboBox)Get(form,"bandBox")).SelectedIndex=1;
   Call(form,"RenderFrame");Check((double)Get(Get(form,"displayHighPass"),"rate")== (np2?7500.0:625.0),"AP/LFP actual sample rate not used");
   Check(box.Right<((Label)Get(form,"report")).Left,"HPF control overlaps status");
   box.SelectedIndex=0;Call(form,"RenderFrame");Check(Get(Get(form,"sweep"),"HighPass")==null,"Off didn't remove USB display processing");
   box.SelectedIndex=4;Call(form,"RenderFrame");form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-32000,-32000);form.Show();Application.DoEvents();
   using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(output,np2?"np2-hpf.png":"np1-lfp-hpf.png"),ImageFormat.Png);}
   form.Close();
  }
 }
 static void Ble(){
  using(var plot=(Control)New("NpBlePreviewForm+Plot")){
   var raw=new ushort[16,3];for(int r=0;r<16;r++)for(int c=0;c<3;c++)raw[r,c]=(ushort)(1200+c*30+r*20);
   var original=(ushort[,])raw.Clone();Call(plot,"Append",raw,0UL,64000UL,65535U);
   var values=(short[,])Get(plot,"values");for(int r=0;r<16;r++)for(int c=0;c<3;c++)Check(values[r,c]==2048-raw[r,c],"BLE Off changed nominal counts");
   Call(plot,"SetHighPass",100.0);Check((int)Get(plot,"count")==0,"Filter switch kept mixed history");
   Call(plot,"Append",raw,0UL,64000UL,65531U);values=(short[,])Get(plot,"values");for(int c=0;c<3;c++){Check(values[0,c]==0,"BLE first row transient");Check(values[2,c]==0,"BLE invalid row gap wasn't reset");}
   Check((int)Get(plot,"count")==15,"BLE invented an invalid row");for(int r=0;r<16;r++)for(int c=0;c<3;c++)Check(raw[r,c]==original[r,c],"BLE RAW packet modified");
   Call(plot,"Clear",4);Call(plot,"Append",raw,1000000UL,64000UL,65535U);for(int c=0;c<3;c++)Check(((short[,])Get(plot,"values"))[0,c]==0,"BLE selection reset carried filter state");
  }
 }
 [STAThread]static int Main(string[] args){try{gui=Assembly.LoadFrom(Path.GetFullPath(args[0]));Directory.CreateDirectory(args[1]);if(args.Length>2&&args[2]=="--ui-only"){Ui(args[1]);Console.WriteLine("PASS "+checks+" offscreen preview UI checks and rendered screenshots; no hardware accessed.");return 0;}Kernel();Queue(false);Queue(true);Ui(args[1]);Ble();Console.WriteLine("PASS "+checks+" actual GUI display HPF checks: Off bypass, DC/gain/cutoff response, signs, channel independence, mapped AP/LFP queue and input immutability, UI rate/overlap, BLE RAW/invalid-gap/reset. No hardware accessed.");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
