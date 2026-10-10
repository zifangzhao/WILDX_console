using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using WildNpConfiguration;

namespace USB512_interface
{
    // Device-produced recording receipt. The NPM1 wire image is intentionally
    // separate from the user-edited JSON: it contains the cached EEPROM identity
    // and exactly the NPB1 payload that firmware holds for the physical port.
    internal sealed class NpProbeManifest
    {
        internal const int SelectionBytes=1576;
        internal const int WireBytes=108+SelectionBytes;
        internal int Port { get; private set; }
        internal bool HasSelection { get; private set; }
        internal uint Model { get; private set; }
        internal uint IdentityReason { get; private set; }
        internal uint IdentitySource { get; private set; }
        internal uint SerialLo { get; private set; }
        internal uint SerialHi { get; private set; }
        internal byte Version { get; private set; }
        internal byte Revision { get; private set; }
        internal byte ConfigurationState { get; private set; }
        internal byte Verification { get; private set; }
        internal uint SelectionCrc { get; private set; }
        internal string Flex { get; private set; }
        internal string Part { get; private set; }
        internal byte[] Selection { get; private set; }

        static uint U32(byte[] b,int at) {return BitConverter.ToUInt32(b,at);}
        static string Text(byte[] b,int at,int count) {
            int n=0;
            while(n<count && b[at+n]!=0 && b[at+n]!=0xff)n++;
            return Encoding.ASCII.GetString(b,at,n);
        }
        static string Hex(byte[] b,int at,int count) {
            var s=new StringBuilder(count*2);
            for(int i=0;i<count;i++)s.Append(b[at+i].ToString("x2",CultureInfo.InvariantCulture));
            return s.ToString();
        }
        static string Quote(string s) {
            var q=new StringBuilder("\"");
            foreach(char c in s??String.Empty) {
                if(c=='\\' || c=='\"')q.Append('\\').Append(c);
                else if(c=='\r')q.Append("\\r");
                else if(c=='\n')q.Append("\\n");
                else if(c<' ')q.Append("\\u").Append(((int)c).ToString("x4",CultureInfo.InvariantCulture));
                else q.Append(c);
            }
            return q.Append('\"').ToString();
        }
        static string ModelName(uint value) {
            return value==1?"NRIC1384":value==2?"Neuropixels 1.0":"unknown";
        }
        static string SourceName(uint value) {
            return value==1?"flex EEPROM (two matching reads)":value==2?"explicit":value==3?"EEPROM absent, NRIC default":"unknown";
        }
        static string ReasonName(uint value) {
            switch(value) {case 0:return "ok";case 1:return "EEPROM missing";case 2:return "I2C error";case 3:return "two EEPROM reads differ";case 4:return "invalid EEPROM";case 5:return "unsupported probe part";case 6:return "explicit model conflict";default:return "unknown";}
        }
        static string StateName(byte value) {
            switch(value) {case 0:return "none/default";case 1:return "receiving";case 2:return "staged, not applied";case 3:return "applied";case 4:return "failed";default:return "unknown";}
        }

        internal static NpProbeManifest Parse(byte[] raw)
        {
            if(raw==null || raw.Length!=WireBytes || raw[0]!='N' || raw[1]!='P' || raw[2]!='M' || raw[3]!='1' || raw[4]!=1 || raw[5]>2)
                throw new IOException("NP probe-metadata receipt has an unknown identity or size.");
            var m=new NpProbeManifest {
                Port=raw[5],HasSelection=raw[6]!=0,Model=U32(raw,8),IdentityReason=U32(raw,12),IdentitySource=U32(raw,16),
                SerialLo=U32(raw,20),SerialHi=U32(raw,24),Version=raw[28],Revision=raw[29],ConfigurationState=raw[30],Verification=raw[31],
                SelectionCrc=U32(raw,32),Flex=Text(raw,48,20),Part=Text(raw,68,40)
            };
            uint bytes=U32(raw,36);
            if(bytes!=(m.HasSelection?(uint)SelectionBytes:0U))throw new IOException("NP probe-metadata selection length is invalid.");
            if(m.HasSelection) {
                m.Selection=new byte[SelectionBytes];Buffer.BlockCopy(raw,108,m.Selection,0,SelectionBytes);
                if(m.Selection[0]!='N' || m.Selection[1]!='P' || m.Selection[2]!='B' || m.Selection[3]!='1' || m.Selection[5]!=(byte)m.Port ||
                   NpConfigClient.Crc(m.Selection,m.Selection.Length)!=m.SelectionCrc)
                    throw new IOException("NP probe-metadata channel selection failed its identity or CRC check.");
            }
            return m;
        }

