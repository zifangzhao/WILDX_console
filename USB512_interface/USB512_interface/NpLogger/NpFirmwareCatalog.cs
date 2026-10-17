using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Diagnostics;

namespace USB512_interface
{
    internal sealed class NpFirmwareIdentity
    {
        internal const byte UsbRequest=0x6a,BleCommand=0xbc;
        internal const uint UsbCapability=0x80,BleCapability=0x02000000;
        internal const int WireBytes=192;
        internal uint Family, Board;
        internal string Uid, AppCrc32, UsbCrc32, RecoveryCrc32=null;
        internal NpFirmwareBuild App, Usb;
        internal uint Flags, LoaderAbi, RecoveryProtocol, Role;
        internal string LoaderBuildTag;
        static uint U(byte[] body,int offset){return BitConverter.ToUInt32(body,offset);}
        static string Hex(byte[] body,int offset,int bytes){return BitConverter.ToString(body,offset,bytes).Replace("-","");}
        static bool Zero(byte[] body,int offset,int bytes){for(int i=offset;i<offset+bytes;i++)if(body[i]!=0)return false;return true;}
        internal static NpFirmwareIdentity Parse(byte[] body,uint family,string expectedUid=null)
        {
            if(body==null || body.Length!=WireBytes || U(body,0)!=0x3146504e || BitConverter.ToUInt16(body,4)!=1 ||
                BitConverter.ToUInt16(body,6)!=WireBytes || U(body,8)!=family || (family!=1 && family!=2) ||
                U(body,188)!=CE32_console.NpSdControl.Crc(body,188))throw new InvalidDataException("Invalid firmware identity record.");
            uint flags=U(body,28);
            string uid=U(body,16).ToString("X8")+U(body,20).ToString("X8")+U(body,24).ToString("X8");
            if((flags&~511U)!=0 || (flags&256)==0 || ((flags&4)!=0 && (flags&1)==0) || ((flags&8)!=0 && (flags&2)==0) ||
                uid==new string('0',24) || uid==new string('F',24) || (expectedUid!=null && !uid.Equals(expectedUid,StringComparison.OrdinalIgnoreCase)) ||
                ((flags&16)==0?U(body,12)!=0:U(body,12)==0) || ((flags&32)==0 && U(body,160)!=0) ||
                ((flags&64)==0 && U(body,164)!=0) || ((flags&128)==0 && !Zero(body,172,16)) || U(body,168)>5)
                throw new InvalidDataException("Firmware identity UID, validity or source mismatch.");
            var value=new NpFirmwareIdentity {Family=family,Board=U(body,12),Uid=uid,Flags=flags,LoaderAbi=U(body,160),RecoveryProtocol=U(body,164),Role=U(body,168),
                LoaderBuildTag=(flags&128)!=0?Hex(body,172,16):null};
            value.App=Descriptor(body,32,(flags&1)!=0,0x41505031,0x08020000,0x15e000);
            value.Usb=Descriptor(body,96,(flags&2)!=0,0x55534231,0x08180000,0x80000);
            value.AppCrc32=value.App?.codeCrc32;value.UsbCrc32=value.Usb?.codeCrc32;
            return value;
        }
        // HEADER_VALID is a firmware statement about its cached manifest check;
        // this record contains descriptor fields, not the original header CRC.
        static NpFirmwareBuild Descriptor(byte[] body,int offset,bool valid,uint image,uint address,uint capacity)
        {
            if(!valid){if(!Zero(body,offset,64))throw new InvalidDataException("Unknown firmware descriptor must be zero.");return null;}
            if(U(body,offset)!=image || U(body,offset+4)!=address || U(body,offset+8)<8 || U(body,offset+8)>capacity || U(body,offset+60)!=0)
                throw new InvalidDataException("Firmware descriptor escapes its partition.");
            return new NpFirmwareBuild {imageId=image,baseAddress=address,bytes=U(body,offset+8),codeCrc32=U(body,offset+12).ToString("X8"),
                buildIdHex=Hex(body,offset+16,32),manifestFlags=U(body,offset+48),minimumLoaderAbi=U(body,offset+52),generation=U(body,offset+56)};
        }
        internal static NpFirmwareIdentity ReadUsb(string path,uint family)
        {
            using(var control=new NpSystemInfoControl(path)){
                byte[] service=control.Read(0xc0,0x6e,0,32);
                string uid=family==1?NpV1Management.UidFromPath(path):NpV2FirmwareForm.Uid(path);
                if(uid==null)throw new IOException("Exact firmware MCU UID is required.");
                if(family==1)NpV1Management.ParseSplitStatus(service,uid);
                else {
                    var state=NpV2ServiceStatus.Parse(service);
                    if(!state.Valid || state.Uid0!=Convert.ToUInt32(uid.Substring(0,8),16) || state.UidXor!=(Convert.ToUInt32(uid.Substring(8,8),16)^Convert.ToUInt32(uid.Substring(16,8),16)))
                        throw new IOException("V2 firmware service identity changed.");
                }
                if((U(service,12)&UsbCapability)==0 || U(service,16)!=0 || U(service,20)!=0)return null;
                return Parse(control.Read(0xc0,UsbRequest,0,WireBytes),family,uid);
            }
        }
    }

