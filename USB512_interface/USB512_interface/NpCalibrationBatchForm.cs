using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USB512_interface
{
    internal sealed class NpCalibrationBatchForm:Form
    {
        readonly NpCalibrationSet profiles;
        readonly NpCalibrationLibrary library;
        readonly TextBox source=new TextBox{Name="calibrationBatchSource",Dock=DockStyle.Fill,AutoCompleteMode=AutoCompleteMode.SuggestAppend,AutoCompleteSource=AutoCompleteSource.FileSystemDirectories};
        readonly ComboBox encoding=new ComboBox{Name="calibrationBatchEncoding",DropDownStyle=ComboBoxStyle.DropDownList,Width=250};
        readonly CheckBox keepBackup=new CheckBox{Name="calibrationBatchKeepBackup",Text="Keep original backup (.dat.bak)",Checked=true,AutoSize=true};
        readonly Button scan=new Button{Text="Scan folders",AutoSize=true},start=new Button{Text="Calibrate ready files",AutoSize=true},cancel=new Button{Text="Cancel",AutoSize=true},close=new Button{Text="Close",AutoSize=true};
        readonly DataGridView files=new DataGridView{Name="calibrationBatchFiles",Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.FixedSingle};
        readonly Label status=new Label{Name="calibrationBatchStatus",Dock=DockStyle.Fill,AutoSize=true};
        readonly ProgressBar progress=new ProgressBar{Dock=DockStyle.Fill};
        readonly List<Control> inputs=new List<Control>();
        readonly Dictionary<NpCalibrationBatch.Item,DataGridViewRow> rows=new Dictionary<NpCalibrationBatch.Item,DataGridViewRow>();
        List<NpCalibrationBatch.Item> items=new List<NpCalibrationBatch.Item>();
        bool busy;volatile bool cancelled;
        internal NpCalibrationBatchForm(NpCalibrationSet profiles):this(profiles,null){}
        internal NpCalibrationBatchForm(NpCalibrationSet profiles,NpCalibrationLibrary library)
        {
            this.profiles=profiles.Copy();this.library=library;Text="WILD-X — Post-calibration";Icon=NpPreviewTheme.ApplicationIcon;
            Font=WildXTheme.Body;ClientSize=new Size(1120,650);MinimumSize=new Size(900,520);StartPosition=FormStartPosition.CenterParent;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=6};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(var style in new[]{new RowStyle(SizeType.AutoSize),new RowStyle(SizeType.AutoSize),new RowStyle(SizeType.AutoSize),new RowStyle(SizeType.Percent,100),new RowStyle(SizeType.AutoSize),new RowStyle(SizeType.AutoSize)})layout.RowStyles.Add(style);
            var paths=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=3,RowCount=1};paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));paths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AddPath(paths,0,"Source folder",source);layout.Controls.Add(paths,0,0);
            var options=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};options.Controls.Add(new Label{Text="Source encoding",AutoSize=true,Margin=new Padding(0,9,8,0)});
            encoding.Items.AddRange(new object[]{"Auto from recording","Signed nominal ADC counts","Legacy unsigned ADC codes"});encoding.SelectedIndex=0;options.Controls.Add(encoding);options.Controls.Add(keepBackup);options.Controls.Add(scan);layout.Controls.Add(options,0,1);
            var assignments=new Label{AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,8,0,10),Text=library!=null?"Factory folder: "+library.Folder+" · "+library.Count+" probe serials · match each recording automatically":String.Join("   |   ",profiles.Ports.Select(p=>{var c=profiles.Get(p);return "P"+p+" → SN "+c.Serial+" · "+c.Model+(c.Np2?"":" · AP "+c.ApGain+" / LFP "+c.LfpGain);}))};layout.Controls.Add(assignments,0,2);
            files.Columns.Add("file","File");files.Columns[0].FillWeight=28;
            files.Columns.Add("band","Band");files.Columns[1].FillWeight=6;
            files.Columns.Add("ports","Probe order");files.Columns[2].FillWeight=10;
            files.Columns.Add("status","Status");files.Columns[3].FillWeight=10;
            files.Columns.Add("detail","Calibration / details");files.Columns[4].FillWeight=46;
            foreach(DataGridViewColumn column in files.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;
            layout.Controls.Add(files,0,3);
            var bottom=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};bottom.Controls.Add(progress,0,0);bottom.Controls.Add(status,0,1);layout.Controls.Add(bottom,0,4);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};actions.Controls.AddRange(new Control[]{start,cancel,close});layout.Controls.Add(actions,0,5);Controls.Add(layout);
            inputs.AddRange(new Control[]{source,encoding,keepBackup,scan});
            source.TextChanged+=delegate{InvalidatePlan();};encoding.SelectedIndexChanged+=delegate{InvalidatePlan();};
            keepBackup.CheckedChanged+=delegate{InvalidatePlan();status.Text=OutputPolicy;};
            scan.Click+=async delegate{await Scan();};start.Click+=async delegate{await Calibrate();};cancel.Click+=delegate{cancelled=true;status.Text="Cancelling…";};close.Click+=delegate{Close();};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){cancelled=true;e.Cancel=true;status.Text="Cancelling…";}};
            WildXTheme.Apply(this);WildXTheme.Button(start,true);status.ForeColor=assignments.ForeColor=WildXTheme.Muted;
            status.Text="Select a recording folder; subfolders are included. "+OutputPolicy;UpdateControls();
        }
        void AddPath(TableLayoutPanel panel,int row,string title,TextBox text)
        {
            var browse=new Button{Text="Browse…",AutoSize=true};inputs.Add(browse);
            panel.Controls.Add(new Label{Text=title,AutoSize=true,Anchor=AnchorStyles.Left},0,row);panel.Controls.Add(text,1,row);panel.Controls.Add(browse,2,row);
            browse.Click+=delegate{using(var dialog=new FolderBrowserDialog{Description=title,ShowNewFolderButton=false,SelectedPath=Directory.Exists(text.Text)?text.Text:""})if(dialog.ShowDialog(this)==DialogResult.OK)text.Text=dialog.SelectedPath;};
        }
        string OutputPolicy {get{return "Corrected DATs replace files in their original folders. "+(keepBackup.Checked?"Originals are kept as .dat.bak.":"No original backup will be kept.");}}
        void InvalidatePlan(){if(busy)return;items.Clear();rows.Clear();files.Rows.Clear();UpdateControls();}
        void UpdateControls(){foreach(var control in inputs)control.Enabled=!busy;start.Enabled=!busy&&items.Any(p=>p.Ready);cancel.Enabled=busy;close.Enabled=!busy;}
        void SetBusy(bool value){busy=value;UpdateControls();}
        async Task Scan()
        {
            if(busy)return;cancelled=false;SetBusy(true);progress.Value=0;
            try{
                string from=source.Text.Trim().Trim('"');if(from.Length==0)throw new IOException("Enter a source folder.");
                var format=(NpCalibrationBatch.Encoding)encoding.SelectedIndex;var ui=new Progress<string>(message=>status.Text=message);
                bool backup=keepBackup.Checked;items=await Task.Run(()=>library==null?NpCalibrationBatch.Scan(from,from,profiles,format,()=>cancelled,message=>((IProgress<string>)ui).Report(message),true,backup):NpCalibrationBatch.ScanLibrary(from,from,profiles,library,format,()=>cancelled,message=>((IProgress<string>)ui).Report(message),true,backup));
                if(library!=null){
                    foreach(var missing in NpCalibrationBatch.MissingSelections(items)){
                        if(cancelled)throw new OperationCanceledException();
                        using(var picker=new NpCalibrationProbePicker(library,missing.Np2,missing.Recording+" · P"+missing.Port,missing.Probe,missing.MetadataFolder,profiles.Get(missing.Port)))
                            if(picker.ShowDialog(this)==DialogResult.OK)NpCalibrationBatch.Assign(items,missing,picker.Profile,picker.Imro);
                    }
                    await Task.Run(()=>NpCalibrationBatch.ResolveSelections(items,profiles,library,format));
                }
                files.Rows.Clear();rows.Clear();foreach(var item in items){int index=files.Rows.Add(item.Relative,item.Output==null?"":item.Lfp?"LFP":"AP",item.Ports==null?"":String.Join(",",item.Ports),item.Status,item.Detail);rows.Add(item,files.Rows[index]);}
                status.Text=items.Count(p=>p.Ready)+" ready · "+items.Count(p=>!p.Ready)+" skipped. "+OutputPolicy;
            }catch(OperationCanceledException){status.Text="Scan cancelled.";items.Clear();}catch(Exception ex){status.Text=ex.Message;items.Clear();}
            finally{SetBusy(false);}
        }
        async Task Calibrate()
        {
            if(busy||!items.Any(p=>p.Ready))return;cancelled=false;SetBusy(true);progress.Value=0;
            var estimate=new NpProgressEstimate();
            try{
                string target=Path.GetFullPath(source.Text.Trim().Trim('"'));
                var ui=new Progress<NpCalibrationBatch.Progress>(p=>{progress.Value=p.Total==0?0:(int)Math.Min(99,p.Done*100.0/p.Total);status.Text=p.Message+" · "+estimate.Describe(p.Done,p.Total);DataGridViewRow row;if(p.Item!=null&&rows.TryGetValue(p.Item,out row)){row.Cells[3].Value=p.Item.Status;row.Cells[4].Value=p.Item.Detail;}});
                string report=await Task.Run(()=>NpCalibrationBatch.Run(items,target,()=>cancelled,p=>((IProgress<NpCalibrationBatch.Progress>)ui).Report(p),true));
                progress.Value=100;status.Text=items.Count(p=>p.Status=="Complete")+" complete · "+items.Count(p=>p.Status=="Failed")+" failed · "+items.Count(p=>p.Status=="Skipped")+" skipped · "+estimate.Elapsed+". Report: "+report;
            }catch(OperationCanceledException){status.Text="Cancelled. Completed outputs and the batch report are saved; partial files remain marked incomplete.";}
            catch(Exception ex){status.Text=ex.Message;}
            finally{foreach(var item in items){DataGridViewRow row;if(rows.TryGetValue(item,out row)){row.Cells[3].Value=item.Status;row.Cells[4].Value=item.Detail;}}SetBusy(false);}
        }
    }
}
