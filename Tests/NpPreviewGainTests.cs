using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class NpPreviewGainTests {
 const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static Assembly app;static int checks;
 static object Get(object item,string name){return item.GetType().GetField(name,All).GetValue(item);}
 static object Call(object item,string name,params object[] args){try{return item.GetType().GetMethod(name,All).Invoke(item,args);}catch(TargetInvocationException ex){throw ex.InnerException;}}
 static void Need(bool value,string why){checks++;if(!value)throw new Exception(why);}
 static Form Preview(bool v2,object connection){var type=app.GetType("USB512_interface."+(v2?"NpBlePreviewForm":"NpV1BlePreviewForm"),true);return (Form)Activator.CreateInstance(type,All,null,new object[]{connection,null,(Func<bool>)(()=>true),null},null);}
 static void Append(Control plot,bool v2,int value,ulong first){
  if(v2){var raw=new ushort[32,1];for(int n=0;n<32;n++)raw[n,0]=(ushort)(2048-value);Call(plot,"Append",raw,first,1UL,0xffffffffUL);}
  else{var samples=new double[32];for(int n=0;n<32;n++)samples[n]=value;Call(plot,"Append",samples,first,1UL);}
 }
 static Bitmap Paint(Control plot){var image=new Bitmap(plot.Width,plot.Height);using(var graphics=Graphics.FromImage(image)){var args=new PaintEventArgs(graphics,plot.ClientRectangle);Call(plot,"OnPaintBackground",args);Call(plot,"OnPaint",args);}return image;}
 static bool GreenNear(Bitmap image,int x,int y){for(int delta=-1;delta<=1;delta++)if(image.GetPixel(x,y+delta).ToArgb()==Color.LightGreen.ToArgb())return true;return false;}
 static void Run(bool v2,string output){
  using(var connection=(IDisposable)Activator.CreateInstance(app.GetType("USB512_interface.NpBleConnection",true),true))
  using(var form=Preview(v2,connection)){
   var box=(NumericUpDown)Get(form,"gainBox");var plot=(Control)Get(form,"plot");double reference=v2?256:64;
   Need(box.Minimum==0.1M&&box.Maximum==100&&box.Value==1&&box.DecimalPlaces==1,"Manual gain control bounds/default incorrect");
   Need(box.AccessibleDescription.Contains("saved data"),"Display-only gain is not identified");
   plot.Dock=DockStyle.None;plot.Size=new Size(600,300);
   Need(Math.Abs((float)Call(plot,"ValueY",0.0)-150)<.01,"Zero is not fixed at center");
   Need(Math.Abs((float)Call(plot,"ValueY",reference)-30)<.01&&Math.Abs((float)Call(plot,"ValueY",-reference)-270)<.01,"1x Y bounds do not match fixed count range");
   if(v2)Call(plot,"SetSampleRate",500U);else Call(plot,"Clear",500.0);
   Call(plot,"SetWindowSeconds",0.1);Append(plot,v2,16,0);
   using(var image=Paint(plot)){int y=(int)(float)Call(plot,"ValueY",16.0);Need(GreenNear(image,500,y),"Constant signal was auto-centered or not drawn at its fixed Y value");}
   int count=(int)Get(plot,"count");var original=(Array)((Array)Get(plot,"values")).Clone();
   box.Value=2;Need((double)Get(plot,"gain")==2&&(int)Get(plot,"count")==count,"Gain UI did not update or discarded history");
   Need(StructuralComparisons.StructuralEqualityComparer.Equals(Get(plot,"values"),original),"Gain changed stored display samples");
   Need(Math.Abs((float)Call(plot,"ValueY",16.0)-(150-16*240/reference))<.01,"Gain does not scale amplitude around fixed zero");
   Append(plot,v2,400,32);Need(Math.Abs((float)Call(plot,"ValueY",16.0)-(150-16*240/reference))<.01,"Large sample changed Y scale");
   using(var image=Paint(plot)){bool clear=true;for(int x=20;x<580;x++)clear&=image.GetPixel(x,25).ToArgb()!=Color.LightGreen.ToArgb();Need(clear,"Clipped trace overwrote scale caption");image.Save(Path.Combine(output,v2?"v2-fixed-gain.png":"v1-fixed-gain.png"));}
   ((NumericUpDown)Get(form,"channel")).Value=17;((NumericUpDown)Get(form,"probe")).Value=2;((NumericUpDown)Get(form,"windowSeconds")).Value=1;
   if(!v2)((ComboBox)Get(form,"band")).SelectedIndex=1;
   Need(box.Value==2&&(double)Get(plot,"gain")==2&&((CheckBox)Get(form,"enabled")).Checked,"Selection or viewing window reset gain/paused preview");
   foreach(double bad in new[]{0.0,-1,100.1,Double.NaN,Double.PositiveInfinity}){bool refused=false;try{Call(plot,"SetGain",bad);}catch(ArgumentOutOfRangeException){refused=true;}Need(refused,"Invalid gain admitted");}
   plot.Dock=DockStyle.Fill;
   using(var host=new Form{ClientSize=new Size(850,550),Font=form.Font,BackColor=form.BackColor,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000)}){
    var controls=new List<Control>();foreach(Control control in form.Controls)controls.Add(control);host.Controls.AddRange(controls.ToArray());host.Show();Application.DoEvents();
    foreach(var size in new[]{new Size(850,550),new Size(700,450)}){host.ClientSize=size;host.PerformLayout();Application.DoEvents();Need(box.Visible&&box.Parent.ClientRectangle.Contains(box.Bounds),"Gain selector clipped at supported window size");}
    host.ClientSize=new Size(850,550);host.PerformLayout();Application.DoEvents();using(var image=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(image,new Rectangle(Point.Empty,host.Size));image.Save(Path.Combine(output,v2?"v2-gain-controls.png":"v1-gain-controls.png"));}
    host.Hide();form.Controls.AddRange(controls.ToArray());
   }
  }
 }
 [STAThread]static int Main(string[] args){try{app=Assembly.LoadFrom(Path.GetFullPath(args[0]));Directory.CreateDirectory(args[1]);Run(false,args[1]);Run(true,args[1]);Console.WriteLine("PASS "+checks+" actual-app V1/V2 BLE fixed-scale/gain/DC/clipping/history/selection checks; no hardware.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