    internal sealed class NpFirmwareBuild
    {
        public uint imageId { get; set; }
        public uint baseAddress { get; set; }
        public uint bytes { get; set; }
        public string codeCrc32 { get; set; }
        public string buildIdHex { get; set; }
        public uint manifestFlags { get; set; }
        public uint minimumLoaderAbi { get; set; }
        public uint generation { get; set; }
        internal bool Same(NpFirmwareBuild value)
        {
            return value != null && imageId == value.imageId && baseAddress == value.baseAddress && bytes == value.bytes &&
                String.Equals(codeCrc32,value.codeCrc32,StringComparison.OrdinalIgnoreCase) &&
                String.Equals(buildIdHex,value.buildIdHex,StringComparison.OrdinalIgnoreCase) &&
                manifestFlags == value.manifestFlags && minimumLoaderAbi == value.minimumLoaderAbi;
        }
    }

    internal sealed class NpFirmwareAsset
    {
        public string path { get; set; }
        public string sha256 { get; set; }
    }

    internal sealed class NpFirmwareRelease
    {
        public uint family { get; set; }
        public uint[] boardIds { get; set; }
        public string board { get; set; }
        public string release { get; set; }
        public string sourceCommit { get; set; }
        public string stage { get; set; }
        public string appVersion { get; set; }
        public string usbVersion { get; set; }
        public string recoveryVersion { get; set; }
        public string appCrc32 { get; set; }
        public string usbCrc32 { get; set; }
        public string recoveryCrc32 { get; set; }
        public NpFirmwareBuild appIdentity { get; set; }
        public NpFirmwareBuild usbIdentity { get; set; }
        public NpFirmwareBuild[] previousApps { get; set; }
        public NpFirmwareBuild[] previousUsb { get; set; }
        public NpFirmwareAsset app { get; set; }
        public NpFirmwareAsset[] files { get; set; }
        public string limits { get; set; }
        internal string Versions { get { return "APP " + appVersion + "  |  USB " + usbVersion + "  |  Boot " + recoveryVersion; } }
        internal bool Matches(NpFirmwareIdentity value)
        { return value != null && value.Family == family && boardIds.Contains(value.Board); }
        internal bool HasUpdate(NpFirmwareIdentity value)
        {
            return Matches(value) && (((value.Flags&4)!=0 && previousApps.Any(x=>x.Same(value.App))) ||
                ((value.Flags&8)!=0 && previousUsb.Any(x=>x.Same(value.Usb))));
        }
        internal string Installed(NpFirmwareIdentity value)
        {
            return "APP " + Component(value == null ? null : value.AppCrc32, value != null && value.Family == family && appIdentity.Same(value.App), appVersion) +
                "  |  USB " + Component(value == null ? null : value.UsbCrc32, value != null && value.Family == family && usbIdentity.Same(value.Usb), usbVersion) +
                "  |  Boot " + Component(value == null ? null : value.RecoveryCrc32, value != null && value.RecoveryCrc32 != null && value.RecoveryCrc32 == recoveryCrc32, recoveryVersion);
        }
        static string Component(string actual, bool matches, string version)
        {
            if (String.IsNullOrEmpty(actual)) return "Not reported";
            return matches ? version + " (" + actual + ")" : "CRC " + actual;
        }
    }

