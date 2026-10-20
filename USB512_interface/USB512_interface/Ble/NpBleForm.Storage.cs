using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using CE32_console;

namespace USB512_interface
{
    internal sealed partial class NpBleForm
    {
        async Task OpenRecordings()
        {
            ClosePreview();var initial=await ReadStatus();int generation=connection.Epoch,version=recorderVersion;
            Action<uint> updateCapacity=null;
            Action target=()=>{if(!connection.Connected || connection.Epoch!=generation || recorderVersion!=version)throw new IOException("Connection changed. Close and reopen Recordings.");};
            var v2=new NpV2BleCatalog(ReadStatus,connection.ExchangeAsync,Send,target);
            Func<Task<NpBleCatalog>> read=async()=>{
                target();if(version==2)return await v2.Read();var before=await ReadStatus();
                if(before.Capacity!=initial.Capacity)throw new IOException("Card capacity changed. Reopen Recordings before reading storage.");
                var catalog=await NpBleCatalog.ReadV1(before,async page=>{target();var body=await connection.ExchangeV1Storage(page,false);target();return body;});
                // Preserve a degraded prefix after a cutoff, including native94
                // timeout retirement. It is a labeled read-only snapshot and
                // cannot authorize mutations; do not issue more reads here.
                if(catalog.V1ReadOnly && catalog.V1Warning!=null)return catalog;
                target();var after=await ReadStatus();
                if(!NpBleCatalog.CanReadV1(after) || before.Capacity!=after.Capacity || before.Start!=after.Start ||
                    NpV1BleProtocol.HasReadOnlyCatalog(before)!=NpV1BleProtocol.HasReadOnlyCatalog(after))throw new IOException("Storage changed while listing recordings. Refresh before modifying it.");
                return catalog;
            };
            Func<NpBleCatalog,Task<NpBleCatalog>> delete=async snapshot=>{
                if(version==2)return await v2.Delete(snapshot);
                if(snapshot==null || !snapshot.CanDeleteLast)throw new IOException("A stopped, mounted, writable V1 catalog is required for deletion.");
                target();var fresh=await read();if(!fresh.CanDeleteLast || !snapshot.SameAs(fresh) || fresh.Records.Count==0)throw new IOException("The recording list changed. Refresh and confirm the last recording again.");
                var s=await ReadStatus();target();if(!NpV1BleProtocol.CanConfigure(s) || !NpBleCatalog.CanReadV1(s) || s.Capacity!=fresh.Capacity || s.Start!=fresh.NextStart || (s.Flags&17)!=17 || (s.Flags&NpV1BleProtocol.CatalogReadOnly)!=0)throw new IOException("Stopped, unchanged, writable V1 storage required.");
                uint remaining=BitConverter.ToUInt32(await connection.ExchangeV1Storage(0,true),0);
                if(remaining!=(uint)fresh.Records.Count-1)throw new IOException("Delete result differs from the confirmed list. Refresh before any further action.");
                var result=await read();if(result.Records.Count!=fresh.Records.Count-1)throw new IOException("Deleted catalog could not be verified. No delete replayed.");return result;
            };
            Func<bool,Task> prepare=async format=>{
                target();var s=await ReadStatus();target();
                if(s.Capacity!=initial.Capacity)throw new IOException("Card capacity changed. Reopen Recordings before modifying storage.");
                bool recoverCapacity=version==1&&!format&&initial.Capacity==0;
                byte[] request;
                if(version==2){
                    if(!NpV2SdControl.CanPrepareStorage(s))throw new IOException("Stop recording before mounting or formatting storage.");
                    request=NpV2SdControl.StorageRequest(NpV2SdControl.NextId(s),format);
                }else{
                    request=format?NpV1BleProtocol.Format(s,initial.Capacity):NpV1BleProtocol.Mount(s,NpSdControl.NextId(s));
                }
                uint requestedLayout=version==1&&format?(NpSdGeometry.U(request,4)==2?NpSdGeometry.U(request,16):1U):0U;
                uint mutationId=NpSdGeometry.U(request,12);await Send(request);
                target();if(current==null)throw new IOException("Storage receipt is no longer available.");
                if(current.Error!=0)throw new IOException("Storage command rejected: "+current.Error+". No command replayed.");
                if(current.LastId!=mutationId)throw new IOException("Storage mutation was not acknowledged. No command replayed.");
                var wait=Stopwatch.StartNew();
                while(current!=null && (current.Flags&8)!=0 && wait.ElapsedMilliseconds<10000){await Task.Delay(200);target();await ReadStatus();}
                if(version==1){
                    if(recoverCapacity){
                        if(current==null||(current.Flags&8)!=0)throw new IOException("Mount is still pending. Read status; no command replayed.");
                        target();await ReadStatus();target();
                        uint recovered=NpV1BleProtocol.RequireMountStatusCompleted(current,0,mutationId);
                        initial.Capacity=recovered;if(updateCapacity!=null)updateCapacity(recovered);
                    }
                    NpV1BleProtocol.RequireStorageLayoutCompleted(current,initial.Capacity,mutationId,format,requestedLayout);
                    if(format){
                        var empty=await read();
                        if(empty.Records.Count!=0 || empty.NextStart!=8192)throw new IOException("Formatted V1 catalog is not empty. No command replayed.");
                    }
                }else{
                    if(current==null || current.LastId!=mutationId || current.Error!=0 || current.Fault!=0 || (current.Flags&8)!=0)throw new IOException("Storage operation not confirmed. Read status; no command replayed.");
                    if(current.State!=1)throw new IOException("Storage catalog did not mount. No command replayed.");
                }
            };
            using(var dialog=new NpBleStorageForm("V"+version+" · "+address.Text.Trim().ToUpperInvariant(),initial.Capacity,read,delete,prepare,version==1 || NpV2BleCatalog.Supported(initial))){updateCapacity=dialog.SetCapacity;dialog.ShowDialog(DialogOwner);}
        }
    }

