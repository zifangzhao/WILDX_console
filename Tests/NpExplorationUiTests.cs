using System;using System.IO;using System.Drawing;using System.Reflection;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;using USB512_interface;
class NpExplorationUiTests{
 const BindingFlags All=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;static int checks;
 static object Get(object o,string n){return o.GetType().GetField(n,All).GetValue(o);}static Task Call(object o,string n){return (Task)o.GetType().GetMethod(n,All).Invoke(o,null);}static void Need(bool b,string m){checks++;if(!b)throw new Exception(m);}
 static void Finish(Task task){var t=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted&&t.ElapsedMilliseconds<5000){Application.DoEvents();Thread.Sleep(1);}Need(task.IsCompleted,"UI callback stalled");task.GetAwaiter().GetResult();}
 [STAThread]static int Main(string[] args){try{Application.EnableVisualStyles();var probe=NpExplorationPlan.LoadProbe(args[0],2,true);var plan=NpExplorationPlan.Combine("TEST_UID",new System.Collections.Generic.Dictionary<int,System.Collections.Generic.List<NpExplorationSelection>>{{2,probe}});double duration=0;
  using(var form=new NpExplorationForm(()=>Task.FromResult(plan),async(p,seconds,progress,cancel)=>{duration=seconds;progress(0,"Recording…");await Task.Delay(10000,cancel);return "unused";},owner=>{},()=>{})){
   form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-4000,-4000);form.Show();Application.DoEvents();Finish(Call(form,"Prepare"));
   var grid=(DataGridView)Get(form,"table");Need(grid.Rows.Count==16,"UI omitted banks");Need(((NumericUpDown)Get(form,"minutes")).Value==1,"Default is not one minute");foreach(DataGridViewColumn c in grid.Columns)Need(c.SortMode==DataGridViewColumnSortMode.NotSortable,"Sorting could mislabel bank progress");
   using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(args[1]);}
   var first=Call(form,"Start");Application.DoEvents();Need(duration==60&&!((Button)Get(form,"start")).Enabled,"Start did not use60s/lock controls");
   ((Button)Get(form,"stop")).PerformClick();Finish(first);Need(((Label)Get(form,"status")).Text.StartsWith("Scan stopped"),"Stop result missing");Need(((Button)Get(form,"start")).Enabled,"Controls not restored after cancellation");
   var second=Call(form,"Start");form.Close();Finish(second);Need(form.IsDisposed,"Close did not wait for cancellation then close");
  }Console.WriteLine("PASS "+checks+" actual scan UI/default/plan/stop/close checks; fake recorder, no hardware");return 0;
 }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
