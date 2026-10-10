using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USB512_interface
{
    internal sealed class XeNpSdInfo
    {
        internal const int WireBytes=384;
        internal uint State, Sequence, Tick, Error, Flags, ClockHz, BusWidth, ActiveFunction, BudgetMa, Ocr, InitStage;
        internal ulong Sectors;
        internal byte[] Raw;
        internal static uint U32(byte[] b,int i) { return (uint)(b[i]|b[i+1]<<8|b[i+2]<<16|b[i+3]<<24); }
        internal static int BE16(byte[] b,int i) { return b[i]*256+b[i+1]; }
        internal static XeNpSdInfo Parse(byte[] b,int count)
        {
            if(b==null || count!=WireBytes || count>b.Length || U32(b,0)!=0x53444946 ||
                U32(b,4)!=1 || U32(b,8)!=WireBytes || U32(b,12)>3)
                throw new IOException("Unsupported or incomplete WILDX SD Info response. Matching USB-service firmware is required.");
            var info=new XeNpSdInfo { State=U32(b,12),Sequence=U32(b,16),Tick=U32(b,20),
                Error=U32(b,24),Flags=U32(b,28),Sectors=U32(b,32)|((ulong)U32(b,36)<<32),
                ClockHz=U32(b,40),BusWidth=U32(b,44),ActiveFunction=U32(b,48),
                BudgetMa=U32(b,52),Ocr=U32(b,56),InitStage=U32(b,60),Raw=new byte[WireBytes] };
            Buffer.BlockCopy(b,0,info.Raw,0,WireBytes);
            uint[] arguments={0x00ffffff,0x00ff0ff2,0x00ff0ff3};
            for(int i=0;i<3;i++)if(U32(b,168+i*72)!=arguments[i])
                throw new IOException("Unexpected SD Info query argument; refusing to label its power data.");
            return info;
        }
        internal static string Mode(uint mode,bool uhs)
        {
            string[] modes=uhs?new[] {"SDR12","SDR25","SDR50","SDR104","DDR50"}:
                new[] {"Default speed","High speed"};
            return mode<modes.Length?modes[(int)mode]:"Unknown/reserved ("+mode+")";
        }
        internal static string Current(int value)
        {
            return value==0?"Not reported / error (0)":value+" mA (reported, not measured)";
        }
        internal string SummaryState
        {
            get {
                if(State!=2)return XeNpSdInfoTree.State(State);
                for(int i=0;i<3;i++)if(U32(Raw,172+i*72)!=0)
                    return "Partial - mode checks unavailable";
                return "Complete";
            }
        }
        internal string SignalingDescription
        {
            get {
                if((Flags&2)==0)return "3.3 V selected";
                if((Flags&32)!=0)return "1.8 V - retained UHS state recovered";
                if((Flags&64)!=0)return "1.8 V - preserved session";
                return "1.8 V negotiated";
            }
        }
        internal string Hex(int offset,int count) { return BitConverter.ToString(Raw,offset,count).Replace("-",""); }
        internal string Ascii(int offset,int count)
        {
            var s=new StringBuilder();
            for(int i=offset;i<offset+count;i++)s.Append(Raw[i]>=32 && Raw[i]<127?(char)Raw[i]:'?');
            return s.ToString();
        }
        internal string Report()
        {
            var s=new StringBuilder();
            bool uhs=(Flags&2)!=0;
            s.AppendLine("WILDX SD INFO - CARD-REPORTED DATA, NOT A CURRENT MEASUREMENT");
            s.AppendLine("Snapshot "+Sequence+"; device uptime "+Tick+" ms; "+SummaryState.ToLowerInvariant());
            s.AppendLine("Error: 0x"+Error.ToString("X8")+"; initialization stage: "+InitStage);
            s.AppendLine("Diagnostic identification only. No sectors written; no high-speed mode selected.");
            s.AppendLine("This is NOT the separate SD recording application's operating configuration.");
            s.AppendLine();
            if((Flags&1)==0) {
                s.AppendLine("Card identity unavailable. Check the card, firmware and power; absent and failed cards cannot always be distinguished.");
                s.AppendLine("OCR: 0x"+Ocr.ToString("X8"));
                return s.ToString();
            }
            s.AppendLine("Product: "+Ascii(67,5)+"; manufacturer ID: 0x"+Raw[64].ToString("X2")+"; OEM: "+Ascii(65,2));
            s.AppendLine("Revision: "+(Raw[72]>>4)+"."+(Raw[72]&15)+"; serial: "+Hex(73,4));
            int date=((Raw[77]&15)<<8)|Raw[78];
            s.AppendLine("Manufactured: "+((date&15)>=1 && (date&15)<=12?
                (2000+(date>>4))+"-"+(date&15).ToString("D2"):"unknown / reserved date"));
            s.AppendLine("Capacity: "+(Sectors*512.0/1000000000.0).ToString("F3",CultureInfo.InvariantCulture)+
                " GB ("+Sectors+" x 512-byte sectors)");
            s.AppendLine("Query-session bus: "+Mode(ActiveFunction,uhs)+", "+BusWidth+"-bit, "+
                (ClockHz/1000000.0).ToString("F3",CultureInfo.InvariantCulture)+" MHz");
            s.AppendLine("Signaling: "+SignalingDescription+" (not measured); card VDD design: 3.3 V (not measured)");
            if((Flags&32)!=0)s.AppendLine("Retained UHS state was re-identified at 1.8 V and checked against the same CID; no CMD11 was forced.");
            if((Flags&64)!=0)s.AppendLine("The powered card's signaling selection was preserved across host reinitialization.");
            s.AppendLine("Board card-VDD budget: "+BudgetMa+" mA; UHS policy: function 0 / 200 mA minimum class.");
            s.AppendLine("No new current-limit selection was applied. The minimum class is not an instantaneous current clamp.");
            s.AppendLine("OCR: 0x"+Ocr.ToString("X8")+"; ready="+((Ocr>>31)&1)+"; CCS="+((Ocr>>30)&1)+"; S18A="+((Ocr>>24)&1));
            s.AppendLine("Host requested 1.8 V signaling: "+(((Flags&16)!=0)?"yes":"no"));
            if(!uhs)s.AppendLine("UHS power checks unavailable in this identification session; advertised support below is not the active mode.");
            s.AppendLine("CID: "+Hex(64,16));s.AppendLine("CSD: "+Hex(80,16));
            s.AppendLine("SCR: "+((Flags&4)!=0?Hex(96,8):"unavailable"));
            s.AppendLine("SD Status: "+((Flags&8)!=0?Hex(104,64):"unavailable"));
            s.AppendLine();
            string[] labels={"Current functions (no influence)","Proposed SDR50 / 200 mA class","Proposed SDR104 / 200 mA class"};
            for(int i=0;i<3;i++) {
                int row=168+i*72, data=row+8;
                uint error=U32(Raw,row+4);
                s.AppendLine(labels[i]+" - CMD6 CHECK 0x"+U32(Raw,row).ToString("X8"));
                if(error!=0) { s.AppendLine(error==0xffffffff?"  Not queried":"  Failed: 0x"+error.ToString("X8"));continue; }
                int support=BE16(Raw,data+12), power=BE16(Raw,data+6);
                var modes=new System.Collections.Generic.List<string>();
                for(uint m=0;m<5U;m++)if((support&(1<<((int)m)))!=0)modes.Add(Mode(m,uhs || m>=2U));
                s.AppendLine("  Supported bus functions: "+String.Join(", ",modes)+" [mask 0x"+support.ToString("X4")+"]");
                s.AppendLine("  Supported current-limit classes: "+PowerClasses(power)+" [mask 0x"+power.ToString("X4")+"]");
                int resultMode=Raw[data+16]&15, resultPower=Raw[data+15]>>4;
                s.AppendLine("  Check result: group1="+resultMode+", group4="+resultPower+" (check results do not switch functions)");
                if(i!=0 && ((support&(1<<(i+1)))==0 || resultMode!=i+1 || (power&1)==0 || resultPower!=0))
                    s.AppendLine("  Proposal not confirmed by the response; its current value must not be attributed to that proposal.");
                s.AppendLine("  Maximum-current field: "+Current(BE16(Raw,data)));
                s.AppendLine("  Raw CMD6: "+Hex(data,64));
            }
            s.AppendLine();
            s.AppendLine("IMPORTANT: CMD6 mode 0 reports a proposed mode's maximum current irrespective of its current-limit setting.");
            s.AppendLine("The field represents a 1-second average specification, NOT actual draw or short write-current peaks.");
            s.AppendLine("A 400/800 mA report does not prove that much was drawn, nor that a 200 mA LDO is sufficient.");
            s.AppendLine("Cards may return conservative or inconsistent current fields. Even a 100 mA response does not establish a safe peak-current budget.");
            s.AppendLine("Supported SDR104 does not prove stable SDR104 recording. Measure the rail and qualify sustained writes separately.");
            return s.ToString();
        }
        private static string PowerClasses(int mask)
        {
            var names=new System.Collections.Generic.List<string>();
            for(int i=0;i<4;i++)if((mask&(1<<i))!=0)names.Add(((i+1)*200)+" mA");
            return names.Count==0?"none reported":String.Join(", ",names);
        }
    }

    // Typed presentation of the existing snapshot; never parse the human-readable
    // report or issue USB commands when nodes are expanded/selected.
    internal sealed class XeNpSdAttribute
    {
        internal readonly string Name,Value,Description;
        internal XeNpSdAttribute(string name,string value,string description)
        { Name=name;Value=value;Description=description; }
    }

    internal static class XeNpSdInfoTree
    {
        internal const string CurrentCaution="Card-reported field, not a measurement. CMD6 mode 0 describes a proposed mode irrespective of its current-limit setting. The specification is a 1-second average, not short write-current peaks. Even 100 mA does not establish a safe peak-current budget.";
        private static TreeNode Node(string key,string name,string value,string description="",int icon=1)
        {
            string shortValue=value.Replace("\r"," ").Replace("\n"," ");
            if(shortValue.Length>65)shortValue=shortValue.Substring(0,62)+"...";
            return new TreeNode(name+(value.Length==0?"":" : "+shortValue),icon,icon) {
                Name=key,Tag=new XeNpSdAttribute(name,value,description),ToolTipText=description };
        }
        private static TreeNode Add(TreeNode parent,string key,string name,string value,string description="",int icon=1)
        { var node=Node(key,name,value,description,icon);parent.Nodes.Add(node);return node; }
        private static string Hex(byte[] raw,int offset,int bytes)
        { return BitConverter.ToString(raw,offset,bytes).Replace("-",""); }
        private static string Ascii(byte[] raw,int offset,int count)
        {
            var text=new StringBuilder();
            for(int i=offset;i<offset+count;i++)text.Append(raw[i]>=32 && raw[i]<127?(char)raw[i]:'?');
            return text.ToString();
        }
        private static string Yes(bool value) { return value?"Yes":"No"; }
        private static void Register(TreeNode parent,string key,string name,byte[] raw,int offset,int length,bool valid)
        {
            var register=Add(parent,key,name,valid?length+" bytes":"Unavailable",
                valid?"Raw SD wire-order bytes. Select a byte range to copy its complete value.":"This register was not successfully read in this snapshot.",valid?1:2);
            if(!valid)return;
            Add(register,"value","Complete value",Hex(raw,offset,length));
            for(int i=0;i<length;i+=16) {
                int count=Math.Min(16,length-i);
                Add(register,"bytes"+i,"Bytes "+i+"-"+(i+count-1),BitConverter.ToString(raw,offset+i,count).Replace("-"," "));
            }
        }
        internal static string State(uint state)
        { return state==0?"Not queried":state==1?"Query in progress":state==2?"Complete":"Failed / partial"; }
        internal static TreeNode Build(XeNpSdInfo info,string path,DateTime receivedAt)
        {
            bool identified=(info.Flags&1)!=0,uhs=(info.Flags&2)!=0;
            byte[] raw=info.Raw;
            string product=identified?Ascii(raw,67,5):"Identity unavailable";
            var root=Node("sd","SD card",product,
                "Read-only card properties. Expand a category, then select an attribute for its full value and meaning. This snapshot is not the separate SD recording application's configuration.",0);
            var identity=Add(root,"identity","Device identity","", "Card identity from this snapshot's CID and capacity fields.");
            if(identified) {
                Add(identity,"product","Product",product);
                Add(identity,"manufacturer","Manufacturer ID","0x"+raw[64].ToString("X2"));
                Add(identity,"oem","OEM",Ascii(raw,65,2));
                Add(identity,"revision","Revision",(raw[72]>>4)+"."+(raw[72]&15));
                Add(identity,"serial","Serial number",Hex(raw,73,4));
                int date=((raw[77]&15)<<8)|raw[78],month=date&15;
                Add(identity,"date","Manufactured",month>=1 && month<=12?
                    (2000+(date>>4))+"-"+month.ToString("D2"):"Unknown / reserved date");
                Add(identity,"capacity","Capacity",(info.Sectors*512.0/1000000000.0).ToString("F3",CultureInfo.InvariantCulture)+" GB");
                Add(identity,"sectors","Logical sectors",info.Sectors.ToString(CultureInfo.InvariantCulture));
                Add(identity,"sectorbytes","Logical sector size","512 bytes");
            } else Add(identity,"unavailable","Status","Unavailable",
                "No valid card identity was obtained. A missing card and an initialization/power failure cannot always be distinguished.",2);

            var bus=Add(root,"bus","Connection / query session","",
                "The temporary safe-clock identification session, not the recording application's operating mode. The SD host is deinitialized after the query.");
            Add(bus,"mode","Bus mode",identified?XeNpSdInfo.Mode(info.ActiveFunction,uhs):"Unavailable");
            Add(bus,"width","Bus width",identified?info.BusWidth+"-bit":"Unavailable");
            Add(bus,"clock","Bus clock",identified?(info.ClockHz/1000000.0).ToString("F3",CultureInfo.InvariantCulture)+" MHz":"Unavailable");
            Add(bus,"signal","Signaling",identified?info.SignalingDescription:"Unavailable","Protocol/controller state, not a voltage measurement. S18A=0 can also indicate an already-switched UHS card.");
            Add(bus,"requested18","Host requested 1.8 V",identified?Yes((info.Flags&16)!=0):"See initialization diagnostics");
            if(identified && !uhs)Add(bus,"uhschecks","UHS current checks","Not queried",
                "A 1.8 V session was not established/verified. S18A=0 can also mean retained UHS state, not simply refusal. No current value was collected for these modes.",2);
            var ocr=Add(bus,"ocr","OCR","0x"+info.Ocr.ToString("X8"),"Raw initialization response; may be partial on a failed query.");
            Add(ocr,"ready","Ready (bit 31)",Yes((info.Ocr&0x80000000)!=0));
            Add(ocr,"ccs","High-capacity addressing (CCS)",Yes((info.Ocr&0x40000000)!=0));
            Add(ocr,"s18a","1.8 V accepted (S18A)",Yes((info.Ocr&0x01000000)!=0));

            var power=Add(root,"power","Power information","Reported values only",CurrentCaution,2);
            Add(power,"budget","Board card-VDD budget",info.BudgetMa!=0?info.BudgetMa+" mA":"Unavailable",
                "Firmware's board budget, not measured consumption or guaranteed available transient current.");
            Add(power,"supply","Card VDD design","3.3 V (not measured)","The card supply remains separate from the signaling voltage.");
            Add(power,"policy","UHS firmware policy","Function 0 / 200 mA minimum class",
                "No new current-limit selection is applied by SD Info. The minimum class is not an instantaneous current clamp.");
            Add(power,"actual","Actual consumption","Not measured",CurrentCaution,2);
            Add(power,"peak","Peak write current","Not measured","Requires current-measurement hardware; card capability data cannot establish rail peak margin.",2);
            Add(power,"meaning","CMD6 current interpretation","1-second average specification",CurrentCaution,2);

            var checks=Add(root,"checks","CMD6 function checks","",
                "Mode 0 checks only. Expanding these nodes never selects a function, changes power settings or accesses the card.");
            string[] names={"Current functions (no change)","Proposed SDR50 / 200 mA class","Proposed SDR104 / 200 mA class"};
            for(int i=0;i<3;i++) {
                int row=168+i*72,data=row+8;
                uint error=XeNpSdInfo.U32(raw,row+4);
                bool valid=identified && error==0;
                string status=!identified?"Unavailable":error==0xffffffff?"Not queried":error!=0?"Failed":"Response received";
                var check=Add(checks,"check"+i,names[i],status,
                    "A successful check response is not a speed switch or a recording-performance qualification.",valid?1:2);
                Add(check,"argument","Command argument","0x"+XeNpSdInfo.U32(raw,row).ToString("X8"),"CMD6 mode 0 (check); bit 31 is clear.");
                Add(check,"status","Response status",status);
                Add(check,"error","Error",error==0xffffffff?"Not queried":"0x"+error.ToString("X8"));
                if(!valid)continue; // Never turn failed/partial bytes into capability claims.
                int support=XeNpSdInfo.BE16(raw,data+12),current=XeNpSdInfo.BE16(raw,data+6);
                int resultMode=raw[data+16]&15,resultPower=raw[data+15]>>4;
                Add(check,"maximum","Maximum-current field",XeNpSdInfo.Current(XeNpSdInfo.BE16(raw,data)),CurrentCaution,2);
                if(i!=0 && ((support&(1<<(i+1)))==0 || resultMode!=i+1 || (current&1)==0 || resultPower!=0))
                    Add(check,"proposal","Proposal confirmation","Not confirmed",
                        "The response does not confirm this proposal. Do not attribute its current value to the proposed mode.",2);
                Add(check,"resultmode","Group 1 check result",resultMode.ToString(),"Check response nibble; not a newly selected active bus mode.");
                Add(check,"resultpower","Group 4 check result",resultPower.ToString(),"Check response nibble; not a newly selected current limit.");
                var modes=Add(check,"modes","Supported bus functions","Mask 0x"+support.ToString("X4"),"Advertised capabilities, not proof of stable operation on this board.");
                for(uint mode=0;mode<5;mode++)Add(modes,"mode"+mode,XeNpSdInfo.Mode(mode,uhs || mode>=2),
                    (support&(1<<(int)mode))!=0?"Supported":"Not reported as supported");
                var classes=Add(check,"classes","Supported current-limit classes","Mask 0x"+current.ToString("X4"),"Capability bits, not measurements or enforced instantaneous current clamps.");
                for(int cls=0;cls<4;cls++)Add(classes,"class"+cls,((cls+1)*200)+" mA (function "+cls+")",
                    (current&(1<<cls))!=0?"Supported":"Not reported as supported");
                Register(check,"raw","Raw CMD6 response",raw,data,64,true);
            }
            var registers=Add(root,"registers","Raw card registers","","SD wire byte order. Failed or unread registers are unavailable, not zero-filled valid data.");
            Register(registers,"cid","CID",raw,64,16,identified);
            Register(registers,"csd","CSD",raw,80,16,identified);
            Register(registers,"scr","SCR",raw,96,8,identified && (info.Flags&4)!=0);
            Register(registers,"ssr","SD Status",raw,104,64,identified && (info.Flags&8)!=0);
            var diagnostic=Add(root,"diagnostics","Snapshot / diagnostics",info.SummaryState,"Cached snapshot; refresh explicitly after reseating or changing the card.");
            Add(diagnostic,"state","Status",info.SummaryState,"",info.SummaryState=="Complete"?1:2);
            Add(diagnostic,"sequence","Sequence",info.Sequence.ToString());
            Add(diagnostic,"tick","Device completion uptime",info.Tick+" ms");
            Add(diagnostic,"received","Read on PC",receivedAt.ToString("O"));
            Add(diagnostic,"error","HAL error","0x"+info.Error.ToString("X8"));
            Add(diagnostic,"stage","Initialization stage",info.InitStage.ToString());
            Add(diagnostic,"validity","Validity flags","0x"+info.Flags.ToString("X8"),"1=identity; 2=1.8 V selected; 4=SCR; 8=SD Status; 16=1.8 V requested; 32=retained UHS recovered; 64=selector preserved; 128=UHS checks skipped.");
            Add(diagnostic,"version","Protocol version","1 / 384 bytes");
            Add(diagnostic,"device","USB device path",path);
            return root;
        }
        internal static string Key(TreeNode node)
        { return node.Parent==null?node.Name:Key(node.Parent)+"/"+node.Name; }
        internal static string Details(TreeNode node)
        {
            var item=(XeNpSdAttribute)node.Tag;
            var text=new StringBuilder();
            if(item.Value.Length!=0)text.AppendLine(item.Value).AppendLine();
            if(item.Description.Length!=0)text.AppendLine(item.Description).AppendLine();
            foreach(TreeNode child in node.Nodes) {
                var property=(XeNpSdAttribute)child.Tag;
                text.AppendLine(property.Name+(property.Value.Length==0?"":" : "+property.Value));
            }
            return text.ToString().TrimEnd();
        }
    }

    internal sealed class XeNpSdInfoForm : Form
    {
        private readonly string path;
        private readonly TreeView attributes=new TreeView { Dock=DockStyle.Fill,HideSelection=false,
            ShowNodeToolTips=true,ShowLines=true,ShowPlusMinus=true,AccessibleName="SD card attributes" };
        private readonly TextBox details=new TextBox { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,
            WordWrap=true,Dock=DockStyle.Fill,BackColor=SystemColors.Window,AccessibleName="Selected attribute value and description" };
        private readonly Label detailTitle=new Label { Dock=DockStyle.Top,Height=38,Padding=new Padding(7),Text="Attribute details" };
        private readonly ToolStripButton refresh=new ToolStripButton("Read SD Info");
        private readonly ToolStripButton save=new ToolStripButton("Save report...") { Enabled=false };
        private readonly ToolStripButton copy=new ToolStripButton("Copy selected") { Enabled=false };
        private readonly ToolStripButton expand=new ToolStripButton("Expand all") { Enabled=false };
        private readonly ToolStripButton collapse=new ToolStripButton("Collapse all") { Enabled=false };
        private readonly ToolStripStatusLabel status=new ToolStripStatusLabel("No snapshot loaded") { Spring=true,TextAlign=ContentAlignment.MiddleLeft };
        private readonly ImageList icons=new ImageList { ImageSize=new Size(16,16),ColorDepth=ColorDepth.Depth32Bit };
        private readonly ContextMenuStrip nodeMenu=new ContextMenuStrip();
        private string reportText;
        private bool busy;
        internal XeNpSdInfoForm(string path,bool autoRead=true)
        {
            this.path=path;Text="WILDX SD Card Properties";Size=new Size(1080,760);
            Font=SystemFonts.MessageBoxFont;AutoScaleMode=AutoScaleMode.Dpi;
            AutoScaleDimensions=new SizeF(96,96);MinimumSize=new Size(740,480);
            StartPosition=FormStartPosition.CenterParent;KeyPreview=true;
            icons.Images.Add(SystemIcons.Application);icons.Images.Add(SystemIcons.Information);icons.Images.Add(SystemIcons.Warning);
            attributes.ImageList=icons;
            var bar=new ToolStrip { GripStyle=ToolStripGripStyle.Hidden,Renderer=new ToolStripSystemRenderer(),Dock=DockStyle.Top };
            bar.Items.AddRange(new ToolStripItem[] {refresh,new ToolStripSeparator(),expand,collapse,new ToolStripSeparator(),copy,save});
            var caution=new Label { Dock=DockStyle.Top,Height=46,Padding=new Padding(8),
                BackColor=SystemColors.Info,ForeColor=SystemColors.InfoText,
                Text="Card-reported properties only — not measured voltage/current or a peak-power guarantee.\r\nRead-only diagnostics; not the separate SD recording application's configuration." };
            var split=new SplitContainer { Dock=DockStyle.Fill,SplitterWidth=5,Size=new Size(1060,630),
                Panel1MinSize=280,Panel2MinSize=240,SplitterDistance=530 };
            split.Panel1.Controls.Add(attributes);
            split.Panel2.Controls.Add(details);split.Panel2.Controls.Add(detailTitle);
            var footer=new StatusStrip();footer.Items.Add(status);
            Controls.Add(split);Controls.Add(caution);Controls.Add(bar);Controls.Add(footer);
            attributes.AfterSelect+=delegate { ShowSelected(); };
            expand.Click+=delegate { attributes.BeginUpdate();try { attributes.ExpandAll(); } finally { attributes.EndUpdate(); } };
            collapse.Click+=delegate { attributes.CollapseAll();if(attributes.Nodes.Count>0) {attributes.Nodes[0].Expand();attributes.SelectedNode=attributes.Nodes[0];} };
            copy.Click+=delegate { CopySelected(); };
            KeyDown+=delegate(object sender,KeyEventArgs e) {
                if(e.Control && e.KeyCode==Keys.C && attributes.ContainsFocus) {CopySelected();e.Handled=true;e.SuppressKeyPress=true;}
            };
            nodeMenu.Items.Add("Copy selected",null,delegate { CopySelected(); });
            nodeMenu.Items.Add("Expand",null,delegate { if(attributes.SelectedNode!=null)attributes.SelectedNode.ExpandAll(); });
            nodeMenu.Items.Add("Collapse",null,delegate { if(attributes.SelectedNode!=null)attributes.SelectedNode.Collapse(); });
            attributes.ContextMenuStrip=nodeMenu;
            attributes.NodeMouseClick+=delegate(object sender,TreeNodeMouseClickEventArgs e) { if(e.Button==MouseButtons.Right)attributes.SelectedNode=e.Node; };
            refresh.Click+=async delegate { await ReadInfo(); };
            if(autoRead)Shown+=async delegate { await ReadInfo(); };
            FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(busy)e.Cancel=true; };
            save.Click+=delegate {
                using(var dialog=new SaveFileDialog { Filter="Text report|*.txt",FileName="WILDX_SD_Info_"+DateTime.Now.ToString("yyyyMMdd_HHmmss")+".txt" })
                    if(dialog.ShowDialog(this)==DialogResult.OK) {
                        try { File.WriteAllText(dialog.FileName,reportText,Encoding.UTF8); }
                        catch(Exception ex) { MessageBox.Show(this,ex.Message,"Cannot save SD Info"); }
                    }
            };
            ShowMessage("No snapshot loaded","Use Read SD Info to identify the card while recording is stopped.");
            WildXTheme.Apply(this);
        }
        private void ShowSelected()
        {
            TreeNode node=attributes.SelectedNode;
            copy.Enabled=node!=null && node.Tag is XeNpSdAttribute;
            if(!copy.Enabled)return;
            detailTitle.Text=((XeNpSdAttribute)node.Tag).Name;
            details.Text=XeNpSdInfoTree.Details(node);details.SelectionStart=0;details.SelectionLength=0;
        }
        private void CopySelected()
        {
            if(attributes.SelectedNode==null || !(attributes.SelectedNode.Tag is XeNpSdAttribute))return;
            try { Clipboard.SetText(((XeNpSdAttribute)attributes.SelectedNode.Tag).Name+Environment.NewLine+XeNpSdInfoTree.Details(attributes.SelectedNode)); }
            catch(Exception ex) { MessageBox.Show(this,ex.Message,"Cannot copy attribute"); }
        }
        private void ShowMessage(string title,string message)
        {
            attributes.Nodes.Clear();attributes.Nodes.Add(new TreeNode(title,2,2));
            detailTitle.Text=title;details.Text=message;status.Text=title;
            save.Enabled=copy.Enabled=expand.Enabled=collapse.Enabled=false;reportText=null;
        }
        private static void Visit(TreeNodeCollection nodes,Action<TreeNode> action)
        { foreach(TreeNode node in nodes) { action(node);Visit(node.Nodes,action); } }
        private System.Collections.Generic.HashSet<string> ExpandedKeys()
        {
            var keys=new System.Collections.Generic.HashSet<string>();
            Visit(attributes.Nodes,delegate(TreeNode n) {if(n.IsExpanded)keys.Add(XeNpSdInfoTree.Key(n));});return keys;
        }
        // Also used by render/integration tests with saved snapshots: no USB access.
        internal void ShowSnapshot(XeNpSdInfo info,DateTime readAt,
            System.Collections.Generic.HashSet<string> expandedKeys=null,string selectedKey=null)
        {
            if(expandedKeys==null) {
                expandedKeys=ExpandedKeys();
                if(attributes.SelectedNode!=null)selectedKey=XeNpSdInfoTree.Key(attributes.SelectedNode);
            }
            TreeNode root=XeNpSdInfoTree.Build(info,path,readAt),selection=null;
            attributes.BeginUpdate();
            try {
                attributes.Nodes.Clear();attributes.Nodes.Add(root);
                if(expandedKeys.Count==0) {root.Expand();root.Nodes["identity"].Expand();}
                else Visit(attributes.Nodes,delegate(TreeNode n) {if(expandedKeys.Contains(XeNpSdInfoTree.Key(n)))n.Expand();});
                Visit(attributes.Nodes,delegate(TreeNode n) {if(XeNpSdInfoTree.Key(n)==selectedKey)selection=n;});
                attributes.SelectedNode=selection??root.Nodes["identity"];attributes.SelectedNode.EnsureVisible();
            } finally { attributes.EndUpdate(); }
            ShowSelected();
            reportText="Read at "+readAt.ToString("O")+"\r\nDevice: "+path+"\r\n\r\n"+info.Report();
            save.Enabled=expand.Enabled=collapse.Enabled=true;
            status.Text=info.SummaryState+"  |  Snapshot "+info.Sequence+"  |  Read "+readAt.ToString("G")+"  |  Cached until refreshed";
        }
        private async Task ReadInfo()
        {
            if(busy)return;busy=true;refresh.Enabled=false;
            var expandedKeys=ExpandedKeys();
            string selectedKey=attributes.SelectedNode==null?null:XeNpSdInfoTree.Key(attributes.SelectedNode);
            ShowMessage("Reading SD card...","Identifying the card at the safe base clock.\r\nNo sector writes or high-speed tuning. Please wait.");
            try {
                XeNpSdInfo info=await Task.Run(delegate {
                    // Control-only open must not reset/flush/abort the capture pipe.
                    using(var device=XeNpWinUsbDevice.OpenForControl(path))return device.ReadSdInfo();
                });
                ShowSnapshot(info,DateTime.Now,expandedKeys,selectedKey);
            } catch(Exception ex) {
                ShowMessage("SD Info unavailable",ex.Message+"\r\n\r\nStop recording first. Older firmware needs the matching SD Info USB-service update. No recording was stopped automatically.");
            } finally { busy=false;refresh.Enabled=true; }
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing) {nodeMenu.Dispose();icons.Dispose();}
            base.Dispose(disposing);
        }
    }
}
