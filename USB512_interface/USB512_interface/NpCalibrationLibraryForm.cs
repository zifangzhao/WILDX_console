using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace USB512_interface
{
    internal sealed class NpCalibrationForm:Form
    {
        readonly string source;readonly bool? np2;
        readonly TextBox folder=new TextBox{Name="calibrationLibraryFolder",Dock=DockStyle.Fill,AutoCompleteMode=AutoCompleteMode.SuggestAppend,AutoCompleteSource=AutoCompleteSource.FileSystemDirectories};
        readonly ComboBox[] serials=new ComboBox[3];readonly TextBox[] imros=new TextBox[3];readonly Label[] matches=new Label[3];
        readonly Dictionary<int,string> configurations;
        readonly Label status=new Label{Dock=DockStyle.Fill,AutoSize=true};
        readonly Button scan=new Button{Text="Search folder",AutoSize=true},apply=new Button{Text="Use for recording",AutoSize=true},batch=new Button{Text="Calibrate folders…",AutoSize=true};
        NpCalibrationLibrary library;bool busy;TableLayoutPanel probeTable;
        internal NpCalibrationForm(string source,bool? np2):this(source,np2,null){}
        internal NpCalibrationForm(string source,bool? np2,IDictionary<int,string> configurations)
        {
            this.source=source;this.np2=np2;this.configurations=configurations==null?new Dictionary<int,string>():new Dictionary<int,string>(configurations);
            Text="WILD-X — Calibration";Icon=NpPreviewTheme.ApplicationIcon;Font=WildXTheme.Body;ClientSize=new Size(1000,440);MinimumSize=new Size(850,420);StartPosition=FormStartPosition.CenterParent;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=5};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            foreach(var style in new[]{new RowStyle(SizeType.AutoSize),new RowStyle(SizeType.AutoSize),new RowStyle(SizeType.Percent,100),new RowStyle(SizeType.AutoSize),new RowStyle(SizeType.AutoSize)})layout.RowStyles.Add(style);
            var path=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=4};path.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,125));path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));path.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));path.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var browse=new Button{Text="Browse…",AutoSize=true};path.Controls.Add(new Label{Text="Calibration folder",AutoSize=true,Anchor=AnchorStyles.Left},0,0);path.Controls.Add(folder,1,0);path.Controls.Add(browse,2,0);path.Controls.Add(scan,3,0);layout.Controls.Add(path,0,0);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};var recording=new Button{Text="From recording…",AutoSize=true};actions.Controls.AddRange(new Control[]{recording,batch});layout.Controls.Add(actions,0,1);
            var table=new TableLayoutPanel{Name="calibrationProbeAssignments",Dock=DockStyle.Fill,ColumnCount=4,RowCount=7};
            probeTable=table;for(int r=0;r<7;r++)table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,60));table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            foreach(string caption in new[]{"Probe","SN","IMRO / recording configuration",""})table.Controls.Add(new Label{Text=caption,AutoSize=true,Font=WildXTheme.Strong});
            for(int p=0;p<3;p++){
                int port=p;serials[p]=new ComboBox{Name="calibrationSerial"+(p+1),Anchor=AnchorStyles.Left|AnchorStyles.Right,AutoCompleteMode=AutoCompleteMode.SuggestAppend,AutoCompleteSource=AutoCompleteSource.ListItems};
                imros[p]=new TextBox{Name="calibrationImro"+(p+1),Anchor=AnchorStyles.Left|AnchorStyles.Right};var choose=new Button{Text="IMRO…",AutoSize=true};
                matches[p]=new Label{AutoSize=true,Dock=DockStyle.Fill,ForeColor=WildXTheme.Muted,Text=""};int row=1+p*2;
                table.Controls.Add(new Label{Text="P"+(p+1),AutoSize=true,Anchor=AnchorStyles.Left},0,row);table.Controls.Add(serials[p],1,row);table.Controls.Add(imros[p],2,row);table.Controls.Add(choose,3,row);
                table.Controls.Add(matches[p],1,row+1);table.SetColumnSpan(matches[p],3);
                choose.Click+=async delegate{using(var file=new OpenFileDialog{Filter="Probe IMRO or configuration|*.imro;*.json"})if(file.ShowDialog(this)==DialogResult.OK){imros[port].Text=file.FileName;await Match(port);}};
                serials[p].SelectedIndexChanged+=async delegate{await Match(port);};imros[p].Leave+=async delegate{await Match(port);};
                var selected=NpCalibrationStore.Get(source).Get(p+1);if(selected!=null)serials[p].Text=selected.Serial;
                string request;if(this.configurations.TryGetValue(p+1,out request))try{var mapping=NpCalibrationLibrary.Configuration(request);if(mapping.Serial!=null)serials[p].Text=mapping.Serial;matches[p].Text="Current recording configuration";}catch(Exception ex){matches[p].Text=ex.Message;}
            }
            layout.Controls.Add(table,0,2);layout.Controls.Add(status,0,3);
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};var manual=new Button{Text="Manual setup…",AutoSize=true};var close=new Button{Text="Done",AutoSize=true};bottom.Controls.AddRange(new Control[]{apply,manual,close});layout.Controls.Add(bottom,0,4);Controls.Add(layout);
            folder.Text=NpCalibrationStore.LibraryFolder;
            folder.TextChanged+=delegate{if(!busy&&library!=null&&!String.Equals(folder.Text.Trim().Trim('"'),library.Folder,StringComparison.OrdinalIgnoreCase)){library=null;SetBusy(false);status.Text="Search the selected calibration folder.";}};
            browse.Click+=async delegate{if(busy)return;using(var dialog=new FolderBrowserDialog{SelectedPath=Directory.Exists(folder.Text)?folder.Text:""})if(dialog.ShowDialog(this)==DialogResult.OK){folder.Text=dialog.SelectedPath;await Search();}};
            scan.Click+=async delegate{await Search();};apply.Click+=async delegate{await Apply();};close.Click+=delegate{if(!busy)Close();};
            batch.Click+=delegate{if(!busy&&library!=null)using(var form=new NpCalibrationBatchForm(NpCalibrationStore.Get(source),library))form.ShowDialog(this);};
            manual.Click+=delegate{if(!busy)using(var form=new NpCalibrationManualForm(source,np2))form.ShowDialog(this);};
            recording.Click+=async delegate{if(busy)return;using(var file=new OpenFileDialog{Filter="Recording probe information|recording_settings.json;probe_info.json"})if(file.ShowDialog(this)==DialogResult.OK){try{ImportRecording(file.FileName);if(library==null)await Search();else for(int p=0;p<3;p++)await Match(p);}catch(Exception ex){status.Text=ex.Message;}}};
            Shown+=async delegate{if(!String.IsNullOrWhiteSpace(folder.Text))await Search();};FormClosing+=delegate(object sender,FormClosingEventArgs e){if(busy)e.Cancel=true;};
            WildXTheme.Apply(this);WildXTheme.Button(apply,true);WildXTheme.Button(batch,true);SetBusy(false);status.Text="Select the factory folder once. SN selects the CSVs; IMRO supplies acquisition gains.";
        }
        void SetBusy(bool value){busy=value;probeTable.Enabled=folder.Enabled=scan.Enabled=!busy;apply.Enabled=batch.Enabled=!busy&&library!=null;}
        async Task Search()
        {
            if(busy)return;SetBusy(true);status.Text="Searching calibration files…";
            try{string path=folder.Text.Trim().Trim('"');library=await Task.Run(()=>NpCalibrationLibrary.Scan(path));NpCalibrationStore.LibraryFolder=library.Folder;
                for(int p=0;p<3;p++){string selected=serials[p].Text;serials[p].Items.Clear();serials[p].Items.AddRange(library.Serials.Cast<object>().ToArray());serials[p].Text=selected;}
                status.Text=library.Count+" probe serials found"+(library.Warnings.Count==0?".":" · "+library.Warnings.Count+" CSVs ignored.");
            }catch(Exception ex){library=null;status.Text=ex.Message;}finally{SetBusy(false);}
            if(library!=null)for(int p=0;p<3;p++)await Match(p);
        }
        Task<NpCalibrationLibrary.Mapping> Mapping(int p)
        {
            string path=imros[p].Text.Trim().Trim('"'),request;configurations.TryGetValue(p+1,out request);
            return Task.Run(()=>!String.IsNullOrWhiteSpace(path)?library.ReadMapping(path):request==null?null:NpCalibrationLibrary.Configuration(request));
        }
        async Task Match(int p)
        {
            if(busy||library==null||String.IsNullOrWhiteSpace(serials[p].Text))return;string serial=serials[p].Text.Trim();SetBusy(true);
            try{var mapping=await Mapping(p);var profile=await Task.Run(()=>library.Resolve(serial,mapping));if(np2.HasValue&&profile.Np2!=np2.Value)throw new InvalidDataException("Calibration model differs from this logger.");matches[p].Text=profile.Model+" · SN "+profile.Serial+(profile.Np2?" · gain CSV matched":" · ADC + gain matched · AP "+profile.ApGain+" / LFP "+profile.LfpGain);}
            catch(Exception ex){matches[p].Text=ex.Message;}finally{SetBusy(false);}
        }
        async Task Apply()
        {
            if(busy||library==null)return;SetBusy(true);
            try{var selected=new NpCalibrationSet();var snapshots=new Dictionary<int,byte[]>();
                for(int p=0;p<3;p++)if(!String.IsNullOrWhiteSpace(serials[p].Text)){int port=p;string serial=serials[p].Text.Trim();
                    var mapping=await Mapping(port);var profile=await Task.Run(()=>library.Resolve(serial,mapping));
                    if(np2.HasValue&&profile.Np2!=np2.Value)throw new InvalidDataException("P"+(p+1)+": calibration model differs from the logger.");selected.Set(p+1,profile);
                    if(mapping!=null&&mapping.File!=null&&Path.GetExtension(mapping.File).Equals(".imro",StringComparison.OrdinalIgnoreCase)){byte[] bytes=File.ReadAllBytes(mapping.File);using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","")!=mapping.Hash)throw new IOException("IMRO changed after matching.");snapshots[p+1]=bytes;}
                }
                NpCalibrationStore.Set(source,selected);NpCalibrationStore.SetImros(source,snapshots);status.Text=selected.Count+" probe calibration profiles selected for future recording/preview.";
            }catch(Exception ex){status.Text=ex.Message;}finally{SetBusy(false);}
        }
        void ImportRecording(string file)
        {
            var json=new JavaScriptSerializer{MaxJsonLength=4*1024*1024};var doc=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file));var probes=NpCalibrationLibrary.Value(doc,"probes") as IEnumerable;if(probes==null)throw new InvalidDataException("Recording has no probe information.");
            bool wasBusy=busy;busy=true;try{
            configurations.Clear();for(int p=0;p<3;p++){serials[p].Text=imros[p].Text=matches[p].Text="";}
            foreach(Dictionary<string,object> probe in probes){int port=Convert.ToInt32(NpCalibrationLibrary.Value(probe,"probe")??NpCalibrationLibrary.Value(probe,"physical_port"));if(port<1||port>3)continue;
                var identity=NpCalibrationLibrary.Value(probe,"identity") as Dictionary<string,object>;
                serials[port-1].Text=NpCalibrationLibrary.Serial(NpCalibrationLibrary.Value(identity,"serial")??NpCalibrationLibrary.Value(identity,"serial_hex"))??"";
                string imro=NpCalibrationLibrary.Value(probe,"imro_file") as string;if(!String.IsNullOrWhiteSpace(imro))imros[port-1].Text=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file),imro));
            }
            }finally{busy=wasBusy;}
            status.Text="Probe SNs and IMRO paths loaded from recording metadata.";
        }
    }
}