    internal sealed class NpFirmwareCatalog
    {
        sealed class Document { public int schemaVersion { get; set; } public NpFirmwareRelease[] releases { get; set; } }
        internal readonly string Root;
        internal readonly NpFirmwareRelease[] Releases;
        internal readonly string Error;
        static readonly Lazy<NpFirmwareCatalog> bundled = new Lazy<NpFirmwareCatalog>(() => {
            string root = Path.Combine(Path.GetDirectoryName(typeof(NpFirmwareCatalog).Assembly.Location), "Firmware");
            try { return Load(root); } catch (Exception ex) { return new NpFirmwareCatalog(root, new NpFirmwareRelease[0], ex.Message); }
        });
        internal static NpFirmwareCatalog Bundled { get { return bundled.Value; } }
        NpFirmwareCatalog(string root, NpFirmwareRelease[] releases, string error = null) { Root = root; Releases = releases; Error = error; }
        internal NpFirmwareRelease For(uint family) { return Releases.SingleOrDefault(x => x.family == family); }
        internal static NpFirmwareCatalog Load(string root)
        {
            root = Path.GetFullPath(root);
            var manifest = new FileInfo(Path.Combine(root, "releases.json"));
            if (!manifest.Exists || manifest.Length > 128 * 1024) throw new InvalidDataException("Bundled firmware manifest missing or too large.");
            var document = new JavaScriptSerializer { MaxJsonLength = 128 * 1024 }.Deserialize<Document>(File.ReadAllText(manifest.FullName));
            if (document == null || document.schemaVersion != 1 || document.releases == null || document.releases.Length != 2 ||
                document.releases.Select(x => x.family).Distinct().Count() != 2) throw new InvalidDataException("Unsupported firmware manifest.");
            var result = new NpFirmwareCatalog(root, document.releases);
            foreach (var release in result.Releases) {
                if ((release.family != 1 && release.family != 2) || release.boardIds == null ||
                    release.boardIds.Any(x => x == 0) || String.IsNullOrWhiteSpace(release.release) ||
                    !Regex.IsMatch(release.sourceCommit ?? "", "\\A[0-9a-fA-F]{40}\\z") ||
                    String.IsNullOrWhiteSpace(release.stage) || String.IsNullOrWhiteSpace(release.limits) ||
                    String.IsNullOrWhiteSpace(release.appVersion) || String.IsNullOrWhiteSpace(release.usbVersion) ||
                    String.IsNullOrWhiteSpace(release.recoveryVersion) || !Crc(release.appCrc32) || !Crc(release.usbCrc32) ||
                    (release.recoveryCrc32 != null && !Crc(release.recoveryCrc32)) || release.app == null || release.files == null || release.files.Length == 0)
                    throw new InvalidDataException("Incomplete V" + release.family + " firmware metadata.");
                foreach(var build in new[]{release.appIdentity,release.usbIdentity}.Concat(release.previousApps??new NpFirmwareBuild[0]).Concat(release.previousUsb??new NpFirmwareBuild[0]))
                    if(build==null || !Crc(build.codeCrc32) || !Regex.IsMatch(build.buildIdHex??"","\\A[0-9a-fA-F]{64}\\z") || build.bytes<8)
                        throw new InvalidDataException("Invalid firmware build identity.");
                if(release.previousApps==null || release.previousUsb==null || release.appIdentity.codeCrc32!=release.appCrc32 || release.usbIdentity.codeCrc32!=release.usbCrc32)
                    throw new InvalidDataException("Firmware identity/history mismatch.");
                if (!release.files.Any(x => x.path == release.app.path && x.sha256 == release.app.sha256)) throw new InvalidDataException("APP is absent from the checked firmware files.");
                foreach (var file in release.files) result.CheckedPath(file);
            }
            return result;
        }
        static bool Crc(string value) { return Regex.IsMatch(value ?? "", "\\A[0-9a-fA-F]{8}\\z"); }
        internal string CheckedPath(NpFirmwareAsset asset)
        {
            if (asset == null || String.IsNullOrWhiteSpace(asset.path) || Path.IsPathRooted(asset.path) || asset.path.IndexOf(':') >= 0 ||
                !Regex.IsMatch(asset.sha256 ?? "", "\\A[0-9a-fA-F]{64}\\z")) throw new InvalidDataException("Invalid bundled firmware file.");
            string path = Path.GetFullPath(Path.Combine(Root, asset.path));
            if (!path.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Firmware path escapes its bundle.");
            using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) {
                if (stream.Length < 8 || stream.Length > 8 * 1024 * 1024 ||
                    !BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").Equals(asset.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Bundled firmware SHA-256 mismatch: " + asset.path);
            }
            return path;
        }
        internal string Installed(uint family, NpFirmwareIdentity identity)
        {
            var release = For(family);
            return release == null ? "APP Not reported  |  USB Not reported  |  Boot Not reported" : release.Installed(identity);
        }
    }