    internal sealed class NpBleStorageForm : Form
    {
        readonly ListView records=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,HideSelection=false,BorderStyle=BorderStyle.None,BackColor=WildXTheme.SurfaceSoft};
        readonly Label info=new Label{AutoSize=true,Dock=DockStyle.Bottom,Padding=new Padding(0,12,0,0),MaximumSize=new Size(850,0)};
        readonly FlowLayoutPanel actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top};
        readonly Func<Task<NpBleCatalog>> read;
        readonly Func<NpBleCatalog,Task<NpBleCatalog>> delete;
        readonly Func<bool,Task> prepare;
        readonly string peer;uint capacity;readonly bool listingSupported;
        readonly Button remove;
        NpBleCatalog catalog;bool busy;
        internal NpBleStorageForm(string device,uint sectors,Func<Task<NpBleCatalog>> list,Func<NpBleCatalog,Task<NpBleCatalog>> deleteLast,Func<bool,Task> storage,bool canList)
        {
            peer=device;capacity=sectors;read=list;delete=deleteLast;prepare=storage;listingSupported=canList;Padding=new Padding(12);
            Text="WILD-X — Recordings · "+peer;Icon=NpPreviewTheme.ApplicationIcon;ClientSize=new Size(780,470);MinimumSize=new Size(650,380);StartPosition=FormStartPosition.CenterParent;
            records.Columns.Add("ID",60);records.Columns.Add("Size (MiB)",130);records.Columns.Add("Start sector",170);records.Columns.Add("End sector",170);
            records.Columns.Add("Status",110);
            var refresh=Add("Refresh",RefreshRecords);refresh.Enabled=canList;
            Add("Mount catalog",async()=>{await prepare(false);if(listingSupported)await RefreshRecords();else info.Text="Catalog mounted.";});
            remove=Add("Delete last",DeleteLast);var format=Add("Format card",FormatCard);
            Controls.Add(records);Controls.Add(info);Controls.Add(actions);WildXTheme.Apply(this);WildXTheme.Button(remove,false,true);WildXTheme.Button(format,false,true);info.ForeColor=WildXTheme.Muted;
            // Recovery format must be reachable before a catalog read: a native
            // timeout can retire the BLE session on an old/unreadable card.
            remove.Enabled=false;Shown+=delegate{info.Text=listingSupported?"Refresh to list recordings. Format does not require a readable catalog.":"V2 BLE file listing requires firmware support. Mount and format are available.";};FormClosing+=delegate(object sender,FormClosingEventArgs e){e.Cancel=busy;};
            records.SizeChanged+=delegate{records.Columns[3].Width=Math.Max(100,records.ClientSize.Width-474);};
            for(int i=1;i<4;i++)records.Columns[i].TextAlign=HorizontalAlignment.Right;
        }
        Button Add(string text,Func<Task> action){var button=new Button{Text=text,AutoSize=true,MinimumSize=new Size(0,36)};actions.Controls.Add(button);button.Click+=async delegate{await Run(action);};return button;}
        async Task Run(Func<Task> action)
        {
            if(busy)return;busy=true;actions.Enabled=false;
            try{await action();}catch(Exception ex){catalog=null;records.Items.Clear();info.Text=ex.Message;}
            finally{busy=false;actions.Enabled=true;remove.Enabled=catalog!=null && catalog.CanDeleteLast;}
        }
        async Task RefreshRecords(){ShowCatalog(await read());}
        internal void SetCapacity(uint sectors){capacity=sectors;}
        void ShowCatalog(NpBleCatalog value)
        {
            catalog=value;records.BeginUpdate();records.Items.Clear();double used=0;bool append=false;
            foreach(var record in value.Records){
                string status=value.V2==null?(value.V1ReadOnly?"Read-only":"—"):NpV2BleCatalog.RecordStatus(record);
                append|=value.V2!=null && (record.Flags&255)==14;
                records.Items.Add(new ListViewItem(new[]{record.Id.ToString(),record.MiB.ToString("N2"),record.Start.ToString("N0"),record.End.ToString("N0"),status}));used+=record.MiB;
            }
            records.EndUpdate();info.Text=value.Records.Count+" recordings · "+used.ToString("N2")+" MiB allocated · card "+(capacity*512.0/1073741824).ToString("N2")+" GiB";
            if(value.V1ReadOnly)info.Text="Read-only catalog — recording and deletion disabled.\r\n"+info.Text;
            else if(value.V1Full)info.Text="SD card full — recording disabled.\r\n"+info.Text;
            if(value.V1Warning!=null)info.Text+="\r\n"+value.V1Warning;
            if(append)info.Text+="\r\n"+NpV2BleCatalog.AppendDiagnostic;
        }
        async Task DeleteLast()
        {
            if(catalog==null || !catalog.CanDeleteLast)return;var last=catalog.Records[catalog.Records.Count-1];
            if(MessageBox.Show(this,"Delete recording "+last.Id+" ("+last.MiB.ToString("N2")+" MiB) from "+peer+"?\r\nIts storage can be overwritten by later recordings.","Delete last recording",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            ShowCatalog(await delete(catalog));
        }
        async Task FormatCard()
        {
            if(MessageBox.Show(this,"Format the "+(capacity*512.0/1073741824).ToString("N2")+" GiB card in "+peer+"?\r\nAll recording catalog entries will be removed. Later recordings can overwrite the data."+
                (peer.StartsWith("V1",StringComparison.Ordinal)?"\r\nV1 filesystem2 is used only when advertised and requires compatible V1 firmware. Older firmware keeps its legacy layout.":""),"Format card",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
            catalog=null;records.Items.Clear();await prepare(true);if(listingSupported)await RefreshRecords();else info.Text="Card formatted.";
        }
    }
}
