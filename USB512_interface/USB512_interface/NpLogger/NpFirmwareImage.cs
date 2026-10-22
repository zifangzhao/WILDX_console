using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CE32_console;

namespace USB512_interface
{
    // Matched V1 ABI257/258 contract. Neither names nor USB indices identify images.
    internal sealed class NpFirmwareImage
    {
        internal const uint FlashBase=0x08000000, ConfigBase=0x0801c000, AppManifest=0x0801e000,
            AppBase=0x08020000, UsbManifest=0x0817e000, UsbBase=0x08180000, FlashLimit=0x08200000;
        internal const int FlashBytes=0x200000, PageBytes=8192;
        // V1 recovery has no independent signed board manifest. Admit the qualified
        // ABI258 recovery anchor; new loader builds need an explicit reviewed anchor.
        internal const string RecoverySha256="03cbca29117ea3c54f797f5cd9698e786bc5d0ffa73853febad3431b425e3a7f";
        readonly byte[] flash=new byte[FlashBytes];
        readonly BitArray present=new BitArray(FlashBytes);
        internal bool Full;
        uint family=1;
        internal byte[] AppPackage;
        internal string SourceSha256;
        int count;
        internal static string Sha(byte[] data,int at=0,int length=-1)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data,at,length<0?data.Length-at:length)).Replace("-","").ToLowerInvariant();}
        internal static uint U(byte[] b,int at){return BitConverter.ToUInt32(b,at);}
        internal static uint Crc(byte[] b,int at,int length){return NpDownloadStream.Crc(b,at,length);}
        internal static NpFirmwareImage Load(string path)
        {
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                if(file.Length<11 || file.Length>8*1024*1024)throw new InvalidDataException("Select an ordinary V1 HEX image of at most 8 MiB.");
                var raw=new byte[(int)file.Length];int at=0,n;
                while(at<raw.Length && (n=file.Read(raw,at,raw.Length-at))>0)at+=n;
                if(at!=raw.Length)throw new IOException("Incomplete firmware file read.");
                return Parse(raw);
            }
        }
        internal static NpFirmwareImage Parse(byte[] raw)
        {return ParseCore(raw,1);}
        internal static NpFirmwareImage LoadBundledV2(string path,string expectedSha256)
        {
            var info=new FileInfo(path);if(!info.Exists||info.Length<11||info.Length>8*1024*1024)throw new InvalidDataException("Invalid bundled V2 HEX size.");
            byte[] raw=File.ReadAllBytes(path);
            if(!System.Text.RegularExpressions.Regex.IsMatch(expectedSha256??"",@"\A[0-9a-fA-F]{64}\z")||!Sha(raw).Equals(expectedSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Bundled V2 firmware SHA-256 mismatch.");
            var image=ParseCore(raw,2);if(!image.Full)throw new InvalidDataException("Bundled full V2 firmware must include recovery, APP and USB service.");return image;
        }
        static NpFirmwareImage ParseCore(byte[] raw,uint family)
        {
            if(raw==null || raw.Length<11 || raw.Length>8*1024*1024)throw new InvalidDataException("Invalid HEX length.");
            var image=new NpFirmwareImage{SourceSha256=Sha(raw),family=family};
            uint upper=0;uint? start=null;bool eof=false;
            using(var reader=new StringReader(Encoding.ASCII.GetString(raw))) {
                string line;
                while((line=reader.ReadLine())!=null){
                    line=line.Trim();if(line.Length==0)continue;
                    if(eof || line.Length<11 || line[0]!=':')throw new InvalidDataException("Malformed HEX or data after EOF.");
                    int length=HexByte(line,1);
                    if(line.Length!=1+2*(length+5))throw new InvalidDataException("HEX record length mismatch.");
                    var record=new byte[length+5];int checksum=0;
                    for(int i=0;i<record.Length;i++){record[i]=(byte)HexByte(line,1+2*i);checksum+=record[i];}
                    if((checksum&255)!=0)throw new InvalidDataException("HEX checksum mismatch.");
                    uint offset=(uint)(record[1]*256+record[2]);int kind=record[3];
                    if(kind==0){
                        if(length==0 || offset+length>65536)throw new InvalidDataException("Empty or crossing HEX data record.");
                        for(int i=0;i<length;i++){
                            ulong address=(ulong)upper+offset+(uint)i;
                            if(address<FlashBase || address>=FlashLimit || (address>=ConfigBase && address<AppManifest))
                                throw new InvalidDataException("Image escapes V1 firmware flash or writes the protected configuration page.");
                            int index=(int)(address-FlashBase);
                            if(image.present[index])throw new InvalidDataException("Overlapping HEX address.");
                            image.present[index]=true;image.flash[index]=record[4+i];image.count++;
                        }
                    }else if(kind==1){
                        if(length!=0 || offset!=0)throw new InvalidDataException("Invalid HEX EOF.");eof=true;
                    }else if(kind==2 || kind==4){
                        if(length!=2 || offset!=0)throw new InvalidDataException("Invalid HEX address record.");
                        upper=(uint)(record[4]*256+record[5])<<(kind==2?4:16);
                    }else if(kind==5 || kind==3){
                        if(length!=4 || offset!=0 || start.HasValue)throw new InvalidDataException("Invalid HEX start record.");
                        start=kind==5?((uint)record[4]<<24)|((uint)record[5]<<16)|((uint)record[6]<<8)|record[7]:
                            ((uint)(record[4]*256+record[5])<<4)+(uint)(record[6]*256+record[7]);
                    }else throw new InvalidDataException("Unsupported HEX record type.");
                }
            }
            if(!eof)throw new InvalidDataException("HEX EOF is missing.");
            byte[] manifest=image.Read(AppManifest,128);uint size=U(manifest,28);
            if(size<8 || size>NpV1Management.Capacity)throw new InvalidDataException("Invalid APP extent.");
            byte[] app=image.Read(AppBase,(int)size);
            image.AppPackage=new byte[128+app.Length];Buffer.BlockCopy(manifest,0,image.AppPackage,0,128);Buffer.BlockCopy(app,0,image.AppPackage,128,app.Length);
            if(family==1)NpV1Management.ValidateAppBytes(image.AppPackage);else image.ValidateManifest(AppManifest,0x41505031,AppBase,UsbManifest);
            if(image.count==image.AppPackage.Length){
                image.Full=false;
                if(start.HasValue && (start.Value&~1U)!=(U(app,4)&~1U))throw new InvalidDataException("APP HEX entry does not match its reset vector.");
                return image;
            }
            if(image.count!=FlashBytes-PageBytes)throw new InvalidDataException("Select complete fused V1 firmware or pure APP-only HEX. Partial APP+USB and service-only images are ambiguous.");
            image.Read(FlashBase,(int)(ConfigBase-FlashBase));image.Read(AppManifest,(int)(FlashLimit-AppManifest));
            if(family==1)ValidateRecovery(image.flash);
            ValidateVector(image.flash,0,FlashBase,(int)(ConfigBase-FlashBase));
            image.ValidateManifest(UsbManifest,0x55534231,UsbBase,FlashLimit);
            int dl=(int)(0x081be000-FlashBase);
            // Unified USB can occupy the old download-manifest address. Only
            // interpret a separate legacy slot when it is outside that payload.
            uint usbSize=U(image.flash,(int)(UsbManifest-FlashBase)+28);
            if(UsbBase+usbSize<=0x081be000 && U(image.flash,dl)!=UInt32.MaxValue)
                image.ValidateManifest(0x081be000,0x444c4431,0x081c0000,FlashLimit);
            if(start.HasValue && (start.Value&~1U)!=(U(image.flash,4)&~1U))throw new InvalidDataException("Fused HEX entry does not match its recovery reset vector.");
            image.Full=true;return image;
        }
        byte[] Read(uint address,int size)
        {
            int at=(int)(address-FlashBase);var result=new byte[size];
            for(int i=0;i<size;i++){if(!present[at+i])throw new InvalidDataException("Firmware image is incomplete at 0x"+(address+(uint)i).ToString("X8")+".");result[i]=flash[at+i];}
            return result;
        }
        void ValidateManifest(uint address,uint id,uint imageBase,uint limit)
        {
            var m=Read(address,128);uint size=U(m,28);
            if(BitConverter.ToUInt64(m,0)!=(family==1?0x31464e4d31504e58UL:0x31464e4d504e4558UL) || U(m,8)!=1 || U(m,12)!=0x56414c44 || U(m,16)!=id || U(m,24)!=imageBase ||
                size<8 || size>limit-imageBase || (family==1?U(m,80)!=257&&U(m,80)!=258:U(m,80)!=4) || U(m,124)!=Crc(m,0,124))
                throw new InvalidDataException("Invalid firmware partition manifest.");
            var bytes=Read(imageBase,(int)size);ValidateVector(bytes,0,imageBase,(int)size);
            if(U(m,32)!=Crc(bytes,0,bytes.Length) || U(m,36)!=U(bytes,0) || U(m,40)!=U(bytes,4))throw new InvalidDataException("V1 service CRC/vector mismatch.");
        }
        static void ValidateVector(byte[] bytes,int at,uint imageBase,int size)
        {
            uint sp=U(bytes,at),pc=U(bytes,at+4);
            if(!(sp>0x20000000 && sp<=0x20270000) || (sp&7)!=0 || (pc&1)==0 || (pc&~1U)<imageBase || (pc&~1U)>=(ulong)imageBase+(uint)size)
                throw new InvalidDataException("Invalid V1 firmware vectors.");
        }
        internal static void ValidateRecovery(byte[] bytes)
        {
            if(bytes==null || bytes.Length<(int)(ConfigBase-FlashBase) || Sha(bytes,0,(int)(ConfigBase-FlashBase))!=RecoverySha256)
                throw new InvalidDataException("Recovery loader is not a recognized V1 ABI258 release. Its board identity cannot be established; no write is allowed.");
        }
        internal byte[] ExpectedFlash(byte[] before)
        {
            if(before==null || (before.Length!=FlashBytes && before.Length!=2*FlashBytes))throw new InvalidDataException("Exact reported 2 or 4 MiB physical flash backup required.");
            var expected=(byte[])before.Clone();
            for(int i=0;i<FlashBytes;i++)if(present[i])expected[i]=flash[i];
            if(!Full){
                ValidateRecovery(before); // Bounded DFU fallback requires a known compatible resident loader.
                int at=(int)(AppManifest-FlashBase);
                uint previous=U(before,at+20);
                bool valid=BitConverter.ToUInt64(before,at)==0x31464e4d31504e58UL && U(before,at+124)==Crc(before,at,124);
                Put(expected,at+20,valid?unchecked(previous+1):0);Put(expected,at+80,258);Put(expected,at+124,Crc(expected,at,124));
            }
            return expected;
        }
        internal FileStream WritePageHex(string path,byte[] before,byte[] expected)
        {
            if(before==null || expected==null || (before.Length!=FlashBytes && before.Length!=2*FlashBytes) || expected.Length!=before.Length)
                throw new InvalidDataException("Matching complete physical flash backup and expected bytes required.");
            var pages=new List<int>();
            for(int page=0;page<FlashBytes;page+=PageBytes){
                bool touched=false,changed=false;
                for(int i=page;i<page+PageBytes;i++){
                    touched|=present[i];changed|=expected[i]!=before[i];
                    if(touched && changed)break;
                }
                if(!touched || !changed)continue;
                if(page==(int)(ConfigBase-FlashBase))throw new InvalidDataException("Protected erase page.");
                pages.Add(page);
            }
            if(pages.Count==0)return null; // No operation file or empty-HEX programming for identical firmware.
            var file=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
            try{
                using(var writer=new StreamWriter(file,Encoding.ASCII,4096,true)){
                    uint upper=UInt32.MaxValue;
                    foreach(int page in pages){
                        for(int i=page;i<page+PageBytes;i+=32){
                            uint address=FlashBase+(uint)i;
                            if((address>>16)!=upper){upper=address>>16;writer.WriteLine(Record(0,4,new[]{(byte)(upper>>8),(byte)upper}));}
                            var bytes=new byte[32];Buffer.BlockCopy(expected,i,bytes,0,32);writer.WriteLine(Record((ushort)address,0,bytes));
                        }
                    }
                    writer.WriteLine(Record(0,1,new byte[0]));writer.Flush();
                }
                file.Flush();file.Position=0;byte[] writtenHash;
                using(var sha=SHA256.Create())writtenHash=sha.ComputeHash(file);
                file.Dispose();file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                byte[] lockedHash;using(var sha=SHA256.Create())lockedHash=sha.ComputeHash(file);
                for(int i=0;i<writtenHash.Length;i++)if(writtenHash[i]!=lockedHash[i])throw new IOException("Validated HEX changed before its read lock was acquired.");
                return file; // Read lock permits normal CLI readers, and denies mutation/deletion through verify.
            }catch{file.Dispose();throw;}
        }
        static string Record(ushort offset,byte type,byte[] payload)
        {
            var text=new StringBuilder(":"+payload.Length.ToString("X2")+offset.ToString("X4")+type.ToString("X2"));
            int sum=payload.Length+(offset>>8)+(offset&255)+type;
            foreach(byte b in payload){sum+=b;text.Append(b.ToString("X2"));}return text.Append(((byte)-sum).ToString("X2")).ToString();
        }
        static void Put(byte[] b,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,at,4);}
        static int HexByte(string line,int at)
        {
            int value=0;
            for(int i=0;i<2;i++){char c=line[at+i];int d=c>='0'&&c<='9'?c-'0':c>='a'&&c<='f'?c-'a'+10:c>='A'&&c<='F'?c-'A'+10:-1;if(d<0)throw new InvalidDataException("Non-hexadecimal HEX record.");value=value*16+d;}
            return value;
        }
    }
}
