using System;
using System.Reflection;
static class NpDownloadSourceLabelTests {
 static int Main(string[] args){try {
  var app=Assembly.LoadFrom(args[0]);var method=app.GetType("CE32_console.NpDownloadForm",true).GetMethod("UsbSourceLabel",BindingFlags.NonPublic|BindingFlags.Static);
  string root=@"\\?\usb#vid_1209&pid_b386#",tail="#{8f5a4a7b-0aa6-4f8e-a14f-a2f7b3c6aa4e}";
  var examples=new[]{
   Tuple.Create(root+"npv1-00190027434250062039314e"+tail,"Neuropixels V1 USB — 00190027434250062039314E"),
   Tuple.Create((root+"npv1-00190027434250062039314e"+tail).ToUpperInvariant(),"Neuropixels V1 USB — 00190027434250062039314E"),
   Tuple.Create(root+"001a0004434250062039314e"+tail,"Neuropixels V2 USB — 001A0004434250062039314E"),
   Tuple.Create(root+"npv2-001a0004434250062039314e"+tail,"Neuropixels V2 USB — 001A0004434250062039314E"),
   Tuple.Create(root+"unrecognized"+tail,"Neuropixels USB — version unknown — "+root+"unrecognized"+tail)};
  foreach(var example in examples)if((string)method.Invoke(null,new object[]{example.Item1})!=example.Item2)throw new Exception("Wrong USB source generation/UID label: "+example.Item1);
  Console.WriteLine("PASS5 actual-app USB source labels: V1, case-insensitive identity, legacy/prefixed V2 and unknown; no enumeration or device access.");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