    internal static class NpFirmwareNotice
    {
        static readonly HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static bool IsCurrent(NpFirmwareRelease release, NpFirmwareIdentity identity)
        {
            return release.Matches(identity) && release.appIdentity.Same(identity.App) && release.usbIdentity.Same(identity.Usb);
        }
        internal static string ReviewText(NpFirmwareRelease release, NpFirmwareIdentity identity)
        {
            return "WILD-X V" + release.family + "\r\nInstalled: " + release.Installed(identity) +
                "\r\nBundled: " + release.Versions + "\r\n\r\n" + release.stage + "\r\n" + release.board +
                "\r\n\r\n" + release.limits + "\r\n\r\n" +
                (identity == null ? "This firmware does not report installed image versions. A newer version cannot be confirmed.\r\n\r\n" : "") +
                "Open firmware management? Firmware will be written only after you select and confirm an update.";
        }
        internal static bool Offer(IWin32Window owner, string device, NpFirmwareRelease release, NpFirmwareIdentity identity)
        {
            if (release == null || String.IsNullOrEmpty(device) || IsCurrent(release, identity) ||
                (identity != null && !release.Matches(identity)) || !seen.Add(device + "|" + release.sourceCommit)) return false;
            return MessageBox.Show(owner, ReviewText(release, identity),
                release.HasUpdate(identity) ? "Firmware update available" : "Bundled firmware", MessageBoxButtons.YesNo,
                MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
        internal static string AppPath(IWin32Window owner, uint family)
        {
            var release = NpFirmwareCatalog.Bundled.For(family);
            return SelectAppPath(owner,release);
        }
        internal static string SelectAppPath(IWin32Window owner,NpFirmwareRelease release)
        {
            if (release == null) throw new IOException("Bundled firmware unavailable: " + NpFirmwareCatalog.Bundled.Error);
            string path = NpFirmwareCatalog.Bundled.CheckedPath(release.app);
            if (MessageBox.Show(owner, "Use bundled WILD-X V" + release.family + " APP " + release.appVersion + " (CRC " + release.appCrc32 + ")?\r\n\r\n" +
                release.board + "\r\n" + release.stage + "\r\n\r\n" + release.limits +
                "\r\n\r\nConfirm that this is the matching board. Only the APP is selected; USB service and recovery images are not sent through the APP updater.",
                "Select bundled APP", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return null;
            return path;
        }
        internal static void OpenFiles()
        {
            if (NpFirmwareCatalog.Bundled.Releases.Length == 0) throw new IOException("Bundled firmware unavailable: " + NpFirmwareCatalog.Bundled.Error);
            Process.Start(new ProcessStartInfo(NpFirmwareCatalog.Bundled.Root) { UseShellExecute = true });
        }
    }
}
