using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USB512_interface
{
    // A common presentation of capabilities; native protocol admission stays in
    // each recorder. A saved request is never an applied-device receipt.
    internal sealed class NpUsbRecordingProfile
    {
        internal int AvailableMask, SelectedMask, SelectedHz;
        internal int[] Rates;
        internal string Status;
        internal Func<int,int,string> Validate;
        internal Func<int,int,string> Warning;
        internal Action<int,int> WarningAccepted;
    }

    internal sealed class NpSdRecordingOptions
    {
        internal bool MediaAvailable, Media, Camera, Microphone;
        internal int CameraHz;
        internal Func<Task<NpUsbRecordingProfile>> Read;
        internal Func<IWin32Window,Task> Configure;
    }

    internal sealed class NpRecordingDialog : Form
    {
        readonly Func<NpUsbRecordingProfile> read;
        readonly CheckBox[] ports = new CheckBox[3];
        readonly ComboBox rate = new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList, Width=180 };
        readonly TextBox status = new TextBox { Multiline=true, ReadOnly=true, ScrollBars=ScrollBars.Vertical, Dock=DockStyle.Fill };
        readonly CheckBox automatic = new CheckBox { Text="Use device connector selection", AutoSize=true };
        readonly CheckBox merge = new CheckBox { Text="Merge active probes into one DAT per signal band", AutoSize=true };
        readonly CheckBox upsample = new CheckBox { Text="Resample 15 kHz AP output to 20 kHz", AutoSize=true };
        readonly CheckBox misc = new CheckBox { Text="Record misc signals (IMU, analog input and clock)", AutoSize=true };
        readonly Button accept = new Button { Text="Save settings", AutoSize=true, Enabled=false };
        readonly Button refresh = new Button { Text="Read device status", AutoSize=true };
        readonly ComboBox source = new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList, Width=200 };
        readonly CheckBox camera = new CheckBox { Text="Camera", AutoSize=true };
        readonly CheckBox microphone = new CheckBox { Text="Microphone (200 kHz)", AutoSize=true };
        readonly NumericUpDown cameraRate = new NumericUpDown { Minimum=1, Maximum=50, Value=40, Width=64 };
        readonly FlowLayoutPanel connectorRow,rateRow,mediaRow;
        readonly Button configuration;
        readonly Label channelInfo;
        readonly NpSdRecordingOptions sd;
        bool loading;
        NpUsbRecordingProfile profile;
        readonly int requestedMask, requestedHz;
        readonly bool np2;
        internal int ProbeMask { get { return automatic.Checked?0:ports.Select((p,i)=>p.Checked?1<<i:0).Sum(); } }
        internal int SampleHz { get { return rate.SelectedItem==null?0:((Rate)rate.SelectedItem).Hz; } }
        internal bool Merge { get { return merge.Checked; } }
        internal bool Upsample { get { return upsample.Checked; } }
        internal bool Misc { get { return misc.Checked; } }
        internal bool Media { get { return sd!=null && source.SelectedIndex==1; } }
        sealed class Rate { internal int Hz; public override string ToString(){return Hz.ToString("N0",CultureInfo.InvariantCulture)+" Hz";} }

        internal NpRecordingDialog(bool np2,int mask,int hz,bool mergeProbes,bool resample,bool recordMisc,
            Func<NpUsbRecordingProfile> readProfile,Action<IWin32Window> configure)
            :this(np2,mask,hz,mergeProbes,resample,recordMisc,readProfile,configure,null){}

        internal NpRecordingDialog(bool np2,int mask,int hz,bool mergeProbes,bool resample,bool recordMisc,
            Func<NpUsbRecordingProfile> readProfile,Action<IWin32Window> configure,NpSdRecordingOptions sd)
        {
            this.np2=np2;this.sd=sd;read=readProfile;requestedMask=mask;requestedHz=hz;
            Text="Recording settings";ClientSize=new Size(660,530);MinimumSize=new Size(610,500);
            StartPosition=FormStartPosition.CenterParent;Icon=NpPreviewTheme.ApplicationIcon;
            var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(18) };
            foreach(int height in new[]{36,40,42,28,0,sd==null?88:0,44,46})layout.RowStyles.Add(new RowStyle(height==0?SizeType.Percent:SizeType.Absolute,height==0?100:height));
            if(sd!=null){layout.RowStyles[5].SizeType=SizeType.Absolute;layout.RowStyles[5].Height=0;}
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(layout);
            var heading=new FlowLayoutPanel {Dock=DockStyle.Fill};heading.Controls.Add(WildXTheme.Label(sd!=null&&sd.MediaAvailable?"Recording source":"Connectors",true));
            source.Items.AddRange(new object[]{"Neuropixels","Camera / Microphone"});source.SelectedIndex=sd!=null&&sd.Media?1:0;
            source.Visible=sd!=null&&sd.MediaAvailable;heading.Controls.Add(source);layout.Controls.Add(heading,0,0);
            connectorRow=new FlowLayoutPanel { Dock=DockStyle.Fill };
            for(int i=0;i<3;i++){ports[i]=new CheckBox { Text="Probe "+(i+1), AutoSize=true };connectorRow.Controls.Add(ports[i]);ports[i].CheckedChanged+=delegate {UpdateSelection();};}
            automatic.Visible=np2&&sd==null;automatic.Checked=np2 && mask==0 && sd==null;automatic.CheckedChanged+=delegate {UpdateSelection();};connectorRow.Controls.Add(automatic);layout.Controls.Add(connectorRow,0,1);
            var streamRow=new Panel {Dock=DockStyle.Fill};layout.Controls.Add(streamRow,0,2);
            rateRow=new FlowLayoutPanel { Dock=DockStyle.Fill };
            rateRow.Controls.Add(WildXTheme.Label("AP sample rate"));rateRow.Controls.Add(rate);rateRow.Controls.Add(WildXTheme.Label("Continuous until Stop"));streamRow.Controls.Add(rateRow);
            mediaRow=new FlowLayoutPanel {Dock=DockStyle.Fill};
            camera.Checked=sd!=null&&sd.Camera;microphone.Checked=sd!=null&&sd.Microphone;if(sd!=null)cameraRate.Value=Math.Max(1,Math.Min(50,sd.CameraHz));
            mediaRow.Controls.AddRange(new Control[]{camera,cameraRate,WildXTheme.Label("fps"),microphone});streamRow.Controls.Add(mediaRow);
            channelInfo=WildXTheme.Label("");layout.Controls.Add(channelInfo,0,3);
            layout.Controls.Add(status,0,4);
            var options=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false };
            merge.Checked=mergeProbes;upsample.Checked=resample;upsample.Visible=!np2;misc.Checked=recordMisc;
            if(sd==null)options.Controls.AddRange(new Control[]{merge,upsample,misc});
            layout.Controls.Add(options,0,5);
            configuration=new Button { Text="Probe configuration…",AutoSize=true };
            configuration.Click+=async delegate {
                string error=null;
                try {loading=true;accept.Enabled=refresh.Enabled=configuration.Enabled=false;if(sd!=null)await sd.Configure(this);else configure(this);}
                catch(Exception ex){error=ex.Message;}
                finally {loading=false;refresh.Enabled=configuration.Enabled=true;UpdateSelection();}
                if(error!=null)status.Text=error;
            };layout.Controls.Add(configuration,0,6);
            var buttons=new FlowLayoutPanel { Dock=DockStyle.Fill };
            refresh.Click+=async delegate {await RefreshProfile();};
            var cancel=new Button { Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel };
            accept.Click+=delegate {
                if(profile==null || !Media && SampleHz==0)return;
                if(Media){if(!camera.Checked&&!microphone.Checked)return;SaveSdOptions();DialogResult=DialogResult.OK;return;}
                int effective=automatic.Checked?profile.SelectedMask:ProbeMask;
                string problem=profile.Validate(effective,SampleHz);
                if(problem!=null){status.Text=problem;return;}
                string warning=profile.Warning(effective,SampleHz);
                if(warning!=null){
                    if(MessageBox.Show(this,warning,"Recording profile",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
                    profile.WarningAccepted?.Invoke(effective,SampleHz);
                }
                SaveSdOptions();DialogResult=DialogResult.OK;
            };
            buttons.Controls.AddRange(new Control[]{refresh,accept,cancel});layout.Controls.Add(buttons,0,7);
            rate.SelectedIndexChanged+=delegate {UpdateSelection();};
            source.SelectedIndexChanged+=delegate {UpdateSelection();};camera.CheckedChanged+=delegate {UpdateSelection();};microphone.CheckedChanged+=delegate {UpdateSelection();};
            AcceptButton=accept;CancelButton=cancel;Shown+=async delegate {await RefreshProfile();};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(loading)e.Cancel=true;};
            WildXTheme.Apply(this);WildXTheme.Button(accept,true);UpdateSelection();
        }
        internal void ReadProfile()
        {
            try {AdoptProfile(read());}
            catch(Exception ex){profile=null;status.Text="Device status unavailable: "+ex.Message;UpdateSelection();}
        }
        async Task RefreshProfile()
        {
            if(sd==null){ReadProfile();return;}
            loading=true;profile=null;UpdateSelection();refresh.Enabled=configuration.Enabled=false;
            try {AdoptProfile(await sd.Read());}
            catch(Exception ex){profile=null;status.Text="Device status unavailable: "+ex.Message;UpdateSelection();}
            finally {loading=false;refresh.Enabled=configuration.Enabled=true;UpdateSelection();}
        }
        void SaveSdOptions(){if(sd==null)return;sd.Media=Media;sd.Camera=camera.Checked;sd.Microphone=microphone.Checked;sd.CameraHz=(int)cameraRate.Value;}
        void AdoptProfile(NpUsbRecordingProfile next)
        {
                profile=null;rate.Items.Clear();
                if(!np2)automatic.Checked=sd==null&&next.AvailableMask==0;
                int selected=requestedMask==0?next.SelectedMask:requestedMask;
                for(int i=0;i<3;i++){ports[i].Checked=(selected&next.AvailableMask&(1<<i))!=0;ports[i].Enabled=(next.AvailableMask&(1<<i))!=0;}
                foreach(int hz in next.Rates)rate.Items.Add(new Rate {Hz=hz});
                int wanted=requestedHz==0?next.SelectedHz:requestedHz;
                rate.SelectedIndex=Array.IndexOf(next.Rates,wanted);
                if(rate.SelectedIndex<0 && rate.Items.Count>0)rate.SelectedIndex=0;
                profile=next;UpdateSelection();
        }
        void UpdateSelection()
        {
            if(connectorRow==null||rateRow==null||mediaRow==null||configuration==null)return;
            connectorRow.Visible=rateRow.Visible=configuration.Visible=!Media;mediaRow.Visible=Media;
            channelInfo.Text=Media?"Continuous until Stop":np2?"384 AP channels per probe":"384 AP + 384 LFP channels per probe";
            cameraRate.Enabled=camera.Checked;
            for(int i=0;i<3;i++)ports[i].Enabled=profile!=null && !automatic.Checked && (profile.AvailableMask&(1<<i))!=0;
            rate.Enabled=profile!=null && rate.Items.Count>0;
            int effective=profile==null?0:automatic.Checked?profile.SelectedMask:ProbeMask;
            merge.Enabled=NpCaptureMode.Count(effective)>1;
            upsample.Enabled=SampleHz==15000;
            string problem=profile==null?null:Media?(!camera.Checked&&!microphone.Checked?"Select camera or microphone.":null):profile.Validate(effective,SampleHz);
            accept.Enabled=!loading && profile!=null && (Media||SampleHz!=0) && problem==null;
            if(profile!=null)status.Text=profile.Status+(problem==null?"":"\r\n\r\n"+problem);
        }
    }
}
