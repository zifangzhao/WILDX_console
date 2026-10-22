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
        readonly TextBox source=new TextBox{Name="calibrationBatchSource",Dock=DockStyle.Fill,AutoCompleteMode=AutoCompleteMode.SuggestAppend,AutoCompleteSource=AutoCompleteSource.FileSystemDirectories};
        readonly TextBox output=new TextBox{Name="calibrationBatchOutput",Dock=DockStyle.Fill,AutoCompleteMode=AutoCompleteMode.SuggestAppend,AutoCompleteSource=AutoCompleteSource.FileSystemDirectories};
        readonly ComboBox encoding=new ComboBox{Name="calibrationBatchEncoding",DropDownStyle=ComboBoxStyle.DropDownList,Width=250};
        readonly CheckBox overwrite=new CheckBox{Name="calibrationBatchOverwrite",Text="Overwrite originals",AutoSize=true};
        readonly Button scan=new Button{Text="Scan folders",AutoSize=true},start=new Button{Text="Calibrate ready files",AutoSize=true},cancel=new Button{Text="Cancel",AutoSize=true},close=new Button{Text="Close",AutoSize=true};
        readonly DataGridView files=new DataGridView{Name="calibrationBatchFiles",Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.FixedSingle};
        readonly Label status=new Label{Name="calibrationBatchStatus",Dock=DockStyle.Fill,AutoSize=true};
        readonly ProgressBar progress=new ProgressBar{Dock=DockStyle.Fill};
        readonly List<Control> inputs=new List<Control>();
        readonly Dictionary<NpCalibrationBatch.Item,DataGridViewRow> rows=new Dictionary<NpCalibrationBatch.Item,DataGridViewRow>();
        List<NpCalibrationBatch.Item> items=new List<NpCalibrationBatch.Item>();
        Button outputBrowse;bool busy,outputEdited;volatile bool cancelled;
        internal NpCalibrationBatchForm(NpCalibrationSet profiles)
        {
            this.profiles=profiles.Copy();Text="WILD-X — Post-calibration";Icon=NpPreviewTheme.ApplicationIcon;
            Font=WildXTheme.Body;ClientSize=new Size(1120,650);MinimumSize=new Size(900,520);StartPosition=FormStartPosition.CenterParent;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=6};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(var style in new[]{new RowStyle(SizeType.AutoSize),new RowStyle(SizeType.AutoSize),new RowStyle(SizeType.AutoSize),new RowStyle(SizeType.Percent,100),new RowStyle(SizeType.AutoSize),new RowStyle(SizeType.AutoSize)})layout.RowStyles.Add(style);
            var paths=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=3,RowCount=2};paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));paths.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AddPath(paths,0,"Source folder",source);AddPath(paths,1,"Output folder",output);layout.Controls.Add(paths,0,0);
            var options=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true};options.Controls.Add(new Label{Text="Source encoding",AutoSize=true,Margin=new Padding(0,9,8,0)});
            encoding.Items.AddRange(new object[]{"Auto from recording","Signed nominal ADC counts","Legacy unsigned ADC codes"});encoding.SelectedIndex=0;options.Controls.Add(encoding);options.Controls.Add(overwrite);options.Controls.Add(scan);layout.Controls.Add(options,0,1);
            var assignments=new Label{AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,8,0,10),Text=String.Join("   |   ",profiles.Ports.Select(p=>{var c=profiles.Get(p);return "P"+p+" → SN "+c.Serial+" · "+c.Model+(c.Np2?"":" · AP "+c.ApGain+" / LFP "+c.LfpGain);}))};layout.Controls.Add(assignments,0,2);
            files.Columns.Add("file","File");files.Columns[0].FillWeight=28;
            files.Columns.Add("band","Band");files.Columns[1].FillWeight=6;
            files.Columns.Add("ports","Probe order");files.Columns[2].FillWeight=10;
            files.Columns.Add("status","Status");files.Columns[3].FillWeight=10;
            files.Columns.Add("detail","Calibration / details");files.Columns[4].FillWeight=46;
            foreach(DataGridViewColumn column in files.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;
            layout.Controls.Add(files,0,3);
            var bottom=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};bottom.Controls.Add(progress,0,0);bottom.Controls.Add(status,0,1);layout.Controls.Add(bottom,0,4);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};actions.Controls.AddRange(new Control[]{start,cancel,close});layout.Controls.Add(actions,0,5);Controls.Add(layout);
            inputs.AddRange(new Control[]{source,output,encoding,overwrite,scan});
            source.TextChanged+=delegate{if(!outputEdited)try{if(!String.IsNullOrWhiteSpace(source.Text))output.Text=NpCalibrationBatch.DefaultOutput(source.Text.Trim().Trim('"'));}catch{}InvalidatePlan();};
            output.TextChanged+=delegate{if(output.Focused)outputEdited=true;InvalidatePlan();};encoding.SelectedIndexChanged+=delegate{InvalidatePlan();};
            overwrite.CheckedChanged+=delegate{InvalidatePlan();UpdateControls();status.Text=overwrite.Checked?"Original DATs will be replaced after successful calibration.":"Calibrated copies will be written to the output folder; originals are preserved.";};
            scan.Click+=async delegate{await Scan();};start.Click+=async delegate{await Calibrate();};cancel.Click+=delegate{cancelled=true;status.Text="Cancelling…";};close.Click+=delegate{Close();};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy){cancelled=true;e.Cancel=true;status.Text="Cancelling…";}};
            WildXTheme.Apply(this);WildXTheme.Button(start,true);status.ForeColor=assignments.ForeColor=WildXTheme.Muted;
            status.Text="Select a recording folder. Review probe assignments before calibrating; subfolders are included and originals are preserved.";UpdateControls();
        }
        void AddPath(TableLayoutPanel panel,int row,string title,TextBox text)
        {
            var browse=new Button{Text="Browse…",AutoSize=true};inputs.Add(browse);
            if(row==1)outputBrowse=browse;
            panel.Controls.Add(new Label{Text=title,AutoSize=true,Anchor=AnchorStyles.Left},0,row);panel.Controls.Add(text,1,row);panel.Controls.Add(browse,2,row);
            browse.Click+=delegate{using(var dialog=new FolderBrowserDialog{Description=title,ShowNewFolderButton=row==1,SelectedPath=Directory.Exists(text.Text)?text.Text:""})if(dialog.ShowDialog(this)==DialogResult.OK){if(row==1)outputEdited=true;text.Text=dialog.SelectedPath;}};
        }
        void InvalidatePlan(){if(busy)return;items.Clear();rows.Clear();files.Rows.Clear();UpdateControls();}
        void UpdateControls(){foreach(var control in inputs)control.Enabled=!busy;output.Enabled=outputBrowse.Enabled=!busy&&!overwrite.Checked;start.Enabled=!busy&&items.Any(p=>p.Ready);cancel.Enabled=busy;close.Enabled=!busy;}
        void SetBusy(bool value){busy=value;UpdateControls();}
        async Task Scan()
        {
            if(busy)return;cancelled=false;SetBusy(true);progress.Value=0;
            try{
                string from=source.Text.Trim().Trim('"'),to=overwrite.Checked?from:output.Text.Trim().Trim('"');if(from.Length==0||to.Length==0)throw new IOException("Enter source and output folders.");
                var format=(NpCalibrationBatch.Encoding)encoding.SelectedIndex;var ui=new Progress<string>(message=>status.Text=message);
                bool replace=overwrite.Checked;items=await Task.Run(()=>NpCalibrationBatch.Scan(from,to,profiles,format,()=>cancelled,message=>((IProgress<string>)ui).Report(message),replace));
                files.Rows.Clear();rows.Clear();foreach(var item in items){int index=files.Rows.Add(item.Relative,item.Output==null?"":item.Lfp?"LFP":"AP",item.Ports==null?"":String.Join(",",item.Ports),item.Status,item.Detail);rows.Add(item,files.Rows[index]);}
                status.Text=items.Count(p=>p.Ready)+" ready · "+items.Count(p=>!p.Ready)+" skipped. "+(replace?"Original DATs will be replaced after successful calibration.":"Output mirrors the source subfolders.");
            }catch(OperationCanceledException){status.Text="Scan cancelled.";items.Clear();}catch(Exception ex){status.Text=ex.Message;items.Clear();}
            finally{SetBusy(false);}
        }
        async Task Calibrate()
        {
            if(busy||!items.Any(p=>p.Ready))return;cancelled=false;SetBusy(true);progress.Value=0;
            try{
                bool replace=overwrite.Checked;string target=Path.GetFullPath((replace?source:output).Text.Trim().Trim('"'));
                var ui=new Progress<NpCalibrationBatch.Progress>(p=>{progress.Value=p.Total==0?0:(int)Math.Min(100,p.Done*100.0/p.Total);status.Text=p.Message;DataGridViewRow row;if(p.Item!=null&&rows.TryGetValue(p.Item,out row)){row.Cells[3].Value=p.Item.Status;row.Cells[4].Value=p.Item.Detail;}});
                string report=await Task.Run(()=>NpCalibrationBatch.Run(items,target,()=>cancelled,p=>((IProgress<NpCalibrationBatch.Progress>)ui).Report(p),replace));
                progress.Value=100;status.Text=items.Count(p=>p.Status=="Complete")+" complete · "+items.Count(p=>p.Status=="Failed")+" failed · "+items.Count(p=>p.Status=="Skipped")+" skipped. Report: "+report;
            }catch(OperationCanceledException){status.Text="Cancelled. Completed outputs and the batch report are saved; partial files remain marked incomplete.";}
            catch(Exception ex){status.Text=ex.Message;}
            finally{foreach(var item in items){DataGridViewRow row;if(rows.TryGetValue(item,out row)){row.Cells[3].Value=item.Status;row.Cells[4].Value=item.Detail;}}SetBusy(false);}
        }
    }
}
