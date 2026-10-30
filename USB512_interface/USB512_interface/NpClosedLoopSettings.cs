using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using WildNpClosedLoop;

namespace USB512_interface
{
    internal sealed class NpClosedLoopSettings
    {
        public bool Enabled,MayBeConfigured;
        public uint TrainingStartMs,TrainingDurationMs=10000;
        public NpClosedLoopChannel[] Channels;
        static int nextId=Environment.TickCount;
        static uint Next(){uint id=unchecked((uint)Interlocked.Increment(ref nextId));return id==0?unchecked((uint)Interlocked.Increment(ref nextId)):id;}
        static string PathFor(bool np2){return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WILDX_console","closed_loop_v"+(np2?2:1)+".json");}
        internal static NpClosedLoopSettings Load(bool np2)
        {
            try{var value=new JavaScriptSerializer().Deserialize<NpClosedLoopSettings>(File.ReadAllText(PathFor(np2)));
                if(value==null||value.Channels==null||value.Channels.Length<1||value.Channels.Length>2)throw new InvalidDataException("Invalid saved detector count.");
                foreach(var c in value.Channels)if(c==null||c.Gain<0.1F||c.Gain>1000)throw new InvalidDataException("Invalid saved threshold gain.");
                NpClosedLoopControl.Request(1,1,15000,value.TrainingStartMs,value.TrainingDurationMs,value.Channels);return value;}
            catch(Exception e) when(e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is InvalidDataException){
                return new NpClosedLoopSettings{Channels=new[]{new NpClosedLoopChannel{Probe=(byte)(np2?3:1),Channel=(ushort)(np2?256:0)}}};
            }
        }
        internal void Save(bool np2)
        {
            string path=PathFor(np2);Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(this));
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
        internal sealed class Setup
        {
            internal byte[] Request,Receipt;
            internal void Save(string folder)
            {
                if(folder==null)return;
                File.WriteAllBytes(Path.Combine(folder,"closed_loop_requested.bin"),Request);
                File.WriteAllBytes(Path.Combine(folder,"closed_loop_control_receipt.bin"),Receipt);
            }
        }
        internal static Task<Setup> Prepare(bool np2,uint mask,uint hz,Func<byte[],CancellationToken,Task<byte[]>> exchange,
            CancellationToken cancel,Action<string> report,string folder=null)
        {return Prepare(Load(np2),mask,hz,exchange,cancel,report,settings=>settings.Save(np2),folder);}
        internal static async Task<Setup> Prepare(NpClosedLoopSettings settings,uint mask,uint hz,Func<byte[],CancellationToken,Task<byte[]>> exchange,
            CancellationToken cancel,Action<string> report,Action<NpClosedLoopSettings> persist,string folder=null)
        {
            try{
                bool enable=settings.Enabled&&mask!=0;
                if(!enable&&!settings.MayBeConfigured)return null;
                var capsRequest=NpClosedLoopControl.Request(0,Next());
                var caps=await NpClosedLoopControl.ExchangeAsync(exchange,capsRequest,cancel).ConfigureAwait(false);
                if(!caps.Supported)throw new NotSupportedException("This firmware does not support on-device closed-loop processing.");
                if(enable){
                    if(settings.Channels.Length>caps.MaxLanes)throw new NotSupportedException("The device does not support the selected detector count.");
                    foreach(var c in settings.Channels){
                        uint port=1U<<(c.Probe-1),group=1U<<((c.Probe-1)*3+c.Channel/128);
                        if((mask&port)==0||(caps.ProbeMask&port)==0||(caps.ChannelGroups&group)==0||(caps.FilterMask&(1U<<c.Filter))==0)
                            throw new NotSupportedException("A selected closed-loop channel/filter is unavailable for this recording profile.");
                    }
                }
                var request=NpClosedLoopControl.Request(1,Next(),enable?hz:0,enable?settings.TrainingStartMs:0,
                    enable?settings.TrainingDurationMs:0,enable?settings.Channels:null);
                // A lost mutating receipt is an unknown outcome. Remember that
                // an explicit disable may be needed; never replay CONFIGURE.
                settings.MayBeConfigured=true;persist(settings);
                byte[] receipt=await exchange(request,cancel).ConfigureAwait(false);
                var reply=NpClosedLoopControl.ParseReply(receipt,1,BitConverter.ToUInt32(request,12));
                if(reply.Result!=0||enable&&((reply.Flags&2)==0||reply.ConfigCrc!=BitConverter.ToUInt32(request,60))||!enable&&(reply.Flags&2)!=0)
                    throw new IOException("Closed-loop settings were not confirmed (result "+reply.Result+").");
                settings.MayBeConfigured=enable;persist(settings);
                var setup=new Setup{Request=request,Receipt=receipt};setup.Save(folder);return setup;
            }catch(OperationCanceledException){throw;}
            catch(Exception e){if(report!=null)report("Closed-loop setup unconfirmed: "+e.Message+" Neural recording remains available.");return null;}
        }

        internal static void Edit(IWin32Window owner,bool np2)
        {
            var settings=Load(np2);
            using(var form=new Form{Text="Closed-loop events",ClientSize=new Size(690,285),MinimumSize=new Size(690,285),StartPosition=FormStartPosition.CenterParent}){
                var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(16)};form.Controls.Add(layout);
                foreach(int h in new[]{34,36,78,52,46})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
                var enabled=new CheckBox{Text="Enable on-device event detection",AutoSize=true,Checked=settings.Enabled};layout.Controls.Add(enabled,0,0);
                var count=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=70};count.Items.AddRange(new object[]{1,2});count.SelectedIndex=Math.Max(0,settings.Channels.Length-1);
                var countRow=new FlowLayoutPanel{Dock=DockStyle.Fill};countRow.Controls.AddRange(new Control[]{WildXTheme.Label("Channels"),count,WildXTheme.Label("Events only")});layout.Controls.Add(countRow,0,1);
                var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=6,RowCount=3};layout.Controls.Add(grid,0,2);
                foreach(string label in new[]{"Detector","Probe","Channel","Band","Threshold gain","Rectify"})grid.Controls.Add(WildXTheme.Label(label),grid.Controls.Count,0);
                var probes=new NumericUpDown[2];var channels=new NumericUpDown[2];var filters=new ComboBox[2];var gains=new NumericUpDown[2];var rectified=new CheckBox[2];
                string[] bands={"Delta","Theta","Alpha","Beta","Gamma","Epsilon","Ripple","IED","SPW ripple","SPW"};
                for(int i=0;i<2;i++){
                    var c=i<settings.Channels.Length?settings.Channels[i]:new NpClosedLoopChannel{Probe=(byte)(np2?3:1),Channel=(ushort)(np2?257:1)};
                    probes[i]=new NumericUpDown{Minimum=1,Maximum=3,Value=c.Probe,Width=55};channels[i]=new NumericUpDown{Minimum=0,Maximum=383,Value=c.Channel,Width=65};
                    filters[i]=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=125};filters[i].Items.AddRange(bands);filters[i].SelectedIndex=c.Filter;
                    gains[i]=new NumericUpDown{Minimum=0.1M,Maximum=1000,DecimalPlaces=1,Increment=0.5M,Value=(decimal)c.Gain,Width=90};rectified[i]=new CheckBox{Checked=c.Rectify,AutoSize=true};
                    grid.Controls.Add(WildXTheme.Label((i+1).ToString()),0,i+1);grid.Controls.Add(probes[i],1,i+1);grid.Controls.Add(channels[i],2,i+1);grid.Controls.Add(filters[i],3,i+1);grid.Controls.Add(gains[i],4,i+1);grid.Controls.Add(rectified[i],5,i+1);
                }
                var start=new NumericUpDown{Minimum=0,Maximum=3600,DecimalPlaces=1,Value=Math.Min(3600,settings.TrainingStartMs/1000M),Width=75};
                var duration=new NumericUpDown{Minimum=0.1M,Maximum=3600,DecimalPlaces=1,Value=Math.Max(0.1M,Math.Min(3600,settings.TrainingDurationMs/1000M)),Width=75};
                var training=new FlowLayoutPanel{Dock=DockStyle.Fill};training.Controls.AddRange(new Control[]{WildXTheme.Label("Training starts after (s)"),start,WildXTheme.Label("Duration (s)"),duration});layout.Controls.Add(training,0,3);
                var save=new Button{Text="Save settings",AutoSize=true};var cancel=new Button{Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};
                var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};buttons.Controls.AddRange(new Control[]{save,cancel});layout.Controls.Add(buttons,0,4);
                Action update=delegate{int selected=count.SelectedIndex+1;grid.Enabled=training.Enabled=count.Enabled=enabled.Checked;for(int i=0;i<2;i++)foreach(Control c in new Control[]{probes[i],channels[i],filters[i],gains[i],rectified[i]})c.Enabled=i<selected;};
                enabled.CheckedChanged+=delegate{update();};count.SelectedIndexChanged+=delegate{update();};update();
                save.Click+=delegate{
                    settings.Enabled=enabled.Checked;settings.TrainingStartMs=(uint)(start.Value*1000);settings.TrainingDurationMs=(uint)(duration.Value*1000);settings.Channels=new NpClosedLoopChannel[count.SelectedIndex+1];
                    for(int i=0;i<settings.Channels.Length;i++)settings.Channels[i]=new NpClosedLoopChannel{Probe=(byte)probes[i].Value,Channel=(ushort)channels[i].Value,Filter=(byte)filters[i].SelectedIndex,Gain=(float)gains[i].Value,Rectify=rectified[i].Checked};
                    if(settings.Channels.Length==2&&settings.Channels[0].Probe==settings.Channels[1].Probe&&settings.Channels[0].Channel==settings.Channels[1].Channel){MessageBox.Show(form,"Select two different channels.");return;}
                    try{settings.Save(np2);form.DialogResult=DialogResult.OK;}
                    catch(Exception e) when(e is IOException||e is UnauthorizedAccessException){MessageBox.Show(form,"Settings could not be saved: "+e.Message);}
                };
                WildXTheme.Apply(form);WildXTheme.Button(save,true);form.AcceptButton=save;form.CancelButton=cancel;form.ShowDialog(owner);
            }
        }
    }
}
