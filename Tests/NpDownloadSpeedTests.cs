using System;using System.Globalization;using USB512_interface;
class NpDownloadSpeedTests{
 static int checks;static void Need(bool v,string why){checks++;if(!v)throw new Exception(why);}
 static int Main(string[] args){try{
  Func<long,double,string> rate=NpProgressEstimate.TransferRate;
  if(args.Length!=0){
   var app=System.Reflection.Assembly.LoadFrom(System.IO.Path.GetFullPath(args[0]));var flags=System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance;
   rate=(Func<long,double,string>)Delegate.CreateDelegate(typeof(Func<long,double,string>),app.GetType("USB512_interface.NpProgressEstimate",true).GetMethod("TransferRate",flags));
   var type=app.GetType("CE32_console.NpDownloadStream",true);var counter=System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);var property=type.GetProperty("DownloadedBytes",flags);
   Need((long)property.GetValue(counter,null)==0,"New compiled counter is not zero");type.GetField("downloadedBytes",flags).SetValue(counter,8589934592L);Need((long)property.GetValue(counter,null)==8589934592L,"Compiled counter truncates large files");type.GetMethod("CountDownloadedPayload",flags).Invoke(counter,new object[]{522240});Need((long)property.GetValue(counter,null)==8590456832L,"Compiled payload counter did not increment");
  }
  foreach(double t in new[]{0,0.1,-1,Double.NaN,Double.PositiveInfinity})Need(rate(100,t)=="Measuring speed…","Invalid/early elapsed time shown as speed");
  Need(rate(0,10)=="Measuring speed…","No-byte transfer has an invented speed");Need(rate(-1,10)=="Measuring speed…","Counter reset shown as negative speed");
  bool oldSawtooth=false;double prior=0;long total=100L*1024*1024*1024;
  for(int t=1;t<=240;t++){
   long bytes=43000000L*t;Need(rate(bytes,t)=="43.0 MB/s","Constant43MB/s did not stay constant");
   int percent=(int)(99.0*bytes/total);double old=total*percent/100.0/t/1e6;if(old<prior)oldSawtooth=true;prior=old;
  }Need(oldSawtooth,"Fixture did not exercise the old percentage sawtooth");
  Need(rate(8589934592L,200)=="42.9 MB/s","Large byte counter lost precision");
  Need(rate(430000000,20)=="21.5 MB/s","Real stall was hidden");
  var oldCulture=CultureInfo.CurrentCulture;try{CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("de-DE");Need(rate(43000000,1)=="43.0 MB/s","Rate formatting changed with locale");}finally{CultureInfo.CurrentCulture=oldCulture;}
  Console.WriteLine("PASS "+checks+" actual-byte rate/startup/stall/large-file checks; no hardware");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