        internal IList<string> Save(string folder)
        {
            int physicalPort=Port+1;
            string prefix="neuropixels_probe"+physicalPort;
            string metadataFile=prefix+"_metadata.json";
            string selectionFile=prefix+"_applied-selection.npb1";
            if(HasSelection)File.WriteAllBytes(Path.Combine(folder,selectionFile),Selection);
            File.WriteAllText(Path.Combine(folder,metadataFile),BuildJson(selectionFile),new UTF8Encoding(false));
            var lines=new List<string>();
            lines.Add(prefix+".identity_model="+ModelName(Model));
            lines.Add(prefix+".identity_source="+SourceName(IdentitySource));
            lines.Add(prefix+".identity_reason="+ReasonName(IdentityReason));
            lines.Add(prefix+".identity_serial=0x"+SerialHi.ToString("X8",CultureInfo.InvariantCulture)+SerialLo.ToString("X8",CultureInfo.InvariantCulture));
            lines.Add(prefix+".identity_part="+(String.IsNullOrEmpty(Part)?"unavailable":Part));
            lines.Add(prefix+".configuration_state="+StateName(ConfigurationState));
            lines.Add(prefix+".configuration_verification="+Verification);
            lines.Add(prefix+".channel_selection="+(HasSelection?"saved CRC32=0x"+SelectionCrc.ToString("X8",CultureInfo.InvariantCulture):"not staged by firmware"));
            lines.Add(prefix+".metadata_file="+metadataFile);
            if(HasSelection)lines.Add(prefix+".selection_file="+selectionFile);
            return lines;
        }

        internal string BuildJson(string selectionFile)
        {
            var s=new StringBuilder(42000);
            s.Append("{\n  \"format\": \"WILDX NP applied probe receipt\",\n  \"format_version\": 1,\n  \"physical_port\": ").Append(Port+1);
            s.Append(",\n  \"identity\": {\n    \"model\": ").Append(Quote(ModelName(Model)));
            s.Append(",\n    \"model_code\": ").Append(Model).Append(",\n    \"source\": ").Append(Quote(SourceName(IdentitySource)));
            s.Append(",\n    \"source_code\": ").Append(IdentitySource).Append(",\n    \"reason\": ").Append(Quote(ReasonName(IdentityReason)));
            s.Append(",\n    \"reason_code\": ").Append(IdentityReason).Append(",\n    \"serial_hex\": ").Append(Quote("0x"+SerialHi.ToString("X8",CultureInfo.InvariantCulture)+SerialLo.ToString("X8",CultureInfo.InvariantCulture)));
            s.Append(",\n    \"version\": ").Append(Version).Append(",\n    \"revision\": ").Append(Revision);
            s.Append(",\n    \"flex\": ").Append(Quote(Flex)).Append(",\n    \"part\": ").Append(Quote(Part)).Append("\n  },");
            s.Append("\n  \"configuration\": {\n    \"state\": ").Append(Quote(StateName(ConfigurationState)));
            s.Append(",\n    \"state_code\": ").Append(ConfigurationState).Append(",\n    \"verification_level\": ").Append(Verification);
            s.Append(",\n    \"selection_present\": ").Append(HasSelection?"true":"false");
            s.Append(",\n    \"selection_crc32\": ").Append(Quote("0x"+SelectionCrc.ToString("X8",CultureInfo.InvariantCulture)));
            if(HasSelection) {
                s.Append(",\n    \"selection_file\": ").Append(Quote(selectionFile));
                s.Append(",\n    \"configuration_id_sha256\": ").Append(Quote(Hex(Selection,8,32)));
                s.Append(",\n    \"channels\": [");
                for(int channel=0;channel<384;channel++) {
                    int at=40+channel*4,site=Selection[at]|(Selection[at+1]<<8),flags=Selection[at+2],reference=Selection[at+3];
                    s.Append(channel==0?"\n      ":",\n      ");
                    s.Append("{\"output_channel\":").Append(channel).Append(",\"site\":");
                    if(site==65535)s.Append("null");else s.Append(site);
                    s.Append(",\"connected\":").Append(site==65535?"false":"true");
                    s.Append(",\"ap_gain_index\":").Append(flags&7).Append(",\"lfp_gain_index\":").Append((flags>>3)&7);
                    s.Append(",\"high_pass\":").Append((flags&64)!=0?"true":"false");
                    s.Append(",\"standby\":").Append((flags&128)!=0?"true":"false");
                    s.Append(",\"reference\":").Append(reference).Append('}');
                }
                s.Append("\n    ]");
            }
            s.Append("\n  },\n  \"provenance\": \"Cached flex EEPROM identity and firmware-held selection were retrieved after STOP; no I2C was issued during capture.\"\n}\n");
            return s.ToString();
        }
    }
}
