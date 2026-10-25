using System;using System.IO;using System.Reflection;using System.Collections;using System.Windows.Forms;
class Actual138Checks {
 const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static int checks;
 static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static object Call(object target,string name,params object[] args){try{return (target as Type??target.GetType()).GetMethod(name,F).Invoke(target is Type?null:target,args);}catch(TargetInvocationException e){throw e.InnerException;}}
 static object Get(object target,string name){return target.GetType().GetField(name,F).GetValue(target);}
 [STAThread]static int Main(string[] args){try{
  Application.EnableVisualStyles();var app=Assembly.LoadFrom(Path.GetFullPath(args[0]));
  Need(app.GetName().Version.ToString()=="1.0.0.138","Wrong actual product version");
  typeof(NpBleWorkflowTests).GetField("app",F).SetValue(null,app);Call(typeof(NpBleWorkflowTests),"Wire");Call(typeof(NpBleWorkflowTests),"PreviewDecode");
  typeof(NpPreviewGainTests).GetField("app",F).SetValue(null,app);Call(typeof(NpPreviewGainTests),"Run",false,args[2]);
  var data=File.ReadAllBytes(args[1]);var type=app.GetType("CE32_console.NpV1PreviewBurst",true);
  for(int band=0;band<2;band++){
   var body=new byte[224];Buffer.BlockCopy(data,band*224,body,0,224);uint id=BitConverter.ToUInt32(body,4);
   var burst=Activator.CreateInstance(type,F,null,new object[]{id},null);Need((bool)Call(burst,"Accept",body),"Actual product rejected firmware packet");
   var samples=(double[][])Call(burst,"DecodeCompact",17U);var channels=(ushort[])Get(burst,"Channels");var offsets=(uint[])Get(burst,"TickOffsets");
   for(int c=0;c<4;c++)for(int n=0;n<16;n++){
    uint ch=channels[c],adc=ch/24*2+(ch&1),slot=ch%24/2;ulong tick=(ulong)Get(burst,"FirstTick")+offsets[c]+(ulong)n*(uint)Get(burst,"Stride"),shifted=tick+4096UL*9360-17;
    Need(samples[c][n]==((adc*12+slot+shifted/780%23*17)&1023),"Actual product changed raw sample/time");
   }
   var selected=Array.ConvertAll(channels,x=>(uint)x);foreach(uint kind in new uint[]{5,3,4}){var request=(byte[])Call(type,"CompactRequest",id,kind,1U,selected,17U,7U,(uint)band);Need(request.Length==64&&BitConverter.ToUInt32(request,8)==kind&&BitConverter.ToUInt32(request,36)==4,"Actual compact lease request wrong");}
   body[60]^=1;bool rejected=false;try{Call(Activator.CreateInstance(type,F,null,new object[]{id},null),"Accept",body);}catch(IOException){rejected=true;}Need(rejected,"Actual product accepted corrupt compact packet");
  }
  Console.WriteLine("PASS actual138: "+checks+" compact firmware-vector/CRC/lease assertions, "+typeof(NpBleWorkflowTests).GetField("checks",F).GetValue(null)+" legacy wire/phase/queue assertions, "+typeof(NpPreviewGainTests).GetField("checks",F).GetValue(null)+" V1 gain/layout assertions. No hardware.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
