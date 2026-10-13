using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
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
        NpUsbRecordingProfile profile;
        readonly int requestedMask, requestedHz;
        internal int ProbeMask { get { return automatic.Checked?0:ports.Select((p,i)=>p.Checked?1<<i:0).Sum(); } }
        internal int SampleHz { get { return rate.SelectedItem==null?0:((Rate)rate.SelectedItem).Hz; } }
        internal bool Merge { get { return merge.Checked; } }
        internal bool Upsample { get { return upsample.Checked; } }
        internal bool Misc { get { return misc.Checked; } }
        sealed class Rate { internal int Hz; public override string ToString(){return Hz.ToString("N0",CultureInfo.InvariantCulture)+" Hz";} }

        internal NpRecordingDialog(bool np2,int mask,int hz,bool mergeProbes,bool resample,bool recordMisc,
            Func<NpUsbRecordingProfile> readProfile,Action<IWin32Window> configure)
        {
            read=readProfile;requestedMask=mask;requestedHz=hz;
            Text="Recording settings";ClientSize=new Size(660,530);MinimumSize=new Size(610,500);
            StartPosition=FormStartPosition.CenterParent;Icon=NpPreviewTheme.ApplicationIcon;
            var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=8,Padding=new Padding(18) };
            foreach(int height in new[]{28,40,42,28,0,88,44,46})layout.RowStyles.Add(new RowStyle(height==0?SizeType.Percent:SizeType.Absolute,height==0?100:height));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(layout);
            layout.Controls.Add(WildXTheme.Label("Connectors",true),0,0);
            var connectorRow=new FlowLayoutPanel { Dock=DockStyle.Fill };
            for(int i=0;i<3;i++){ports[i]=new CheckBox { Text="Probe "+(i+1), AutoSize=true };connectorRow.Controls.Add(ports[i]);ports[i].CheckedChanged+=delegate {UpdateSelection();};}
            automatic.Checked=mask==0;automatic.CheckedChanged+=delegate {UpdateSelection();};connectorRow.Controls.Add(automatic);layout.Controls.Add(connectorRow,0,1);
            var rateRow=new FlowLayoutPanel { Dock=DockStyle.Fill };
            rateRow.Controls.Add(WildXTheme.Label("AP sample rate"));rateRow.Controls.Add(rate);rateRow.Controls.Add(WildXTheme.Label("Continuous until Stop"));layout.Controls.Add(rateRow,0,2);
            layout.Controls.Add(WildXTheme.Label(np2?"384 AP channels per probe":"384 AP + 384 LFP channels per probe"),0,3);
            layout.Controls.Add(status,0,4);
            var options=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false };
            merge.Checked=mergeProbes;upsample.Checked=resample;upsample.Visible=!np2;misc.Checked=recordMisc;
            options.Controls.AddRange(new Control[]{merge,upsample,misc});layout.Controls.Add(options,0,5);
            var configuration=new Button { Text="Probe configuration…",AutoSize=true };
            configuration.Click+=delegate {configure(this);};layout.Controls.Add(configuration,0,6);
            var buttons=new FlowLayoutPanel { Dock=DockStyle.Fill };
            var refresh=new Button { Text="Read device status",AutoSize=true };refresh.Click+=delegate {ReadProfile();};
            var cancel=new Button { Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel };
            accept.Click+=delegate {
                if(profile==null || SampleHz==0)return;
                int effective=automatic.Checked?profile.SelectedMask:ProbeMask;
                string problem=profile.Validate(effective,SampleHz);
                if(problem!=null){status.Text=problem;return;}
                string warning=profile.Warning(effective,SampleHz);
                if(warning!=null && MessageBox.Show(this,warning,"Recording profile",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning)!=DialogResult.OK)return;
                DialogResult=DialogResult.OK;
            };
            buttons.Controls.AddRange(new Control[]{refresh,accept,cancel});layout.Controls.Add(buttons,0,7);
            rate.SelectedIndexChanged+=delegate {UpdateSelection();};
            AcceptButton=accept;CancelButton=cancel;Shown+=delegate {ReadProfile();};
            WildXTheme.Apply(this);WildXTheme.Button(accept,true);
        }
        internal void ReadProfile()
        {
            try {
                var next=read();profile=null;rate.Items.Clear();
                int selected=requestedMask==0?next.SelectedMask:requestedMask;
                for(int i=0;i<3;i++){ports[i].Checked=(selected&next.AvailableMask&(1<<i))!=0;ports[i].Enabled=(next.AvailableMask&(1<<i))!=0;}
                foreach(int hz in next.Rates)rate.Items.Add(new Rate {Hz=hz});
                int wanted=requestedHz==0?next.SelectedHz:requestedHz;
                rate.SelectedIndex=Array.IndexOf(next.Rates,wanted);
                if(rate.SelectedIndex<0 && rate.Items.Count>0)rate.SelectedIndex=0;
                profile=next;UpdateSelection();
            } catch(Exception ex){profile=null;status.Text="Device status unavailable: "+ex.Message;UpdateSelection();}
        }
        void UpdateSelection()
        {
            for(int i=0;i<3;i++)ports[i].Enabled=profile!=null && !automatic.Checked && (profile.AvailableMask&(1<<i))!=0;
            rate.Enabled=profile!=null && rate.Items.Count>0;
            int effective=profile==null?0:automatic.Checked?profile.SelectedMask:ProbeMask;
            merge.Enabled=NpCaptureMode.Count(effective)>1;
            upsample.Enabled=SampleHz==15000;
            string problem=profile==null?null:profile.Validate(effective,SampleHz);
            accept.Enabled=profile!=null && SampleHz!=0 && problem==null;
            if(profile!=null)status.Text=profile.Status+(problem==null?"":"\r\n\r\n"+problem);
        }
    }
}
