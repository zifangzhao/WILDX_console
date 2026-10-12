using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace USB512_interface
{
    // Immutable display geometry, prepared before START from compiled requests.
    // NP1 uses the same zero-based site/channel and staggered contact geometry
    // as NPConfigurator Catalog.Channel and StandardFormats.ToProbeInterface.
    internal sealed class NpPreviewSite
    {
        internal readonly int Channel, Site, Shank;
        internal readonly float XUm,YUm;
        internal readonly string Label;
        internal NpPreviewSite(int channel,int site=-1,int shank=0,bool np2=false)
        {
            Channel=channel;Site=site;Shank=shank;
            int row=site/2;
            XUm=shank*250+site%2*32+(!np2 && row%2==0?16:0);YUm=row*(np2?15:20);
            Label=site<0?"C"+channel:"S"+shank+" E"+site+" / C"+channel;
        }
    }

    internal sealed class NpPreviewPage
    {
        internal readonly int[] Channels=new int[64];
        internal readonly NpPreviewSite[] Sites=new NpPreviewSite[64];
        internal readonly int Count;
        internal NpPreviewPage(List<NpPreviewSite> ordered,int start)
        {
            for(int i=0;i<64;i++)Channels[i]=-1;
            Count=Math.Min(64,ordered.Count-start);
            for(int i=0;i<Count;i++) {
                NpPreviewSite site=ordered[start+i];Sites[i]=site;Channels[i]=site.Channel;
            }
        }
    }

    internal sealed class NpPreviewDefinition
    {
        internal readonly NpPreviewPage[] Pages;
        internal readonly string ConfigurationId;
        internal readonly bool Physical;
        internal readonly bool Np2;
        internal static readonly NpPreviewDefinition Hardware=CreateHardware();
        private NpPreviewDefinition(List<NpPreviewSite> sites,bool physical,string id)
            : this(sites,physical,id,false) { }
        private NpPreviewDefinition(List<NpPreviewSite> sites,bool physical,string id,bool np2)
        {
            Physical=physical;ConfigurationId=id;Np2=np2;
            sites.Sort((a,b)=>{
                int shank=a.Shank.CompareTo(b.Shank);
                if(shank!=0)return shank;
                if(!physical)return a.Channel.CompareTo(b.Channel);
                int row=b.YUm.CompareTo(a.YUm);
                if(row!=0)return row;
                int column=a.XUm.CompareTo(b.XUm);
                return column!=0?column:a.Channel.CompareTo(b.Channel);
            });
            Pages=new NpPreviewPage[Math.Max(1,(sites.Count+63)/64)];
            for(int i=0;i<Pages.Length;i++)Pages[i]=new NpPreviewPage(sites,i*64);
        }
        private static NpPreviewDefinition CreateHardware()
        {
            var sites=new List<NpPreviewSite>();
            for(int ch=0;ch<384;ch++)sites.Add(new NpPreviewSite(ch));
            return new NpPreviewDefinition(sites,false,null);
        }
        internal static NpPreviewDefinition FromSelection(byte[] selection)
        {
            // NPB1 only defines NP1/NRIC. No NP2 physical routing is inferred.
            if(selection==null || selection.Length!=1576 || selection[0]!=78 ||
               selection[1]!=80 || selection[2]!=66 || selection[3]!=49 ||
               selection[4]!=2 || selection[5]>2)return null;
            var sites=new List<NpPreviewSite>();
            for(int ch=0;ch<384;ch++) {
                int at=40+ch*4,site=selection[at]|selection[at+1]<<8;
                if(site==65535 || (selection[at+2]&128)!=0)continue;
                if(site>=960 || site%384!=ch || site==191 || site==575 || site==959)return null;
                sites.Add(new NpPreviewSite(ch,site));
            }
            return new NpPreviewDefinition(sites,true,BitConverter.ToString(selection,8,8).Replace("-",""));
        }
        internal static NpPreviewDefinition FromNp2Request(string normalizedJson,byte[] selection)
        {
            // The shared configurator already validated the vendor channel map.
            // Use its normalized channel assignments, not site % 384 or a second
            // routing catalog. N2B1 carries shift bits, not a channel/site table.
            if(String.IsNullOrWhiteSpace(normalizedJson) || selection==null || selection.Length!=876 ||
               selection[0]!=78 || selection[1]!=50 || selection[2]!=66 || selection[3]!=49 ||
               selection[4]!=4 || selection[5]>2 || selection[6]!=0 || selection[7]!=0)return null;
            var document=new JavaScriptSerializer {MaxJsonLength=1024*1024}.Deserialize<Dictionary<string,object>>(normalizedJson);
            object profile,connector,channels;
            if(document==null || !document.TryGetValue("Profile",out profile) || !Equals(profile,"Np2Multi") ||
               !document.TryGetValue("Connector",out connector) || !Equals(connector,(int)selection[5]+1) ||
               !document.TryGetValue("Channels",out channels) || !(channels is System.Collections.IEnumerable))return null;
            var sites=new List<NpPreviewSite>();var assigned=new bool[384];var occupied=new HashSet<int>();
            foreach(object item in (System.Collections.IEnumerable)channels) {
                var channel=item as Dictionary<string,object>;object chValue,siteValue,shankValue,standby;
                if(channel==null || !channel.TryGetValue("Channel",out chValue) || !(chValue is int) ||
                   !channel.TryGetValue("Site",out siteValue) || !(siteValue is int) ||
                   !channel.TryGetValue("Shank",out shankValue) || !(shankValue is int) ||
                   !channel.TryGetValue("Standby",out standby) || !Equals(standby,false))return null;
                int ch=(int)chValue,site=(int)siteValue,shank=(int)shankValue;
                if(ch<0 || ch>=384 || assigned[ch] || site<0 || site>=1280 || shank<0 || shank>=4 ||
                   !occupied.Add(shank*1280+site))return null;
                int bit=(site&1)!=0?642-site/2:645+site/2;
                if((selection[40+shank*161+bit/8]&(1<<(7-bit%8)))==0)return null;
                assigned[ch]=true;sites.Add(new NpPreviewSite(ch,site,shank,true));
            }
            return sites.Count==384?new NpPreviewDefinition(sites,true,BitConverter.ToString(selection,8,8).Replace("-",""),true):null;
        }
    }

    internal static class NpPreviewTheme
    {
        internal static readonly Color Canvas=Color.FromArgb(27,36,50);
        internal static readonly Color Blue=Color.FromArgb(118,182,255);
        internal static readonly Color Grid=Color.FromArgb(50,64,82);
        internal static readonly Color Sweep=Color.FromArgb(112,130,151);
        internal static readonly SolidBrush Text=new SolidBrush(Color.FromArgb(221,230,242));
        internal static readonly SolidBrush Secondary=new SolidBrush(Color.FromArgb(168,184,205));
        internal static readonly SolidBrush Inactive=new SolidBrush(Color.FromArgb(65,81,102));
        internal static readonly Icon ApplicationIcon=LoadIcon();
        private static Icon LoadIcon()
        {
            using(var stream=typeof(NpPreviewTheme).Assembly.GetManifestResourceStream("USB512_interface.Waveform.ico"))
                return stream==null?null:new Icon(stream,32,32);
        }
    }

    internal sealed class NpPreviewPalette : IDisposable
    {
        internal readonly string Name;
        internal readonly Pen[] Pens=new Pen[64];
        internal readonly SolidBrush[] Brushes=new SolidBrush[64];
        internal NpPreviewPalette(string name,params Color[] anchors)
        {
            Name=name;
            for(int row=0;row<64;row++) {
                float position=row*(anchors.Length-1)/63f;
                int first=(int)position,last=Math.Min(first+1,anchors.Length-1);float t=position-first;
                Color a=anchors[first],b=anchors[last];
                Color color=Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));
                Pens[row]=new Pen(color,1f);Brushes[row]=new SolidBrush(color);
            }
        }
        internal static NpPreviewPalette[] CreateAll()
        {
            return new[] {
                // Brighter channel colours retain the hue families while making
                // one-pixel traces readable on the muted slate canvas.
                new NpPreviewPalette("Blue",NpPreviewTheme.Blue),
                new NpPreviewPalette("Viridis",Color.FromArgb(174,126,214),Color.FromArgb(112,166,224),Color.FromArgb(64,185,180),Color.FromArgb(137,209,132),Color.FromArgb(239,221,92)),
                new NpPreviewPalette("Turbo",Color.FromArgb(174,141,216),Color.FromArgb(111,160,240),Color.FromArgb(84,200,230),Color.FromArgb(109,218,167),Color.FromArgb(207,223,99),Color.FromArgb(246,172,86),Color.FromArgb(237,136,153)),
                new NpPreviewPalette("Grayscale",Color.FromArgb(160,160,160),Color.FromArgb(224,224,224))
            };
        }
        public void Dispose() {for(int i=0;i<64;i++){Pens[i].Dispose();Brushes[i].Dispose();}}
    }

    // Per-probe recording producers, one display consumer. Only the selected
    // bank is copied. A nonwaiting gate handles overlap during probe selection;
    // display contention never blocks a recording worker.
    internal sealed class NpPreviewQueue
    {
        private sealed class Selection { internal bool Lfp,Contiguous; internal int Bank, Generation, Probe=1,FirstChannel; internal int[] Channels; }
        private Selection selection=new Selection();
        private readonly short[] samples;
        private readonly long[] indices;
        private readonly int[] generations;
        private readonly int capacity;
        private long published, consumed, dropped;
        private int publishing;
        private readonly long[] apWritten=new long[4],lfpWritten=new long[4];
        internal long DroppedRows { get { return Interlocked.Read(ref dropped); } }
        internal int Generation { get { return Volatile.Read(ref selection).Generation; } }
        internal long Written(bool lfp,int probe=1) { return lfp?Interlocked.Read(ref lfpWritten[probe]):Interlocked.Read(ref apWritten[probe]); }
        internal NpPreviewQueue(int capacity=8192)
        {
            if(capacity<1)throw new ArgumentOutOfRangeException("capacity");
            this.capacity=capacity;samples=new short[checked(capacity*64)];
            indices=new long[capacity];generations=new int[capacity];
        }
        internal void Select(bool lfp,int bank,int probe=1,int[] channels=null)
        {
            if(bank<0 || bank>=6)throw new ArgumentOutOfRangeException("bank");
            if(probe<1 || probe>3)throw new ArgumentOutOfRangeException("probe");
            Selection old=Volatile.Read(ref selection);
            if(old.Lfp==lfp && old.Bank==bank && old.Probe==probe && ReferenceEquals(old.Channels,channels))return;
            bool contiguous=true;int first=bank*64;
            if(channels!=null) {
                if(channels.Length!=64)throw new ArgumentException("Preview requires one 64-channel page.");
                first=channels[0];
                for(int i=0;i<64;i++) {
                    if(channels[i]<-1 || channels[i]>=384)throw new ArgumentException("Invalid preview hardware channel.");
                    if(channels[i]!=first+i || first<0)contiguous=false;
                }
            }
            Volatile.Write(ref selection,new Selection { Lfp=lfp,Bank=bank,Probe=probe,Generation=old.Generation+1,
                Channels=channels,Contiguous=contiguous,FirstChannel=first });
        }
        internal void InvalidateView()
        {
            Selection old=Volatile.Read(ref selection);
            Volatile.Write(ref selection,new Selection { Lfp=old.Lfp,Bank=old.Bank,Probe=old.Probe,Generation=old.Generation+1,
                Channels=old.Channels,Contiguous=old.Contiguous,FirstChannel=old.FirstChannel });
        }
        internal void AdvanceClock(int apRows,int lfpRows,int probe=1)
        { Interlocked.Add(ref apWritten[probe],apRows);Interlocked.Add(ref lfpWritten[probe],lfpRows); }
        internal void Push(short[] ap,int apRows,short[] lfp,int lfpRows,int probe=1)
        {
            Selection selected=Volatile.Read(ref selection);
            long a=apWritten[probe],l=lfpWritten[probe];
            if(probe!=selected.Probe) {AdvanceClock(apRows,lfpRows,probe);return;}
            short[] source=selected.Lfp?lfp:ap;
            int count=selected.Lfp?lfpRows:apRows;
            long first=selected.Lfp?l:a;
            if(Interlocked.CompareExchange(ref publishing,1,0)==0) {
                try {
                    for(int row=0;row<count;row++) {
                        long at=published;
                        if(at-Volatile.Read(ref consumed)>=capacity) { Interlocked.Add(ref dropped,count-row);break; }
                        int slot=(int)(at%capacity);
                        if(selected.Channels==null || selected.Contiguous)
                            Buffer.BlockCopy(source,(row*384+selected.FirstChannel)*2,samples,slot*128,128);
                        else {
                            int sourceAt=row*384,targetAt=slot*64;
                            for(int ch=0;ch<64;ch++) {
                                int hardware=selected.Channels[ch];
                                samples[targetAt+ch]=hardware<0?(short)0:source[sourceAt+hardware];
                            }
                        }
                        indices[slot]=first+row;generations[slot]=selected.Generation;
                        Volatile.Write(ref published,at+1);
                    }
                } finally { Volatile.Write(ref publishing,0); }
            } else {
                Interlocked.Add(ref dropped,count);
            }
            Interlocked.Exchange(ref apWritten[probe],a+apRows);Interlocked.Exchange(ref lfpWritten[probe],l+lfpRows);
        }
        internal bool DrainTo(NpSweepBuffer target,int generation)
        {
            long limit=Volatile.Read(ref published),start=consumed;
            // Snapshot the limit: a fast producer cannot keep this loop running
            // forever. Publish each retired slot only after the copy finishes.
            for(long at=start;at<limit;at++) {
                int slot=(int)(at%capacity);
                if(generations[slot]==generation)target.AddRow(samples,slot*64,indices[slot]);
                Volatile.Write(ref consumed,at+1);
            }
            return limit!=start;
        }
    }

    // Display-only phase buffer. Absolute sample indices stay at fixed X
    // positions; new samples overwrite only the section behind the sweep.
    internal sealed class NpSweepBuffer
    {
        internal int Period { get; private set; }
        internal readonly short[] Samples;
        private readonly long[] stamps;
        internal long Written { get; private set; }
        internal long CopiedRows { get; private set; }
        internal int Cursor { get { return (int)(Written%Period); } }
        internal int ValidRows { get { return (int)Math.Min(Written,Period); } }
        private bool initialized;
        internal NpSweepBuffer(int period,int capacity=0)
        {
            if(period<2)throw new ArgumentOutOfRangeException("period");
            capacity=Math.Max(period,capacity);
            Period=period;Samples=new short[checked(capacity*64)];stamps=new long[capacity];
        }
        internal void Reset(int period)
        {
            if(period<2 || period>stamps.Length)throw new ArgumentOutOfRangeException("period");
            Period=period;Written=0;CopiedRows=0;initialized=false;Array.Clear(stamps,0,stamps.Length);
        }
        internal void AddRow(short[] source,int offset,long index)
        {
            if(index<Math.Max(0,Written-Period))return;
            int phase=(int)(index%Period);
            Buffer.BlockCopy(source,offset*2,Samples,phase*128,128);
            stamps[phase]=index+1;Written=Math.Max(Written,index+1);CopiedRows++;initialized=true;
        }
        internal void AdvanceTo(long written) { Written=Math.Max(Written,written); }
        internal bool IsValid(int phase)
        {
            long latest=Written-1;
            long index=latest-((latest-phase)%Period+Period)%Period;
            return index>=0 && stamps[phase]==index+1;
        }
        // The form holds sampleLock while copying. No recording data is changed.
        internal bool CopyFrom(short[,] source,long written,int firstChannel)
        {
            int history=source.GetLength(0), channels=source.GetLength(1);
            if(written<0 || Period>history || firstChannel<0 || firstChannel+64>channels)
                throw new ArgumentOutOfRangeException("written");
            bool reset=!initialized || written<Written;
            if(!reset && written==Written)return false;
            long oldest=Math.Max(0,written-Period);
            long start=reset?oldest:Math.Max(Written,oldest);
            if(reset)Array.Clear(Samples,0,Samples.Length);
            for(long row=start;row<written;row++)
            {
                int sourceRow=(int)(row%history), phase=(int)(row%Period);
                Buffer.BlockCopy(source,(sourceRow*channels+firstChannel)*2,Samples,phase*64*2,64*2);
                stamps[phase]=row+1;
                CopiedRows++;
            }
            Written=written;initialized=true;return true;
        }
    }

    internal sealed class NpSweepPanel : Panel
    {
        internal NpSweepPanel()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|
                ControlStyles.OptimizedDoubleBuffer|ControlStyles.Opaque|ControlStyles.ResizeRedraw,true);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
    }

    // Banked live scope matching USB512's 64-channel display, with independent
    // AP and LFP rings so changing band never compromises recording.
    internal sealed class NeuropixelsPreviewForm : Form
    {
        private const int Channels = XeNpStreamDecoder.Channels;
        private const int ChannelsPerBank = 64;
        private const int HistorySeconds = 2;
        // NP1 decoders emit signed nominal ADC counts after 512 - raw10.
        // Keep the AP reference fixed at zero; NP2 keeps its 12-bit reference.
        private const float NpAdcMidscale = 0f;
        private const float NpDisplayReferenceCounts = 64f;
        // LFP uses frozen per-channel display-only references in signed counts.
        // Zero is the fallback; these references never change recorded samples.
        private const float NpLfpMidscale = 0f;
        private const float NpLfpDisplayReferenceCounts = 16f;
        private readonly NpPreviewQueue previewQueue = new NpPreviewQueue();
        private readonly int apSampleRate;
        private readonly double lfpSampleRate;
        private readonly bool apOnly;
        private readonly int adcBits;
        private readonly float apAdcMidscale;
        private readonly float apDisplayReferenceCounts;
        private long rawBytes, packetCount, apRows, lfpRows, relocks;
        private readonly ComboBox bandBox = new ComboBox();
        private readonly ComboBox probeBox = new ComboBox();
        private readonly ComboBox paletteBox=new ComboBox();
        private readonly Label pageLabel=new Label();
        private readonly NpPreviewPalette[] palettes=NpPreviewPalette.CreateAll();
        private NpPreviewDefinition[] preparedDefinitions;
        private bool definitionsApplied,updatingView;
        private readonly int probeMask;
        private bool physicalPortsKnown=true;
        internal void SetPhysicalPortReporting(bool known) {physicalPortsKnown=known;probeBox.Refresh();UpdateView();}
        internal void PrepareProbeDefinitions(NpPreviewDefinition[] definitions)
        {
            preparedDefinitions=definitions;definitionsApplied=false;UpdateView();
        }
        internal void ConfirmProbeDefinitions() {definitionsApplied=true;UpdateView();}
        private readonly NumericUpDown bankBox = new NumericUpDown();
        private readonly NumericUpDown durationBox = new NumericUpDown();
        private readonly NumericUpDown gainBox = new NumericUpDown();
        private readonly Label report = new Label();
        private readonly NpSweepPanel graph = new NpSweepPanel();
        private readonly System.Windows.Forms.Timer repaintTimer = new System.Windows.Forms.Timer();
        private readonly object renderLock = new object();
        private Bitmap backBuffer;
        private Bitmap renderBuffer;
        private Thread renderThread;
        private volatile bool renderRunning;
        private volatile bool displayActive;
        private readonly object viewLock=new object();
        private struct ViewState { internal bool Lfp; internal int Bank,Generation,Samples,Width,Height,Probe,Palette; internal float Gain; internal NpPreviewDefinition Definition; internal NpPreviewPage Page; }
        private ViewState view;
        private ViewState drawnView;
        private NpSweepBuffer sweep;
        private float[] sweepReferences;
        private volatile int frameVersion;
        private int paintedVersion=-1;
        private int lastReportTick;
        private PointF[] tracePoints;
        private volatile string renderFailure;
        private const int RenderWidth=1280, RenderHeight=900, MapWidth=136;
        private Bitmap mapBuffer;
        private NpPreviewPage mappedPage;
        private int mappedPalette=-1,mappedHeight;
        private NpPreviewDefinition mappedDefinition;
        internal bool KeepAliveWhileRecording { get; set; }
        internal bool MatchesRate(int rate) { return apSampleRate==rate; }
        internal bool MatchesProfile(int rate,int mask) { return MatchesProfile(rate,mask,false,10); }
        internal bool MatchesProfile(int rate,int mask,bool wantedApOnly,int wantedAdcBits)
        { return apSampleRate==rate && probeMask==mask && apOnly==wantedApOnly && adcBits==wantedAdcBits; }
        internal long DroppedPreviewRows { get { return previewQueue.DroppedRows; } }

        internal NeuropixelsPreviewForm(int apSampleRate,int mask=1) : this(apSampleRate,mask,false,10) { }

        // NP2 has one full-rate 384-channel AP stream. Its wire protocol has
        // no independently sampled LFP band, so keep the existing renderer but
        // present an AP-only, 12-bit view instead of inventing an LFP signal.
        internal NeuropixelsPreviewForm(int apSampleRate,int mask,bool apOnly,int adcBits)
        {
            if(mask<1 || mask>7)throw new ArgumentOutOfRangeException("mask");
            if(adcBits<2 || adcBits>15)throw new ArgumentOutOfRangeException("adcBits");
            probeMask=mask;
            this.apSampleRate = apSampleRate;
            this.apOnly=apOnly;this.adcBits=adcBits;
            lfpSampleRate = apOnly ? 0 : apSampleRate / 12.0;
            apAdcMidscale=adcBits==10?NpAdcMidscale:(1<<(adcBits-1));
            apDisplayReferenceCounts=adcBits==10?NpDisplayReferenceCounts:256f;
            Text = apOnly ? "WILD-X NP2.0 Live Preview" : "WILDX Live Preview";
            Icon=NpPreviewTheme.ApplicationIcon;
            DoubleBuffered=true;
            ClientSize = new Size(1180, 780);
            MinimumSize = new Size(840, 450);
            bandBox.DropDownStyle = ComboBoxStyle.DropDownList;
            bandBox.Items.Add("AP / spike band");
            if(!apOnly)bandBox.Items.Add("LFP band");
            bandBox.SelectedIndex = 0;
            bandBox.SetBounds(8, 8, 135, 25);
            probeBox.DropDownStyle=ComboBoxStyle.DropDownList;
            probeBox.FormattingEnabled=true;
            probeBox.Format+=delegate(object sender,ListControlConvertEventArgs e) {e.Value=physicalPortsKnown?"P"+e.ListItem:"Legacy";};
            for(int p=1;p<=3;p++)if((mask&(1<<(p-1)))!=0)probeBox.Items.Add(p);
            probeBox.SelectedIndex=0;probeBox.SetBounds(55,35,88,24);
            probeBox.SelectedIndexChanged+=delegate {UpdateView();};
            bankBox.Minimum = 0; bankBox.Maximum = (Channels / ChannelsPerBank) - 1; bankBox.Value = 0;
            bankBox.SetBounds(245, 8, 45, 25);
            durationBox.Minimum = 0.05M; durationBox.Maximum = HistorySeconds; durationBox.DecimalPlaces = 2;
            durationBox.Increment = 0.05M; durationBox.Value = 0.50M;
            durationBox.SetBounds(375, 8, 60, 25);
            gainBox.Minimum = 0.1M; gainBox.Maximum = 100M; gainBox.DecimalPlaces = 1;
            // Match the previous initial rendered gain, now honestly reflected
            // in the control (the old field started at1 while the box said10).
            gainBox.Increment = 0.1M; gainBox.Value = 1.0M;
            gainBox.SetBounds(495, 8, 60, 25);
            paletteBox.DropDownStyle=ComboBoxStyle.DropDownList;
            foreach(var palette in palettes)paletteBox.Items.Add(palette.Name);
            paletteBox.SelectedIndex=0;paletteBox.SetBounds(210,35,110,24);
            paletteBox.SelectedIndexChanged+=delegate {UpdateView();};
            report.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            report.AutoSize = false; report.TextAlign = ContentAlignment.MiddleLeft;
            report.SetBounds(575, 8, ClientSize.Width-590, 48);
            graph.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            graph.BackColor = NpPreviewTheme.Canvas; graph.BorderStyle = BorderStyle.FixedSingle;
            graph.SetBounds(8, 64, ClientSize.Width - 16, ClientSize.Height - 72);
            graph.Paint += Graph_Paint;
            graph.Resize += delegate { UpdateView(); };
            bandBox.SelectedIndexChanged += delegate
            {
                gainBox.Value = !apOnly && bandBox.SelectedIndex==1 ? 3.0M : 1.0M;
                UpdateView();
            };
            bankBox.ValueChanged += delegate { UpdateView(); };
            durationBox.ValueChanged += delegate { UpdateView(); };
            gainBox.ValueChanged += delegate { UpdateView(); };
            Controls.AddRange(new Control[] {
                bandBox, bankBox, durationBox, gainBox, report, graph,probeBox,paletteBox,pageLabel,
                new Label { Text = "Probe", AutoSize = true, Location = new Point(8, 38) },
                new Label { Text = "Palette", AutoSize = true, Location = new Point(150, 38) },
                new Label { Text = "Sweep (s)", AutoSize = true, Location = new Point(305, 12) },
                new Label { Text = "Gain", AutoSize = true, Location = new Point(450, 12) }
            });
            pageLabel.AutoSize=true;pageLabel.Location=new Point(150,12);
            UpdateView();
            // Large managed/native allocations and first-use rendering happen
            // before capture START, never when a recording preview is opened.
            sweep=new NpSweepBuffer(view.Samples,apSampleRate*HistorySeconds);
            renderBuffer=new Bitmap(RenderWidth,RenderHeight);
            backBuffer=new Bitmap(RenderWidth,RenderHeight);
            renderRunning=true;
            short[] warm=new short[ChannelsPerBank];
            sweep.AddRow(warm,0,0);sweep.AddRow(warm,0,1);
            RenderFrame();
            // Warm-up is display-only; expose no synthetic history.
            sweep.Reset(view.Samples);drawnView.Width=-1;RenderFrame();
            repaintTimer.Interval = 33;
            repaintTimer.Tick += delegate {
                int version=frameVersion;
                if(version!=paintedVersion) { paintedVersion=version;graph.Invalidate(); }
                int now=Environment.TickCount;
                if(unchecked(now-lastReportTick)>=250) { lastReportTick=now;UpdateReport(); }
            };
            repaintTimer.Start();
            renderRunning = true;
            renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "WILDX scope renderer", Priority=ThreadPriority.BelowNormal };
            renderThread.Start();
        }

        internal void PushSamples(short[] apSamples, int apFrames, short[] lfpSamples, int lfpFrames)
        {PushSamples(1,apSamples,apFrames,lfpSamples,lfpFrames);}

        internal void PushSamples(int probe,short[] apSamples, int apFrames, short[] lfpSamples, int lfpFrames)
        {
            if(!renderRunning)return;
            if(displayActive)previewQueue.Push(apSamples,apFrames,lfpSamples,lfpFrames,probe);
            else previewQueue.AdvanceClock(apFrames,lfpFrames,probe);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if(previewQueue!=null && viewLock!=null && graph!=null) {
                previewQueue.InvalidateView();UpdateView();
            }
            displayActive=Visible;
        }

        private static void PrepareHandles(Control control)
        {
            IntPtr ignored=control.Handle;
            foreach(Control child in control.Controls)PrepareHandles(child);
        }

        internal void PrepareForCapture()
        {
            if(InvokeRequired)throw new InvalidOperationException("Prepare NP preview on the UI thread before START.");
            previewQueue.InvalidateView();
            preparedDefinitions=null;definitionsApplied=false;
            PrepareHandles(this);PerformLayout();UpdateView();
            KeepAliveWhileRecording=true;
        }

        internal void UpdateRecordingReport(long bytes, long packets, long apSampleRows, long lfpSampleRows, long resyncs)
        {
            rawBytes = bytes; packetCount = packets; apRows = apSampleRows; lfpRows = lfpSampleRows; relocks = resyncs;
        }

        private void Graph_Paint(object sender, PaintEventArgs e)
        {
            lock (renderLock)
            {
                e.Graphics.Clear(NpPreviewTheme.Canvas);
                if (backBuffer != null)
                    e.Graphics.DrawImage(backBuffer,graph.ClientRectangle,
                        new Rectangle(0,0,Math.Max(1,drawnView.Width),Math.Max(1,drawnView.Height)),GraphicsUnit.Pixel);
                else
                    DrawEmpty(e.Graphics);
            }
        }

        private void UpdateReport()
        {
            bool showLfp = !apOnly && bandBox.SelectedIndex == 1;
            int bank = (int)bankBox.Value;
            NpPreviewPage page=view.Page;bool physical=view.Definition.Physical;
            string range=page.Count==0?"no enabled sites":page.Sites[0].Label+" .. "+page.Sites[page.Count-1].Label;
            report.Text = String.Format((physicalPortsKnown?"P"+probeBox.SelectedItem:"Legacy (port unreported)")+" | {0} | "+
                (physical?"electrode order (applied "+(view.Definition.Np2?"NP2 IMRO":"NPB1")+")":"hardware-channel fallback; mapping unverified")+"\r\nPage {1}: {2}\r\n{4:N1} MiB | {5:N0} packets | AP {6:N0}"+(apOnly?" (12-bit)":" | LFP {7:N0} | relocks {8:N0}"),
                showLfp ? "LFP" : "AP", bank, range, 0,
                rawBytes / 1048576.0, packetCount, apRows, lfpRows, relocks);
            if(renderFailure!=null)report.Text="Preview stopped: "+renderFailure+" (recording continues)";
            else if(DroppedPreviewRows>0)report.Text+=" | display skipped "+DroppedPreviewRows.ToString("N0")+" rows";
        }

        // Match ScopeForm: expensive snapshot and GDI work happen away from the
        // message pump.  The paint event only blits the last completed bitmap.
        private void UpdateView()
        {
            // Called only on the UI thread. Publish one coherent settings copy.
            if(updatingView)return;
            updatingView=true;
            try {
            bool lfpBand=!apOnly && bandBox.SelectedIndex==1;
            lock(viewLock) {
                int probe=probeBox.SelectedItem==null?1:(int)probeBox.SelectedItem;
                NpPreviewDefinition definition=physicalPortsKnown && definitionsApplied && preparedDefinitions!=null &&
                    probe<preparedDefinitions.Length?preparedDefinitions[probe]:null;
                definition=definition??NpPreviewDefinition.Hardware;
                bankBox.Maximum=definition.Pages.Length-1;
                pageLabel.Text=definition.Physical?"Page (64 sites)":"Page (64 ch)";
                NpPreviewPage page=definition.Pages[(int)bankBox.Value];
                previewQueue.Select(lfpBand,(int)bankBox.Value,probe,page.Channels);
                view=new ViewState { Lfp=lfpBand,Probe=probe, Bank=(int)bankBox.Value, Generation=previewQueue.Generation,
                    Samples=(int)Math.Round((double)durationBox.Value*(lfpBand?lfpSampleRate:apSampleRate)),
                    Gain=(float)gainBox.Value, Width=Math.Min(RenderWidth,graph.ClientSize.Width),
                    Height=Math.Min(RenderHeight,graph.ClientSize.Height),Definition=definition,Page=page,
                    Palette=Math.Max(0,paletteBox.SelectedIndex) };
            }
            UpdateReport();
            } finally {updatingView=false;}
        }

        private void RenderLoop()
        {
            try {
                var timer=new Stopwatch();
                while(renderRunning) {
                    timer.Restart();if(displayActive)RenderFrame();
                    // Bound display CPU duty cycle; never spin at full CPU if
                    // a large/maximized window takes longer than its budget.
                    Thread.Sleep(Math.Max(20,50-(int)timer.ElapsedMilliseconds));
                }
            } catch(Exception ex) {
                renderFailure=ex.Message;renderRunning=false;
            } finally {
                if(renderBuffer!=null) { renderBuffer.Dispose();renderBuffer=null; }
                if(mapBuffer!=null) {mapBuffer.Dispose();mapBuffer=null;}
                foreach(var palette in palettes)palette.Dispose();
            }
        }

        private void RenderFrame()
        {
            ViewState settings;
            lock(viewLock)settings=view;
            if(settings.Width<40 || settings.Height<20)return;
            int period=(int)Math.Ceiling(Math.Min((settings.Lfp?lfpSampleRate:apSampleRate)*HistorySeconds,Math.Max(2,settings.Samples)));
            bool reset=sweep==null || sweep.Period!=period || settings.Generation!=drawnView.Generation;
            if(sweep==null)throw new InvalidOperationException("Preview history was not prepared before capture.");
            if(reset) { sweep.Reset(period);sweepReferences=null; }
            long oldWritten=sweep.Written;
            // Read the producer clock BEFORE taking its published queue limit.
            // Missing display rows remain blank, with the true sample phase.
            long written=previewQueue.Written(settings.Lfp,settings.Probe);
            bool changed=previewQueue.DrainTo(sweep,settings.Generation);
            sweep.AdvanceTo(written);changed|=sweep.Written!=oldWritten;
            if(!changed && !reset && settings.Width==drawnView.Width &&
               settings.Height==drawnView.Height && settings.Gain==drawnView.Gain && settings.Palette==drawnView.Palette)return;
            if(settings.Lfp && sweepReferences==null && sweep.CopiedRows>=2)
                sweepReferences=CaptureSweepReferences(sweep);
            if(renderBuffer==null)throw new InvalidOperationException("Preview surface was not prepared before capture.");
            if(mapBuffer==null || mappedPage!=settings.Page || mappedPalette!=settings.Palette ||
               mappedHeight!=settings.Height || mappedDefinition!=settings.Definition) {
                if(mapBuffer!=null)mapBuffer.Dispose();
                mapBuffer=new Bitmap(MapWidth,settings.Height);
                using(Graphics g=Graphics.FromImage(mapBuffer))
                    DrawElectrodeMap(g,settings.Definition,settings.Page,palettes[settings.Palette],MapWidth,settings.Height);
                mappedPage=settings.Page;mappedPalette=settings.Palette;mappedHeight=settings.Height;mappedDefinition=settings.Definition;
            }
            int columns=Math.Max(1,Math.Min(600,settings.Width-MapWidth-94));
            int pointCount=period<=columns?period:columns*4;
            if(tracePoints==null || tracePoints.Length!=pointCount)tracePoints=new PointF[pointCount];
            using(Graphics g=Graphics.FromImage(renderBuffer)) {
                g.Clear(NpPreviewTheme.Canvas);g.TranslateTransform(MapWidth,0);
                DrawSweepBank(g,sweep,0,settings.Gain,
                    settings.Lfp?NpLfpMidscale:apAdcMidscale,
                    settings.Lfp?NpLfpDisplayReferenceCounts:apDisplayReferenceCounts,
                    sweepReferences,settings.Width-MapWidth,settings.Height,tracePoints,settings.Page,palettes[settings.Palette]);
                g.ResetTransform();g.DrawImageUnscaled(mapBuffer,0,0);
            }
            lock(renderLock) {
                if(!renderRunning)return;
                Bitmap old=backBuffer;backBuffer=renderBuffer;renderBuffer=old;
                drawnView=settings;frameVersion++;
            }
        }

        // Freeze display-only LFP offsets for this view. Continually moving the
        // extrema-based centre made every channel jump on every redraw.
        internal static float[] CaptureSweepReferences(NpSweepBuffer sweep)
        {
            if(sweep.ValidRows<1)throw new ArgumentException("No samples to centre.");
            float[] result=new float[ChannelsPerBank];
            for(int ch=0;ch<ChannelsPerBank;ch++) {
                short low=short.MaxValue,high=short.MinValue;
                for(int row=1;row<sweep.ValidRows;row++) {
                    if(!sweep.IsValid(row))continue;
                    short value=sweep.Samples[row*ChannelsPerBank+ch];
                    if(value<low)low=value;
                    if(value>high)high=value;
                }
                if(sweep.IsValid(0)) { low=Math.Min(low,sweep.Samples[ch]);high=Math.Max(high,sweep.Samples[ch]); }
                result[ch]=low<=high?(low+high)*0.5f:NpLfpMidscale;
            }
            return result;
        }

        internal static void DrawElectrodeMap(Graphics g,NpPreviewDefinition definition,NpPreviewPage page,
            NpPreviewPalette palette,int width,int height)
        {
            g.Clear(NpPreviewTheme.Canvas);
            using(Font font=new Font("Arial",8f))using(Font small=new Font("Arial",7f)) {
                g.DrawString(definition.Physical?(definition.Np2?"NP2 electrode map":"NP1 electrode map"):"Hardware channels",font,NpPreviewTheme.Text,7,7);
                g.DrawString(definition.Physical?(definition.Np2?"S0–S3 · 0-based\nLayout schematic":"S0 · sites (0-based)\nLayout schematic"):"Electrode routing\nunverified",small,NpPreviewTheme.Secondary,7,26);
                if(definition.Physical) {
                    int total=definition.Np2?1280:960,shanks=definition.Np2?4:1,pitch=definition.Np2?15:20;
                    float top=70,bottom=height-63,vertical=Math.Max(1,bottom-top)/((total/2-1)*pitch);
                    float left=definition.Np2?32:40,horizontal=definition.Np2?(width-48)/782f:1.1f;
                    g.DrawString((total-1).ToString(),small,NpPreviewTheme.Secondary,3,top-9);
                    g.DrawString("0",small,NpPreviewTheme.Secondary,5,bottom-4);
                    // The contact layout is a schematic: expand X for readable
                    // contacts while retaining the configurator's stagger/rows.
                    for(int shank=0;shank<shanks;shank++)for(int site=0;site<total;site++) {
                        int row=site/2;float x=left+(shank*250+site%2*32+(!definition.Np2 && row%2==0?16:0))*horizontal;
                        float y=bottom-row*pitch*vertical;
                        g.FillRectangle(NpPreviewTheme.Inactive,x-1,y-1,2,2);
                    }
                    for(int i=0;i<page.Count;i++) {
                        NpPreviewSite site=page.Sites[i];float x=left+site.XUm*horizontal,y=bottom-site.YUm*vertical;
                        g.FillRectangle(palette.Brushes[i],x-2,y-1.5f,4,3);
                    }
                    if(definition.Np2)for(int shank=0;shank<4;shank++)g.DrawString("S"+shank,small,NpPreviewTheme.Secondary,left+shank*250*horizontal-2,bottom+7);
                    g.DrawString((definition.Np2?"Applied NP2 IMRO":"Applied NPB1")+"\n"+definition.ConfigurationId,small,NpPreviewTheme.Secondary,7,height-47);
                } else {
                    float cellW=(width-16)/16f,cellH=Math.Max(4,(height-115)/24f);
                    for(int ch=0;ch<384;ch++) {
                        int display=-1;for(int i=0;i<page.Count;i++)if(page.Channels[i]==ch){display=i;break;}
                        g.FillRectangle(display<0?NpPreviewTheme.Inactive:palette.Brushes[display],
                            8+(ch%16)*cellW,65+(ch/16)*cellH,Math.Max(2,cellW-1),Math.Max(2,cellH-1));
                    }
                    g.DrawString("C0 → C383\nHardware-channel\nfallback",small,NpPreviewTheme.Secondary,7,height-46);
                }
                g.DrawString("Highlighted: "+page.Count,small,NpPreviewTheme.Secondary,7,height-13);
            }
        }

        internal static void DrawSweepBank(Graphics g,NpSweepBuffer sweep,int firstChannel,
            float gain,float referenceCounts,float displayReferenceCounts,float[] references,
            int width,int heightPixels,PointF[] scratch=null,NpPreviewPage page=null,NpPreviewPalette palette=null)
        {
            g.Clear(NpPreviewTheme.Canvas);
            if(sweep.ValidRows<2) {
                using(Font waitingFont=new Font("Arial",9f))
                    g.DrawString("Waiting for decoded WILDX samples...",waitingFont,NpPreviewTheme.Secondary,10,10);
                return;
            }
            float left=page==null?34f:92f;
            float traceWidth=width-left-2f, height=heightPixels/(float)ChannelsPerBank;
            int rows=sweep.Period, columns=Math.Max(1,Math.Min(600,(int)traceWidth));
            float scale=height/(2f*displayReferenceCounts)*gain;
            float cursor=left+sweep.Cursor*traceWidth/(rows-1);
            // Blank the complete epoch-joining segment, including sparse views.
            float gap=Math.Max(6f,2f*traceWidth/Math.Max(1,Math.Min(rows,columns)-1));
            PointF[] points=scratch??new PointF[rows<=columns?rows:columns*4];
            using(Pen pen=palette==null?new Pen(NpPreviewTheme.Blue,1f):null)
            using(Pen grid=new Pen(NpPreviewTheme.Grid,1f))
            using(Pen sweepPen=new Pen(NpPreviewTheme.Sweep,1f))
            using(Font font=new Font("Arial",7f))
            {
                for(int ch=0;ch<ChannelsPerBank;ch++) {
                    if(page!=null && ch>=page.Count)continue;
                    float baseline=(ch+.5f)*height;
                    g.DrawLine(grid,left,baseline,width,baseline);
                    g.DrawString(page==null?(firstChannel+ch).ToString():page.Sites[ch].Label,font,NpPreviewTheme.Text,1,baseline-6);
                }
                GraphicsState saved=g.Save();
                using(Region clip=new Region(new RectangleF(left,0,traceWidth,heightPixels)))
                {
                    if(sweep.ValidRows<rows) {
                        float lastValid=left+(sweep.ValidRows-1)*traceWidth/(rows-1);
                        float until=Math.Min(lastValid,cursor-gap);
                        clip.Intersect(new RectangleF(left,0,Math.Max(0,until-left),heightPixels));
                    } else clip.Exclude(new RectangleF(cursor-gap,0,gap*2,heightPixels));
                    // Never connect across dropped preview rows or show stale
                    // data from a previous bank. Include the reduction bucket
                    // on either side so an invalid extremum cannot leak out.
                    for(int row=0;row<rows;) {
                        if(sweep.IsValid(row)) { row++;continue; }
                        int first=row++;
                        while(row<rows && !sweep.IsValid(row))row++;
                        float x=left+first*traceWidth/(rows-1)-gap;
                        float end=left+(row-1)*traceWidth/(rows-1)+gap;
                        clip.Exclude(new RectangleF(x,0,end-x,heightPixels));
                    }
                    g.SetClip(clip,CombineMode.Intersect);
                    for(int ch=0;ch<ChannelsPerBank;ch++) {
                        if(page!=null && ch>=page.Count)continue;
                        float baseline=(ch+.5f)*height;
                        FillConnectedTrace(points,sweep.Samples,rows,ch,columns,left,traceWidth,
                            baseline,references==null?referenceCounts:references[ch],scale);
                        g.DrawLines(palette==null?pen:palette.Pens[ch],points);
                    }
                }
                g.Restore(saved);
                g.DrawLine(sweepPen,cursor,0,cursor,heightPixels);
            }
        }

        private void DrawEmpty(Graphics g)
        {
            g.Clear(NpPreviewTheme.Canvas);
            g.DrawString("Waiting for decoded WILDX samples...", Font, NpPreviewTheme.Secondary, 10, 10);
        }

        // Pure geometry/rendering entry points also support offscreen tests.
        internal static void FillConnectedTrace(PointF[] points, short[] samples,
            int rows, int channel, int columns, float left, float traceWidth,
            float baseline, float reference, float scale)
        {
            if(rows<2 || columns<1 || channel<0 || channel>=ChannelsPerBank)
                throw new ArgumentOutOfRangeException("rows");
            int count=rows<=columns?rows:checked(columns*4);
            if(samples==null || samples.Length<checked(rows*ChannelsPerBank) || points==null || points.Length!=count)
                throw new ArgumentException("Trace buffers do not match the sample geometry.");
            float dx=traceWidth/(rows-1);
            if(rows<=columns)
            {
                for(int row=0;row<rows;row++)
                    points[row]=TracePoint(samples,row,channel,left,dx,baseline,reference,scale);
                return;
            }
            // Preserve narrow spikes when zoomed out, but connect extrema in
            // their actual time order. Include bucket endpoints so adjacent
            // buckets join naturally instead of drawing isolated min/max bars.
            for(int col=0;col<columns;col++)
            {
                int first=(int)((long)col*rows/columns);
                int last=(int)((long)(col+1)*rows/columns)-1;
                int low=first, high=first;
                short lowValue=samples[first*ChannelsPerBank+channel], highValue=lowValue;
                for(int row=first+1;row<=last;row++)
                {
                    short value=samples[row*ChannelsPerBank+channel];
                    if(value<lowValue) { lowValue=value;low=row; }
                    if(value>highValue) { highValue=value;high=row; }
                }
                int at=col*4;
                points[at]=TracePoint(samples,first,channel,left,dx,baseline,reference,scale);
                points[at+1]=TracePoint(samples,Math.Min(low,high),channel,left,dx,baseline,reference,scale);
                points[at+2]=TracePoint(samples,Math.Max(low,high),channel,left,dx,baseline,reference,scale);
                points[at+3]=TracePoint(samples,last,channel,left,dx,baseline,reference,scale);
            }
        }

        private static PointF TracePoint(short[] samples, int row, int channel,
            float left, float dx, float baseline, float reference, float scale)
        {
            return new PointF(left+row*dx,baseline-(samples[row*ChannelsPerBank+channel]-reference)*scale);
        }

        internal static void DrawBank(Graphics g, short[] samples, int rows, int firstChannel, float gain,
                              float referenceCounts, float displayReferenceCounts,
                              bool autoCentreForDisplay, int width, int heightPixels)
        {
            g.Clear(NpPreviewTheme.Canvas);
            float height = heightPixels / (float)ChannelsPerBank;
            float left = 34f, traceWidth = width - left - 2f;
            int columns = Math.Max(1, Math.Min(900, (int)traceWidth));
            float scale = height / (2f * displayReferenceCounts) * gain;
            // Reuse one bounded point buffer across all 64 channels. Drawing
            // one polyline/channel also avoids thousands of GDI calls/frame.
            PointF[] points=new PointF[rows<=columns?rows:columns*4];
            using (Pen pen = new Pen(NpPreviewTheme.Blue, 1f))
            using (Pen grid = new Pen(NpPreviewTheme.Grid, 1f))
            using (Font font = new Font("Arial", 7f))
            {
                for (int ch = 0; ch < ChannelsPerBank; ch++)
                {
                    float baseline = (ch + .5f) * height;
                    float channelReference = referenceCounts;
                    if (autoCentreForDisplay)
                    {
                        /* The NP1 lane8 LFP data have a channel-dependent DC
                         * pedestal.  Centre only the visualization on its
                         * current extrema; the raw/decoded recording remains
                         * untouched and a stopped capture stays visually fixed. */
                        short low = samples[ch], high = low;
                        for (int row = 1; row < rows; row++)
                        {
                            short value = samples[row * ChannelsPerBank + ch];
                            if (value < low) low = value;
                            if (value > high) high = value;
                        }
                        channelReference = (low + high) * 0.5f;
                    }
                    g.DrawLine(grid, left, baseline, width, baseline);
                    g.DrawString((firstChannel + ch).ToString(), font, NpPreviewTheme.Text, 1, baseline - 6);
                    FillConnectedTrace(points,samples,rows,ch,columns,left,traceWidth,
                        baseline,channelReference,scale);
                    g.DrawLines(pen,points);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if(KeepAliveWhileRecording && e.CloseReason==CloseReason.UserClosing) {
                e.Cancel=true;Hide();return;
            }
            renderRunning = false;
            repaintTimer.Stop();
            if (renderThread != null && renderThread != Thread.CurrentThread) renderThread.Join(1000);
            lock (renderLock) { if (backBuffer != null) { backBuffer.Dispose(); backBuffer = null; } }
            base.OnFormClosing(e);
        }
    }
}
