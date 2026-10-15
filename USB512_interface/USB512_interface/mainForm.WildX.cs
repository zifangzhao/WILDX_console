using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace USB512_interface
{
    public partial class Form1
    {
        TabControl workspaceTabs;
        TabPage usbPage,blePage,downloadPage,advancedPage;
        CE32_console.NpDownloadForm downloadWorkspace;
        NpBleForm bleWorkspace;
        Label workspaceStatus;
        Label advancedTarget,advancedStorageSource;
        Button diagnosticsButton,captureReportButton;
        string lastCaptureReport;
        Label recordingSummary,usbHealthSummary;
        string recordingSummaryFolder;
        DateTime recordingSummaryStartUtc=DateTime.MinValue,recordingSummaryStopUtc=DateTime.MinValue;
        readonly TextBox npSessionLabel=new TextBox {Name="npSessionLabel",Dock=DockStyle.Fill,MaxLength=80,AccessibleName="Session label"};
        bool NpControlsAvailable {get{return (selectedDeviceKind==DeviceKind.XeNp || selectedDeviceKind==DeviceKind.NpV2) && !String.IsNullOrWhiteSpace(devicePath) &&
            !running && !ztestRunning && systemInfoActive==0 && deviceRefreshActive==0 && !firmwareManagementActive && npRecoveryCancelled && !DownloadBusy && !BleInUse;}}
        bool DownloadBusy { get { return downloadWorkspace!=null && downloadWorkspace.IsBusy; } }
        bool BleInUse { get { return bleWorkspace!=null && bleWorkspace.SessionOwned; } }
        void InitializeWildX()
        {
            Text = "WILD-X Neuropixels Console v" + GetPackageVersion();
            label1.Text = "USB device";
            btnStreams.Text = "Recording settings";
            btnDfu.Text = "Firmware Update";
            btnScope.Text = "NP Preview";
            btnPreview.Text = "Media preview…";
            btnImpedance.Visible = false;
            BuildWildXWorkspace();
        }

        static FlowLayoutPanel ActionRow(params Control[] controls)
        {
            var row=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=true,Margin=Padding.Empty,BackColor=WildXTheme.Surface };
            foreach(var control in controls){control.Dock=DockStyle.None;control.Anchor=AnchorStyles.Top|AnchorStyles.Left;control.Margin=new Padding(0,0,8,8);
                if(control is Button){var button=(Button)control;if(button.FlatStyle!=FlatStyle.Flat)WildXTheme.Button(button);control.AutoSize=true;button.AutoSizeMode=AutoSizeMode.GrowAndShrink;control.MinimumSize=new Size(0,36);control.Height=36;}
                row.Controls.Add(control);
            }
            return row;
        }
        static TableLayoutPanel Stack(params int[] heights)
        {
            var stack=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=heights.Length,Margin=Padding.Empty,BackColor=WildXTheme.Surface };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(int height in heights)stack.RowStyles.Add(height==0?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,height));
            return stack;
        }
        static WildXCard Card(string title,string description,Control content,bool compact=false)
        {
            var card=new WildXCard { Dock=DockStyle.Fill };
            if(compact){card.Padding=new Padding(10);card.Margin=new Padding(0,0,8,8);}
            var layout=description==null?Stack(compact?20:28,0):Stack(compact?20:28,compact?24:32,0);
            var heading=WildXTheme.Label(title,true);if(compact)heading.Margin=new Padding(0,0,0,4);layout.Controls.Add(heading,0,0);
            if(description!=null)layout.Controls.Add(new Label { Text=description,Dock=DockStyle.Fill,ForeColor=WildXTheme.Muted },0,1);
            content.Dock=DockStyle.Fill;layout.Controls.Add(content,0,description==null?1:2);card.Controls.Add(layout);return card;
        }
        void BuildWildXWorkspace()
        {
            SuspendLayout();
            AutoScaleMode=AutoScaleMode.None;
            WildXTheme.Apply(this);
            var oldInfo=systemInfoSummary.Parent;
            var infoDetails=oldInfo.Controls.OfType<Button>().First(b=>b!=btnSystemInfoRefresh);
            var oldTime=Controls.OfType<GroupBox>().First(g=>g.Text.StartsWith("Secondary network time"));
            var timeStatus=oldTime.Controls.OfType<Label>().Single();
            var timeSettings=oldTime.Controls.OfType<Button>().Single();
            Controls.Clear();
            Text="WILD-X Neuropixels Console v"+GetPackageVersion();
            BackColor=WildXTheme.Canvas;Font=WildXTheme.Body;ClientSize=new Size(1180,920);
            MinimumSize=new Size(1060,780);StartPosition=FormStartPosition.CenterScreen;MaximizeBox=true;

            var shell=Stack(52,0,24);shell.BackColor=WildXTheme.Canvas;shell.Padding=new Padding(24,0,24,0);Controls.Add(shell);
            var brand=new Panel { Dock=DockStyle.Fill,BackColor=WildXTheme.Canvas };
            brand.Controls.Add(new Label { Text="WILD-X",AutoSize=true,Font=WildXTheme.Title,ForeColor=WildXTheme.Ink,Location=new Point(0,4) });
            shell.Controls.Add(brand,0,0);

            workspaceTabs=new TabControl { Name="workspaceTabs",Dock=DockStyle.Fill,DrawMode=TabDrawMode.OwnerDrawFixed,SizeMode=TabSizeMode.Fixed,
                ItemSize=new Size(180,40),Padding=new Point(18,8),Margin=Padding.Empty };
            usbPage=new TabPage("USB Mode") { Name="usbPage",BackColor=WildXTheme.Canvas,Padding=new Padding(12),UseVisualStyleBackColor=false };
            blePage=new TabPage("BLE Mode") { Name="blePage",BackColor=WildXTheme.Canvas,Padding=new Padding(16),UseVisualStyleBackColor=false };
            downloadPage=new TabPage("Data Download") { Name="downloadPage",BackColor=WildXTheme.Canvas,Padding=new Padding(16),UseVisualStyleBackColor=false };
            advancedPage=new TabPage("Advanced") { Name="advancedPage",BackColor=WildXTheme.Canvas,Padding=new Padding(16),UseVisualStyleBackColor=false };
            workspaceTabs.TabPages.AddRange(new[]{usbPage,blePage,downloadPage,advancedPage});workspaceTabs.DrawItem+=DrawWorkspaceTab;
            workspaceTabs.Selecting+=delegate(object sender,TabControlCancelEventArgs e){
                if(running || ztestRunning || !npRecoveryCancelled || systemInfoActive!=0 || deviceRefreshActive!=0 || firmwareManagementActive || DownloadBusy){
                    e.Cancel=true;workspaceStatus.Text="Wait for the active operation to finish before changing workspace.";
                }
            };
            workspaceTabs.SelectedIndexChanged+=delegate { if(downloadWorkspace!=null)downloadWorkspace.SetUsbSelection(devicePath);workspaceStatus.Text=workspaceTabs.SelectedTab==usbPage?
                "USB MODE  /  Live acquisition, previews and device health":workspaceTabs.SelectedTab==blePage?
                "BLE MODE  /  Wireless connection and onboard SD recording":workspaceTabs.SelectedTab==downloadPage?
                "DATA DOWNLOAD  /  Saved recordings, downloads and media export":"ADVANCED  /  Manual firmware, USB modes and storage maintenance";UpdateWildXControls(); };
            shell.Controls.Add(workspaceTabs,0,1);
            workspaceStatus=WildXTheme.Label("USB MODE  /  Live acquisition, previews and device health");workspaceStatus.Dock=DockStyle.Fill;shell.Controls.Add(workspaceStatus,0,2);

            var online=new TableLayoutPanel { Name="usbLayout",Dock=DockStyle.Top,Height=650,ColumnCount=2,RowCount=3,Margin=Padding.Empty,BackColor=WildXTheme.Canvas };
            online.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));online.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));
            online.RowStyles.Add(new RowStyle(SizeType.Absolute,276));online.RowStyles.Add(new RowStyle(SizeType.Absolute,150));online.RowStyles.Add(new RowStyle(SizeType.Percent,100));usbPage.Controls.Add(online);
            usbPage.AutoScroll=true;

            var capture=Stack(36,36,30,44,24,36);
            label1.AutoSize=false;label1.Text="USB device";label1.ForeColor=WildXTheme.Muted;label1.Dock=DockStyle.Fill;label1.TextAlign=ContentAlignment.MiddleLeft;label1.Margin=Padding.Empty;
            var deviceRow=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty };
            deviceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));
            deviceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));deviceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,102));
            comboDrives.Dock=DockStyle.None;comboDrives.Anchor=AnchorStyles.Left|AnchorStyles.Right;comboDrives.Margin=new Padding(0,0,10,0);btnRefresh.Dock=DockStyle.Fill;btnRefresh.Margin=Padding.Empty;
            deviceRow.Controls.Add(label1,0,0);deviceRow.Controls.Add(comboDrives,1,0);deviceRow.Controls.Add(btnRefresh,2,0);capture.Controls.Add(deviceRow,0,0);
            label2.Text="Output folder";label2.ForeColor=WildXTheme.Muted;label2.AutoSize=false;label2.Dock=DockStyle.Fill;label2.TextAlign=ContentAlignment.MiddleLeft;label2.Margin=Padding.Empty;
            var folderRow=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty };
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));
            folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,132));
            llblSaveFolder.AutoSize=false;llblSaveFolder.Dock=DockStyle.Fill;llblSaveFolder.TextAlign=ContentAlignment.MiddleLeft;llblSaveFolder.AutoEllipsis=true;llblSaveFolder.Margin=Padding.Empty;
            llblSaveFolder.LinkColor=WildXTheme.Accent;btnSaveFolder.Dock=DockStyle.Fill;btnSaveFolder.Margin=Padding.Empty;
            llblSaveFolder.Text="Choose a destination folder";
            folderRow.Controls.Add(label2,0,0);folderRow.Controls.Add(llblSaveFolder,1,0);folderRow.Controls.Add(btnSaveFolder,2,0);capture.Controls.Add(folderRow,0,1);
            var nameRow=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
            nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            nameRow.Controls.Add(new Label {Text="Session label",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=WildXTheme.Muted,Margin=Padding.Empty},0,0);
            nameRow.Controls.Add(npSessionLabel,1,0);npSessionLabel.Dock=DockStyle.None;npSessionLabel.Anchor=AnchorStyles.Left|AnchorStyles.Right;npSessionLabel.Margin=Padding.Empty;WildXTheme.Apply(npSessionLabel);capture.Controls.Add(nameRow,0,2);
            btnStart.Text="Start recording";btnStop.Text="Stop recording";btnStreams.Text="Recording settings";
            calibrationButton=new Button {Text="Calibration…",AutoSize=true};
            calibrationButton.Click+=delegate {if(!running && (selectedDeviceKind==DeviceKind.XeNp || selectedDeviceKind==DeviceKind.NpV2))NpCalibrationStore.Show(this,devicePath,selectedDeviceKind==DeviceKind.NpV2);};
            WildXTheme.Button(btnStart,true);WildXTheme.Button(btnStop,false,true);
            var captureActions=ActionRow(btnStart,btnStop,btnStreams,calibrationButton);captureActions.Name="usbAcquisitionActions";capture.Controls.Add(captureActions,0,3);
            npAutoRecover.AutoSize=true;npAutoRecover.Margin=Padding.Empty;capture.Controls.Add(npAutoRecover,0,4);
            capture.Controls.Add(BuildGpioRtcControls(),0,5);
            var acquisitionCard=Card("USB acquisition",null,capture,true);acquisitionCard.Name="usbAcquisitionCard";capture.Dock=DockStyle.Top;online.Controls.Add(acquisitionCard,0,0);

            var monitor=Stack(40,0);
            monitor.Controls.Add(ActionRow(btnScope,btnPreview,btnNpLogger),0,0);
            imuTelemetry.Dock=DockStyle.Fill;imuTelemetry.Margin=Padding.Empty;imuTelemetry.BackColor=WildXTheme.Dark;monitor.Controls.Add(imuTelemetry,0,1);
            online.Controls.Add(Card("Live monitor",null,monitor,true),0,2);
            var health=Stack(40,0);health.Controls.Add(ActionRow(btnSystemInfoRefresh,infoDetails,btnSdInfo),0,0);
            systemInfoSummary.Dock=DockStyle.Fill;systemInfoSummary.Margin=Padding.Empty;systemInfoSummary.BorderStyle=BorderStyle.None;health.Controls.Add(systemInfoSummary,0,1);
            var healthAndGpio=Stack(176,232,0);
            healthAndGpio.Controls.Add(Card("Device health",null,health,true),0,0);
            healthAndGpio.Controls.Add(BuildGpioRtcInfo(),0,1);online.Controls.Add(healthAndGpio,1,0);online.SetRowSpan(healthAndGpio,3);

            var activity=Stack(0,36,0,0);
            activity.RowStyles[0].SizeType=activity.RowStyles[2].SizeType=activity.RowStyles[3].SizeType=SizeType.Absolute;
            recordingSummary=new Label {Name="recordingSummary",Dock=DockStyle.Fill,Font=WildXTheme.Strong,UseMnemonic=false,Margin=Padding.Empty};
            activity.Controls.Add(recordingSummary,0,0);
            lblSpeed.AutoSize=true;lblSpeed.Font=WildXTheme.Strong;lblSpeed.Text="USB idle";
            captureReportButton=new Button {Text="Report…",AutoSize=true,Enabled=false};
            captureReportButton.Click+=delegate {if(lastCaptureReport!=null)using(var report=new NpReportForm("Capture report",lastCaptureReport))report.ShowDialog(this);};
            var usbDetails=new Button {Name="usbHealthDetails",Text="Details…",AutoSize=true};
            usbDetails.Click+=delegate {using(var report=new NpReportForm("USB transfer health",CurrentUsbHealthReport))report.ShowDialog(this);};
            usbQualityTestButton=new Button {Name="usbQualityTest",Text="USB quality test…",AutoSize=true};usbQualityTestButton.Click+=async delegate {await TestUsbQualityAsync();};
            usbQualityContinuous=new CheckBox {Name="usbQualityContinuous",Text="Continuous",Checked=true,AutoSize=true};
            activity.Controls.Add(ActionRow(lblSpeed,captureReportButton),0,1);
            lblRecInfo.AutoSize=false;lblRecInfo.Dock=DockStyle.Fill;lblRecInfo.AutoEllipsis=false;lblRecInfo.UseMnemonic=false;lblRecInfo.Margin=Padding.Empty;lblRecInfo.Text="Choose a USB device and an output folder to begin.";activity.Controls.Add(lblRecInfo,0,2);
            llblSavefile.AutoSize=false;llblSavefile.Dock=DockStyle.Fill;llblSavefile.AutoEllipsis=false;llblSavefile.UseMnemonic=false;llblSavefile.Margin=Padding.Empty;llblSavefile.LinkColor=WildXTheme.Accent;activity.Controls.Add(llblSavefile,0,3);
            var usbInfo=Stack(36,24,0);usbInfo.RowStyles[2].SizeType=SizeType.Absolute;
            usbInfo.Controls.Add(ActionRow(usbQualityTestButton,usbDetails),0,0);
            usbQualityContinuous.Margin=Padding.Empty;usbInfo.Controls.Add(usbQualityContinuous,0,1);
            usbHealthSummary=new Label {Name="usbHealthSummary",Dock=DockStyle.Fill,UseMnemonic=false,Margin=Padding.Empty,ForeColor=WildXTheme.Muted};usbInfo.Controls.Add(usbHealthSummary,0,2);
            var usbCard=Card("USB",null,usbInfo,true);usbCard.Name="usbHealthCard";
            var activityCard=Card("Capture activity",null,activity,true);activityCard.Name="captureActivityCard";
            var activitySplit=new TableLayoutPanel {Name="captureActivitySplit",Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
            activitySplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));activitySplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));
            activitySplit.Controls.Add(activityCard,0,0);activitySplit.Controls.Add(usbCard,1,0);online.Controls.Add(activitySplit,0,1);
            bool arrangingActivity=false;
            Action arrangeActivity=delegate {
                if(arrangingActivity || activity.ClientSize.Width<=0)return;
                arrangingActivity=true;
                try {
                    foreach(var label in new Label[]{recordingSummary,lblRecInfo,llblSavefile}) {
                        int width=Math.Max(1,activity.ClientSize.Width-label.Margin.Horizontal);
                        int height=String.IsNullOrEmpty(label.Text)?0:label.GetPreferredSize(new Size(width,0)).Height+label.Margin.Vertical;
                        var row=activity.RowStyles[activity.GetRow(label)];
                        if(row.Height!=height)row.Height=height;
                    }
                    var layout=(TableLayoutPanel)activityCard.Controls[0];
                    int heightNeeded=(int)(layout.RowStyles[0].Height+activity.RowStyles.Cast<RowStyle>().Sum(row=>row.Height))+
                        activityCard.Padding.Vertical+activityCard.Margin.Vertical;
                    float scale=activity.RowStyles[1].Height/36F;
                    capture.RowStyles[3].Height=Math.Max(40*scale,captureActions.GetPreferredSize(new Size(capture.ClientSize.Width,0)).Height);
                    capture.RowStyles[4].Height=npAutoRecover.Visible?npAutoRecover.GetPreferredSize(Size.Empty).Height+npAutoRecover.Margin.Vertical:0;
                    var acquisitionLayout=(TableLayoutPanel)acquisitionCard.Controls[0];
                    int captureHeight=(int)Math.Ceiling(acquisitionLayout.RowStyles[0].Height+capture.RowStyles.Cast<RowStyle>().Sum(row=>row.Height))+
                        acquisitionCard.Padding.Vertical+acquisitionCard.Margin.Vertical;
                    capture.Height=(int)Math.Ceiling(capture.RowStyles.Cast<RowStyle>().Sum(row=>row.Height));
                    usbInfo.RowStyles[0].Height=Math.Max(36*scale,usbQualityTestButton.Parent.GetPreferredSize(new Size(usbInfo.ClientSize.Width,0)).Height);
                    usbInfo.RowStyles[1].Height=Math.Max(24*scale,usbQualityContinuous.GetPreferredSize(Size.Empty).Height);
                    usbInfo.RowStyles[2].Height=usbHealthSummary.GetPreferredSize(new Size(Math.Max(1,usbInfo.ClientSize.Width),0)).Height;
                    var usbLayout=(TableLayoutPanel)usbCard.Controls[0];
                    int usbHeight=(int)Math.Ceiling(usbLayout.RowStyles[0].Height+usbInfo.RowStyles.Cast<RowStyle>().Sum(row=>row.Height))+usbCard.Padding.Vertical+usbCard.Margin.Vertical;
                    int minimumActivityHeight=(int)Math.Ceiling(120*scale);
                    int activityHeight=Math.Max(minimumActivityHeight,Math.Max(heightNeeded,usbHeight));
                    int upperHeight=Math.Max(captureHeight+activityHeight,(int)Math.Ceiling(408*scale));
                    online.RowStyles[0].Height=upperHeight-activityHeight;
                    online.RowStyles[1].Height=activityHeight;
                    healthAndGpio.RowStyles[0].Height=176*scale;
                    healthAndGpio.RowStyles[1].Height=upperHeight-healthAndGpio.RowStyles[0].Height;
                    online.Height=Math.Max(upperHeight+(int)Math.Ceiling(224*scale),usbPage.ClientSize.Height-usbPage.Padding.Vertical);
                } finally {arrangingActivity=false;}
            };
            lblRecInfo.TextChanged+=delegate {arrangeActivity();};llblSavefile.TextChanged+=delegate {arrangeActivity();};
            recordingSummary.TextChanged+=delegate {arrangeActivity();};recordingSummary.FontChanged+=delegate {arrangeActivity();};
            usbHealthSummary.TextChanged+=delegate {arrangeActivity();};usbHealthSummary.FontChanged+=delegate {arrangeActivity();};
            usbInfo.SizeChanged+=delegate {arrangeActivity();};
            lblRecInfo.FontChanged+=delegate {arrangeActivity();};llblSavefile.FontChanged+=delegate {arrangeActivity();};
            activity.SizeChanged+=delegate {arrangeActivity();};capture.SizeChanged+=delegate {arrangeActivity();};
            captureActions.Layout+=delegate {arrangeActivity();};npAutoRecover.VisibleChanged+=delegate {arrangeActivity();};
            usbPage.Resize+=delegate {arrangeActivity();};arrangeActivity();
            var recordingSummaryTimer=new Timer {Interval=1000};components.Add(recordingSummaryTimer);
            recordingSummaryTimer.Tick+=delegate {RefreshRecordingSummary(DateTime.UtcNow);PollGpioRtcRecording();};
            RefreshRecordingSummary(DateTime.UtcNow);recordingSummaryTimer.Start();
            var clock=Stack(54,40,0);timeStatus.Dock=DockStyle.Fill;timeStatus.AutoEllipsis=true;clock.Controls.Add(timeStatus,0,0);clock.Controls.Add(ActionRow(timeSettings),0,1);
            clock.Controls.Add(new Label {Dock=DockStyle.Fill,ForeColor=WildXTheme.Muted,Text="Use Sync PC time in BLE Mode to set the recorder clock."},0,2);
            var networkTimeCard=Card("Network time / Local",null,clock,true);networkTimeCard.Name="networkTimeCard";
            healthAndGpio.Controls.Add(networkTimeCard,0,2);

            bleWorkspace=new NpBleForm() {TopLevel=false,FormBorderStyle=FormBorderStyle.None,Dock=DockStyle.Fill,MinimumSize=Size.Empty};
            bleWorkspace.StateChanged+=delegate {if(!Disposing && !IsDisposed)UpdateStartEnabled();};
            blePage.Controls.Add(Card("BLE Control",null,bleWorkspace));bleWorkspace.Show();

            downloadWorkspace=new CE32_console.NpDownloadForm(null,false) { TopLevel=false,FormBorderStyle=FormBorderStyle.None,Dock=DockStyle.Fill };
            npSessionLabel.TextChanged+=delegate {if(downloadWorkspace.SessionLabel!=npSessionLabel.Text)downloadWorkspace.SessionLabel=npSessionLabel.Text;};
            downloadWorkspace.SessionLabelChanged+=value=>{if(npSessionLabel.Text!=value)npSessionLabel.Text=value;};
            downloadWorkspace.BusyChanged+=UpdateStartEnabled;
            downloadPage.Controls.Add(Card("Recordings & storage","Refresh sources, list sessions, then select recordings to download or decode.",downloadWorkspace));downloadWorkspace.Show();
            BuildAdvancedWorkspace();
            FormClosing+=delegate(object sender,FormClosingEventArgs e){
                if(usbQualityRunning){usbQualityCancel=true;e.Cancel=true;usbQualityStatus="Stopping USB test after the current transfer…";return;}
                if(bleWorkspace.IsBusy){e.Cancel=true;workspaceStatus.Text="Waiting for the BLE command to finish before closing.";return;}
                if(DownloadBusy){downloadWorkspace.RequestCancel();e.Cancel=true;workspaceStatus.Text="Waiting for the storage operation to finish before closing.";}
            };
            oldInfo.Dispose();oldTime.Dispose();
            // Keep unused legacy status controls out of the workspace, while retaining ownership.
            label4.Visible=lblStaleBanks.Visible=btnImpedance.Visible=false;Controls.Add(label4);Controls.Add(lblStaleBanks);Controls.Add(btnImpedance);
            AutoScaleDimensions=new SizeF(96F,96F);AutoScaleMode=AutoScaleMode.Dpi;ResumeLayout(true);UpdateStartEnabled();
        }
        void ResetRecordingSummary(string folder) {
            usbQualityPath=usbQualityStatus=lastUsbQualityReport=null;
            UsbHealth.BeginRecording(devicePath);
            recordingSummaryFolder=folder;recordingSummaryStartUtc=recordingSummaryStopUtc=DateTime.MinValue;
            if(selectedDeviceKind!=DeviceKind.XeNp && selectedDeviceKind!=DeviceKind.NpV2)recordingSummaryStartUtc=captureStartedAtUtc;
            RefreshRecordingSummary(DateTime.UtcNow);
        }
        void ObserveRecordingSummary(DateTime first,string folder) {
            if(folder!=recordingSummaryFolder || first==DateTime.MinValue)return;
            if(recordingSummaryStartUtc!=DateTime.MinValue)return;
            recordingSummaryStartUtc=first;
            RefreshRecordingSummary(DateTime.UtcNow);
        }
        void FreezeRecordingSummary(DateTime now) {
            if(recordingSummaryStopUtc==DateTime.MinValue)recordingSummaryStopUtc=now;
            RefreshRecordingSummary(now);
        }
        static long? RecordingFolderBytes(string folder) {
            if(String.IsNullOrEmpty(folder))return 0;
            try{return Directory.EnumerateFiles(folder,"*",SearchOption.AllDirectories).Sum(file=>new FileInfo(file).Length);}
            catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}
        }
        static string RecordingSize(long? bytes) {
            if(!bytes.HasValue)return "n/a";
            string[] units={"B","KiB","MiB","GiB","TiB"};double size=bytes.Value;int unit=0;
            while(size>=1024 && unit<units.Length-1){size/=1024;unit++;}
            return size.ToString(unit==0?"F0":"F2")+" "+units[unit];
        }
        void RefreshRecordingSummary(DateTime now) {
            if(recordingSummary==null)return;
            usbHealthSummary.Text=CurrentUsbHealthSummary;
            TimeSpan elapsed=recordingSummaryStartUtc==DateTime.MinValue?TimeSpan.Zero:
                (recordingSummaryStopUtc==DateTime.MinValue?now:recordingSummaryStopUtc)-recordingSummaryStartUtc;
            if(elapsed<TimeSpan.Zero)elapsed=TimeSpan.Zero;
            string time=((long)elapsed.TotalHours).ToString("00")+":"+elapsed.Minutes.ToString("00")+":"+elapsed.Seconds.ToString("00");
            string destination=running || captureThread!=null?runFolder:saveFolder;
            recordingSummary.Text="Elapsed "+time+"   |   Recorded size "+RecordingSize(RecordingFolderBytes(recordingSummaryFolder))+"   |   Disk free "+GetRemainingDiskSpace(destination);
        }

        void BuildAdvancedWorkspace()
        {
            var tools=new TableLayoutPanel {Dock=DockStyle.Top,Height=510,ColumnCount=2,RowCount=2,Margin=Padding.Empty,BackColor=WildXTheme.Canvas};
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute,320));tools.RowStyles.Add(new RowStyle(SizeType.Percent,100));advancedPage.Controls.Add(tools);advancedPage.AutoScroll=true;
            var firmware=Stack(58,92,0);
            advancedTarget=new Label {Dock=DockStyle.Fill,AutoEllipsis=true,ForeColor=WildXTheme.Muted};firmware.Controls.Add(advancedTarget,0,0);
            diagnosticsButton=new Button {Text="Diagnostics & self-test…",AutoSize=true};
            diagnosticsButton.Click+=delegate {
                if(!NpControlsAvailable)return;firmwareManagementActive=true;UpdateStartEnabled();
                try{using(var dialog=new NpDiagnosticsForm(devicePath,selectedDeviceKind==DeviceKind.NpV2))dialog.ShowDialog(this);}
                finally{firmwareManagementActive=false;UpdateStartEnabled();}
            };
            WildXTheme.Button(btnDfu,true);firmware.Controls.Add(ActionRow(btnDfu,diagnosticsButton),0,1);
            firmware.Controls.Add(new Label {Dock=DockStyle.Fill,ForeColor=WildXTheme.Muted,
                Text="NP1: validated APP / HEX firmware.\r\nNP2: transactional .xenp APP updates.\r\n\r\nThe manager also provides USB mode changes and recovery. Choose the exact device and firmware file before updating."},0,2);
            tools.Controls.Add(Card("Firmware & USB modes",null,firmware),0,0);
            var storage=Stack(58,48,0);
            advancedStorageSource=new Label {Dock=DockStyle.Fill,AutoEllipsis=true,ForeColor=WildXTheme.Muted};storage.Controls.Add(advancedStorageSource,0,0);
            var openStorage=new Button {Text="Open Data Download",AutoSize=true};openStorage.Click+=delegate {workspaceTabs.SelectedTab=downloadPage;};
            storage.Controls.Add(ActionRow(openStorage),0,1);
            storage.Controls.Add(new Label {Dock=DockStyle.Fill,ForeColor=WildXTheme.Muted,
                Text="Delete the last recording or format a card in Data Download.\r\n\r\nThese actions require an SD card reader and confirmation. USB download is read-only."},0,2);
            tools.Controls.Add(Card("Storage maintenance",null,storage),1,0);
            var bleMaintenance=Card("BLE firmware & recovery",null,bleWorkspace.MaintenanceControls);
            tools.Controls.Add(bleMaintenance,0,1);tools.SetColumnSpan(bleMaintenance,2);
        }
        void DrawWorkspaceTab(object sender,DrawItemEventArgs e)
        {
            bool selected=e.Index==workspaceTabs.SelectedIndex;var bounds=workspaceTabs.GetTabRect(e.Index);
            using(var brush=new SolidBrush(selected?WildXTheme.AccentLight:WildXTheme.Canvas))e.Graphics.FillRectangle(brush,bounds);
            TextRenderer.DrawText(e.Graphics,workspaceTabs.TabPages[e.Index].Text,WildXTheme.Strong,bounds,selected?WildXTheme.Accent:WildXTheme.Muted,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            if(selected)using(var pen=new Pen(WildXTheme.Accent,3))e.Graphics.DrawLine(pen,bounds.Left+18,bounds.Bottom-3,bounds.Right-18,bounds.Bottom-3);
            if(selected && workspaceTabs.Focused)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(bounds,-6,-6));
        }
        void UpdateWildXControls()
        {
            npSessionLabel.Enabled=!running && npRecoveryCancelled && !DownloadBusy;
            if(downloadWorkspace!=null)downloadWorkspace.SetUsbSelection(devicePath);
            if(advancedTarget!=null)advancedTarget.Text=String.IsNullOrWhiteSpace(devicePath)?
                "Select a device in USB Mode, or choose an exact device in the firmware manager.":
                SystemInfoDeviceName()+" / UID "+(NpV2FirmwareForm.Uid(devicePath)??NpV1Management.UidFromPath(devicePath)??"unverified — confirm in manager");
            if(advancedStorageSource!=null)advancedStorageSource.Text=downloadWorkspace.SelectedSourceDescription??"No storage source selected. Choose a card in Data Download.";
            if(DownloadBusy || BleInUse)btnStart.Enabled=btnDfu.Enabled=btnRefresh.Enabled=comboDrives.Enabled=btnStreams.Enabled=btnSdInfo.Enabled=btnSystemInfoRefresh.Enabled=false;
            bool usbBusy=running || ztestRunning || systemInfoActive!=0 || firmwareManagementActive || deviceRefreshActive!=0 || !npRecoveryCancelled;
            if(gpioRtcDevices!=null)gpioRtcDevices.Enabled=gpioRtcRefresh.Enabled=!usbBusy && gpioRtcLogger==null;
            if(bleWorkspace!=null)bleWorkspace.SetHostBusy(usbBusy || DownloadBusy);
            if(downloadWorkspace!=null)downloadWorkspace.SetUsbBlocked(usbBusy || BleInUse);
            if(selectedDeviceKind==DeviceKind.XeNp)btnPreview.Enabled=NpControlsAvailable;
            if(diagnosticsButton!=null)diagnosticsButton.Enabled=NpControlsAvailable;
            if(usbQualityTestButton!=null)usbQualityTestButton.Enabled=usbQualityRunning || CanTestUsb;
            if(usbQualityContinuous!=null)usbQualityContinuous.Enabled=!usbQualityRunning && CanTestUsb;
            if(String.IsNullOrWhiteSpace(devicePath))btnStreams.Enabled=false;
        }
        void OpenV2Firmware()
        {
            if (running || systemInfoActive != 0 || firmwareManagementActive || !npRecoveryCancelled || DownloadBusy || BleInUse) return;
            firmwareManagementActive = true;
            UpdateStartEnabled();
            try { using (var dialog = new NpV2FirmwareForm(devicePath)) dialog.ShowDialog(this); }
            finally { firmwareManagementActive = false; UpdateStartEnabled(); }
            RefreshDeviceList();
        }
    }
}
