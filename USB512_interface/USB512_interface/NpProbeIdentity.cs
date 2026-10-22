using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace USB512_interface
{
    // Identity evidence belongs to the acquisition, never to whichever probe
    // happens to be connected when an old recording is downloaded.
    internal static class NpProbeIdentity
    {
        internal static Dictionary<string,object> Unknown(string reason)
        {
            return NpRecordingSettings.Fields("available",false,"serial",null,"serial_hex",null,"model",null,
                "part",null,"source","unavailable","reason",reason);
        }
        internal static Dictionary<string,object> Eeprom(byte[] bytes,int offset,string model,string evidence,bool generated=false)
        {
            if(bytes==null || offset<0 || offset>bytes.Length-8)throw new InvalidDataException("Probe serial bytes are truncated.");
            ulong serial=BitConverter.ToUInt64(bytes,offset);
            if(generated || serial==0 || serial==UInt64.MaxValue)return Unknown(generated?"Generated source; no physical probe identity.":"EEPROM serial is absent or erased.");
            var result=NpRecordingSettings.Fields("available",true,"serial",serial.ToString(CultureInfo.InvariantCulture),
                "serial_hex","0x"+serial.ToString("X16",CultureInfo.InvariantCulture),"serial_bytes_le",BitConverter.ToString(bytes,offset,8).Replace("-",""),
                "model",String.IsNullOrEmpty(model)?null:model,"part",String.IsNullOrEmpty(model)?null:model,
                "source","probe EEPROM","evidence",evidence,"reason",null);
            return result;
        }
        internal static string Model(byte[] bytes,int offset)
        {
            int count=0;while(count<8 && bytes[offset+count]!=0 && bytes[offset+count]!=255)count++;
            return Encoding.ASCII.GetString(bytes,offset,count);
        }
        // Call only with a header accepted by the native verifier. Old V1/V2
        // stream formats have one P2 identity, not the three V3 identity arrays.
        internal static void V2Header(NpRecordingSettings settings,byte[] header,string evidence)
        {
            if(header==null)return;
            if(header.Length!=512)throw new InvalidDataException("A full verified NP2 header is required.");
            string magic=Encoding.ASCII.GetString(header,0,8);
            if(magic=="NP2USB03") {
                uint mask=BitConverter.ToUInt32(header,76),live=BitConverter.ToUInt32(header,80),flags=BitConverter.ToUInt32(header,52);
                for(int p=0;p<3;p++)if((mask&(1U<<p))!=0)
                    settings.Identity(p+1,(live&(1U<<p))==0?Unknown("This stored lane was not a live physical probe."):
                        Eeprom(header,96+p*8,Model(header,120+p*8),evidence,(flags&4)!=0));
            } else if(magic=="NP2USB01" || magic=="NP2USB02")
                settings.Identity(2,Eeprom(header,80,Model(header,88),evidence+"; legacy single P2 identity"));
            else throw new InvalidDataException("Unsupported NP2 identity header.");
        }
        internal static void V1Receipt(NpRecordingSettings settings,NpProbeManifest receipt)
        {
            if(receipt.IdentitySource!=1 || receipt.IdentityReason!=0) {
                settings.Identity(receipt.Port+1,Unknown("Device EEPROM identity was not confirmed (source "+receipt.IdentitySource+", reason "+receipt.IdentityReason+")."));return;
            }
            var raw=new byte[8];Buffer.BlockCopy(BitConverter.GetBytes(receipt.SerialLo),0,raw,0,4);Buffer.BlockCopy(BitConverter.GetBytes(receipt.SerialHi),0,raw,4,4);
            var identity=Eeprom(raw,0,receipt.Part,"NPM1 cached EEPROM receipt after confirmed STOP");
            identity["model"]=receipt.Model==2?"Neuropixels 1.0":receipt.Model==1?"NRIC1384":null;
            identity["version"]=receipt.Version;identity["revision"]=receipt.Revision;identity["flex"]=receipt.Flex;
            settings.Identity(receipt.Port+1,identity);
        }
    }
}
