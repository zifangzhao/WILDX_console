using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WildNpConfiguration;

namespace USB512_interface
{
    public partial class Form1
    {
        readonly Button explorationButton=new Button{Text="Explore…",AutoSize=true};
        volatile NpExplorationRun npExplorationRun;
        NpExplorationPlan npExplorationPlan;
        NpExplorationStep npExplorationStep;
        string npExplorationRoot;
        double npExplorationSeconds;
        readonly List<object> npExplorationRecords=new List<object>();
        bool NpExplorationActive {get{return npExplorationPlan!=null;}}
        string ExplorationUid(){return selectedDeviceKind==DeviceKind.NpV2?NpV2FirmwareForm.Uid(devicePath):NpV1Management.UidFromPath(devicePath);}
        Button InitializeNpExploration(){explorationButton.Click+=delegate{OpenNpExploration();};return explorationButton;}
        void OpenNpExploration()
        {
            if(!NpControlsAvailable||usbQualityRunning||String.IsNullOrWhiteSpace(saveFolder))return;
            var form=new NpExplorationForm(PrepareNpExploration,RunNpExploration,owner=>ShowNpProbeConfiguration(owner),()=>btnScope_Click(this,EventArgs.Empty));
            // A modeless tool keeps waveform windows interactive, while the
            // owner cannot change the target, profile or output path mid-scan.
            Enabled=false;form.FormClosed+=delegate{Enabled=true;Activate();UpdateStartEnabled();};form.Show(this);
        }
        async Task<NpExplorationPlan> PrepareNpExploration()
        {
            if(running||captureThread!=null||usbQualityRunning||!npRecoveryCancelled)throw new IOException("Stop the active recording first.");
            string uid=ExplorationUid();if(String.IsNullOrEmpty(uid))throw new IOException("A stable device UID is required for automatic scanning.");
            var profile=ReadNpRecordingProfile();bool np2=selectedDeviceKind==DeviceKind.NpV2;
            int mask=np2?(npV2RequestedProbeMask==0?profile.SelectedMask:(int)npV2RequestedProbeMask):npRequestedMode==null?profile.SelectedMask:npRequestedMode.ProbeMask;
            if(mask==0||(mask&~profile.AvailableMask)!=0)throw new IOException("Select available probes in Recording settings first.");
            var templates=np2?new Dictionary<int,string>(NpV2RequestsForDevice(devicePath)):
                (NpRequestsForDevice(devicePath)??new Dictionary<byte,NpConfigRequest>()).Values.ToDictionary(v=>v.Port+1,v=>v.Json);
            for(int port=1;port<=3;port++)if((mask&(1<<(port-1)))!=0&&!templates.ContainsKey(port))
                throw new IOException("Load or save one probe configuration for P"+port+" in Probe settings. The scan generates every bank from it, preserving gains and references.");
            string temp=Path.Combine(Path.GetTempPath(),"WILDX_Explore_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
            try{
                var plans=new Dictionary<int,List<NpExplorationSelection>>();
                for(int port=1;port<=3;port++)if((mask&(1<<(port-1)))!=0){
                    string input=Path.Combine(temp,"probe"+port+".json"),output=Path.Combine(temp,"probe"+port);File.WriteAllText(input,templates[port]);
                    string args="--scan-plan "+ExplorationQuote(input)+" "+ExplorationQuote(output)+" "+port;
                    if(await NpConfigurationDialog.Run(args)!=0)throw new IOException("Probe scan preparation was cancelled.");
                    plans.Add(port,NpExplorationPlan.LoadProbe(output,port,np2));
                }
                if(uid!=ExplorationUid())throw new IOException("Device changed while preparing the scan.");
                return NpExplorationPlan.Combine(uid,plans);
            }finally{
                string absolute=Path.GetFullPath(temp),parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
                if(absolute.StartsWith(parent+"WILDX_Explore_",StringComparison.OrdinalIgnoreCase)&&Directory.Exists(absolute))Directory.Delete(absolute,true);
            }
        }
        static string ExplorationQuote(string p){if(p.Contains("\""))throw new ArgumentException("Invalid path.");return "\""+p+"\"";}
        async Task<string> RunNpExploration(NpExplorationPlan plan,double seconds,Action<int,string> report,CancellationToken cancel)
        {
            if(NpExplorationActive||running||captureThread!=null||ExplorationUid()!=plan.DeviceUid)throw new IOException("The selected logger is no longer idle/current.");
            bool np2=selectedDeviceKind==DeviceKind.NpV2,oldRecovery=npAutoRecover.Checked,oldUpsample=npUpsampleAp15To20;
            string originalFolder=saveFolder,originalLabel=npSessionLabel.Text;
            var v1=NpRequestsForDevice(devicePath,true);var v1Original=new Dictionary<byte,NpConfigRequest>(v1);
            var v2=np2?NpV2RequestsForDevice(devicePath):null;var v2Original=np2?new Dictionary<int,string>(v2):null;
            var apply=np2?NpV2ApplyRequestsForDevice(devicePath,true):null;var applyOriginal=np2?new HashSet<int>(apply):null;
            uint oldV2Mask=npV2RequestedProbeMask;var oldV1Mode=npRequestedMode;
            DateTime now=DateTime.Now;string root=NpFolderNaming.CreateFresh(originalFolder,NpFolderNaming.Name(originalLabel+"_explore",now,"WILDX_explore_"+now.ToString("yyyy-MM-dd_HH-mm-ss_fff")));
            npExplorationRoot=root;npExplorationSeconds=seconds;npExplorationRecords.Clear();
            NpExplorationPlan.SaveJson(Path.Combine(root,"exploration_plan.json"),plan.Describe());
            bool scanComplete=false;string scanFailure=null;
            try{
                npExplorationPlan=plan;npAutoRecover.Checked=false;npUpsampleAp15To20=false;
                if(np2)npV2RequestedProbeMask=(uint)plan.Mask;
                else npRequestedMode=new NpCaptureRequest(plan.Mask,oldV1Mode==null?ReadNpRecordingProfile().SelectedHz:oldV1Mode.RequestedApHz);
                await NpExploration.Run(plan,seconds,async step=>{
                    if(ExplorationUid()!=plan.DeviceUid)throw new IOException("Selected device changed; scan stopped.");
                    var idle=Stopwatch.StartNew();
                    while(systemInfoActive!=0||deviceRefreshActive!=0){cancel.ThrowIfCancellationRequested();if(idle.Elapsed.TotalSeconds>30)throw new IOException("Device information operation did not finish; scan stopped.");await Task.Delay(100,cancel);}
                    cancel.ThrowIfCancellationRequested();
                    if(np2){v2.Clear();apply.Clear();foreach(var p in step.Probes){v2.Add(p.Port,p.Json);apply.Add(p.Port);}}
                    else{v1.Clear();foreach(var p in step.Probes)v1.Add((byte)(p.Port-1),new NpConfigRequest(p.Json,p.Payload));}
                    npExplorationStep=step;npExplorationRun=new NpExplorationRun(plan.Mask);
                    saveFolder=root;npSessionLabel.Text=(originalLabel.Length>24?originalLabel.Substring(0,24):originalLabel)+"_"+step.Label;
                    btnStart_Click(this,EventArgs.Empty);
                    if(!running&&captureThread==null)throw new IOException("Recording did not start; no later bank was attempted.");
                    return npExplorationRun;
                },StopCapture,report,cancel);
                scanComplete=true;return root;
            }catch(Exception ex){scanFailure=ex.Message;throw;}finally{
                // Run only returns after capture ownership retires. Restore the
                // user's host maps; original maps are reapplied on next START.
                npExplorationRun=null;npExplorationPlan=null;npExplorationStep=null;
                v1.Clear();foreach(var p in v1Original)v1.Add(p.Key,p.Value);
                if(np2){v2.Clear();foreach(var p in v2Original)v2.Add(p.Key,p.Value);apply.Clear();foreach(int p in applyOriginal)apply.Add(p);
                    foreach(var p in v2Original)if((plan.Mask&(1<<(p.Key-1)))!=0)apply.Add(p.Key);}
                saveFolder=originalFolder;npSessionLabel.Text=originalLabel;npAutoRecover.Checked=oldRecovery;npUpsampleAp15To20=oldUpsample;npRequestedMode=oldV1Mode;npV2RequestedProbeMask=oldV2Mask;
                NpExplorationPlan.SaveJson(Path.Combine(root,"exploration_manifest.json"),new{schemaVersion=1,device_uid=plan.DeviceUid,seconds_per_bank=seconds,complete=scanComplete,failure=scanFailure,records=npExplorationRecords.ToArray(),host_maps_restored=true,original_maps_apply_on_next_recording=true});
                UpdateStartEnabled();
            }
        }
        void NpExplorationAttach(NpRecordingSettings settings,string folder)
        {
            if(!NpExplorationActive)return;var step=npExplorationStep;
            settings.Context("exploration",new{root=npExplorationRoot,step=step.Index+1,label=step.Label,target_seconds=npExplorationSeconds,numbering="zero-based"});
            foreach(var p in step.Probes){
                File.WriteAllText(Path.Combine(folder,"exploration_probe"+p.Port+".npconfig.json"),p.Json);File.WriteAllText(Path.Combine(folder,"exploration_probe"+p.Port+".imro"),p.Imro);
                // The factory correction stays attached to the physical port,
                // but its companion IMRO must describe this bank, not the template.
                if(npRunCalibration!=null && npRunCalibration.Get(p.Port)!=null)File.WriteAllText(Path.Combine(folder,"calibration","probe"+p.Port+".imro"),p.Imro);
            }
            NpExplorationPlan.SaveJson(Path.Combine(folder,"exploration_step.json"),new{state="requested",step=step.Index+1,target_seconds=npExplorationSeconds,plan_file="../exploration_plan.json",label=step.Label});
        }
        void NpExplorationSamples(int probe,int count,int rate){var scan=npExplorationRun;if(scan!=null)scan.Observe(probe,count,rate);}
        void NpExplorationAdmitted(int mask){if(NpExplorationActive&&mask!=npExplorationPlan.Mask)throw new IOException("Explorer requires every selected probe; accepted probe mask changed. No later bank will be started.");}
        void NpExplorationFinished(string message)
        {
            var run=npExplorationRun;if(run==null)return;bool complete=false;string problem=message;
            try{
                var doc=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(runFolder,"recording_settings.json")));
                object value;complete=doc.TryGetValue("recording_complete",out value)&&value is bool&&(bool)value;
                var record=new{step=npExplorationStep.Index+1,label=npExplorationStep.Label,folder=runFolder,recording_complete=complete,window_completed=run.Seconds>=npExplorationSeconds,target_seconds=npExplorationSeconds,counts=run.Counts(),message};
                NpExplorationPlan.SaveJson(Path.Combine(runFolder,"exploration_step.json"),record);npExplorationRecords.Add(record);NpRecordingSettings.RefreshBundle(runFolder);
            }catch(Exception ex){complete=false;problem="Exploration metadata failed: "+ex.Message;}
            run.Done.TrySetResult(new NpExplorationResult{Complete=complete,Folder=runFolder,Message=problem});
        }
    }

    internal sealed class NpExplorationForm:Form
    {
        readonly NumericUpDown minutes=new NumericUpDown{Minimum=0.1M,Maximum=1440,DecimalPlaces=1,Increment=0.1M,Value=1,Width=80};
        readonly DataGridView table=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false};
        readonly Label status=new Label{Dock=DockStyle.Fill,AutoEllipsis=true};
        readonly Button prepare=new Button{Text="Prepare scan",AutoSize=true},start=new Button{Text="Start scan",AutoSize=true,Enabled=false},stop=new Button{Text="Stop scan",AutoSize=true,Enabled=false},settings=new Button{Text="Probe settings…",AutoSize=true};
        readonly Func<Task<NpExplorationPlan>> preparePlan;readonly Func<NpExplorationPlan,double,Action<int,string>,CancellationToken,Task<string>> execute;
        readonly Action<IWin32Window> configure;readonly Action preview;NpExplorationPlan plan;CancellationTokenSource cancel;bool busy,closeRequested;
        internal NpExplorationForm(Func<Task<NpExplorationPlan>> build,Func<NpExplorationPlan,double,Action<int,string>,CancellationToken,Task<string>> run,Action<IWin32Window> edit,Action showPreview)
        {
            preparePlan=build;execute=run;configure=edit;preview=showPreview;Text="Exploratory scan";ClientSize=new Size(840,580);MinimumSize=new Size(720,460);StartPosition=FormStartPosition.CenterParent;Icon=NpPreviewTheme.ApplicationIcon;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=5};
            foreach(int height in new[]{44,44,0,62,44})layout.RowStyles.Add(new RowStyle(height==0?SizeType.Percent:SizeType.Absolute,height==0?100:height));Controls.Add(layout);
            var timing=new FlowLayoutPanel{Dock=DockStyle.Fill};timing.Controls.AddRange(new Control[]{WildXTheme.Label("Time per bank (minutes)"),minutes,WildXTheme.Label("Starts with captured samples")});layout.Controls.Add(timing,0,0);
            var tools=new FlowLayoutPanel{Dock=DockStyle.Fill};var wave=new Button{Text="Waveform preview",AutoSize=true};wave.Click+=delegate{preview();};
            tools.Controls.AddRange(new Control[]{settings,prepare,wave});layout.Controls.Add(tools,0,1);layout.Controls.Add(table,0,2);
            foreach(string column in new[]{"Step","Probes","Shank","Sites (0-based)","New sites","Overlap","State"})table.Columns.Add(column,column);foreach(DataGridViewColumn column in table.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;table.Columns["State"].FillWeight=180;
            table.BorderStyle=BorderStyle.None;table.BackgroundColor=WildXTheme.Surface;table.GridColor=WildXTheme.Border;table.EnableHeadersVisualStyles=false;
            table.ColumnHeadersDefaultCellStyle.BackColor=WildXTheme.SurfaceSoft;table.ColumnHeadersDefaultCellStyle.ForeColor=WildXTheme.Ink;
            table.DefaultCellStyle.BackColor=WildXTheme.Surface;table.DefaultCellStyle.ForeColor=WildXTheme.Ink;table.DefaultCellStyle.SelectionBackColor=WildXTheme.AccentLight;table.DefaultCellStyle.SelectionForeColor=WildXTheme.Ink;
            table.AllowUserToResizeRows=false;table.RowTemplate.Height=26;
            status.Text="Each bank uses a separate recording. The final window overlaps to keep 384 channels connected. Original maps are restored for the next recording.";layout.Controls.Add(status,0,3);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};var close=new Button{Text="Close",AutoSize=true};close.Click+=delegate{Close();};actions.Controls.AddRange(new Control[]{start,stop,close});layout.Controls.Add(actions,0,4);
            prepare.Click+=async delegate{await Prepare();};settings.Click+=async delegate{configure(this);await Prepare();};start.Click+=async delegate{await Start();};stop.Click+=delegate{cancel.Cancel();stop.Enabled=false;};
            FormClosing+=delegate(object s,FormClosingEventArgs e){if(busy){closeRequested=true;e.Cancel=true;if(cancel!=null)cancel.Cancel();status.Text="Waiting for recording / file cleanup before closing…";}};
            Shown+=async delegate{await Prepare();};WildXTheme.Apply(this);WildXTheme.Button(start,true);
        }
        async Task Prepare(){if(busy)return;busy=true;prepare.Enabled=settings.Enabled=start.Enabled=false;plan=null;table.Rows.Clear();status.Text="Preparing bank selections…";
            try{plan=await preparePlan();foreach(var step in plan.Steps){var p=step.Probes[0];table.Rows.Add(step.Index+1,String.Join(" + ",step.Probes.Select(x=>"P"+x.Port)),p.Shank,p.FirstSite+"–"+p.LastSite,p.NewSites,p.OverlapSites,"Ready");}status.Text=plan.Steps.Length+" recordings · "+plan.DeviceUid+" · gains/references preserved. Final window may overlap.";}
            catch(Exception ex){status.Text=ex.Message;}finally{busy=false;prepare.Enabled=settings.Enabled=true;start.Enabled=plan!=null;if(closeRequested)Close();}}
        async Task Start(){if(busy||plan==null)return;busy=true;cancel=new CancellationTokenSource();prepare.Enabled=settings.Enabled=start.Enabled=minutes.Enabled=false;stop.Enabled=true;
            try{string folder=await execute(plan,(double)minutes.Value*60,(index,text)=>{table.Rows[index].Cells["State"].Value=text;table.CurrentCell=table.Rows[index].Cells[0];status.Text="Step "+(index+1)+" / "+plan.Steps.Length+" · "+text;},cancel.Token);status.Text="Scan complete · "+folder+" · Original maps apply on next recording.";}
            catch(OperationCanceledException){status.Text="Scan stopped. Saved recordings retained; original maps apply on next recording.";}
            catch(Exception ex){status.Text=ex.Message+" Saved recordings retained.";}
            finally{cancel.Dispose();cancel=null;busy=false;prepare.Enabled=settings.Enabled=minutes.Enabled=true;start.Enabled=plan!=null;stop.Enabled=false;if(closeRequested)Close();}}
    }
}
