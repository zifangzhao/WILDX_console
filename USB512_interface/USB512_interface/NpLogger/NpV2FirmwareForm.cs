using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CE32_console;

namespace USB512_interface
{
    // Existing callers share the same window; only the USB protocol is V2-specific.
    internal sealed class NpV2FirmwareForm : NpFirmwareForm
    {
        internal NpV2FirmwareForm(string selectedPath):base(selectedPath,2){}
        internal static string Uid(string path)
        {
            var match = Regex.Match(path ?? "", @"#(?:npv2-)?([0-9a-f]{24})#", RegexOptions.IgnoreCase);
            if (!match.Success || match.Groups[1].Value == new string('0', 24) || match.Groups[1].Value.Equals(new string('F',24),StringComparison.OrdinalIgnoreCase)) return null;
            return match.Groups[1].Value.ToUpperInvariant();
        }
        internal static void RequireIdleV2(string path,string uid)
        {
            using(var control=new NpSystemInfoControl(path)){var state=NpV2ServiceStatus.Parse(control.Read(0xc0,0x6e,0,32));state.RequireUid(uid);
                if(state.Busy!=0||state.Target!=0||(state.Mode!=2&&state.Mode!=3))throw new IOException("Select an idle V2 USB streaming or download service before a full update.");}
        }
        internal static bool WaitForV2(string uid)
        {
            var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<30000){var paths=DownloadPaths(uid);if(paths.Count>1)throw new IOException("The reconnecting V2 UID is ambiguous.");
                if(paths.Count==1)try{RequireIdleV2(paths[0],uid);return true;}catch(Win32Exception){}catch(IOException){}Thread.Sleep(250);}return false;
        }
        internal static string UsbModeOperation(ushort mode)
        {
            switch(mode){case 1:return "--sd";case 2:return "--stream";case 3:return "--download";default:throw new ArgumentOutOfRangeException("mode");}
        }
        internal static Task<string> SwitchUsbMode(string selectedPath,ushort mode)
        {
            string uid=Uid(selectedPath);
            if(uid==null || NpV1Management.UidFromPath(selectedPath)!=null)throw new IOException("Select the exact V2 USB device before changing mode.");
            return RunHelper(uid,UsbModeOperation(mode),null);
        }
        static NpV2ServiceStatus DownloadStatus(string path,string uid)
        {
            using(var control=new NpSystemInfoControl(path)){
                byte[] bytes;
                try{bytes=control.Read(0xc0,0x6e,0,32);}
                catch(Win32Exception ex) when(ex.NativeErrorCode==1 || ex.NativeErrorCode==31){
                    // Older standalone download firmware has no mode service.
                    // Confirm its read-only protocol; never send it a mode OUT.
                    var info=control.Read(0xc0,0x60,0,32);
                    if(BitConverter.ToUInt32(info,0)!=0x3144504e || BitConverter.ToUInt32(info,4)!=1 || BitConverter.ToUInt32(info,28)!=1)
                        throw new IOException("This firmware does not expose a supported USB mode or download service.");
                    return new NpV2ServiceStatus{Mode=3,Busy=BitConverter.ToUInt32(info,16)};
                }
                var state=NpV2ServiceStatus.Parse(bytes);state.RequireUid(uid);
                if(state.Mode!=2 && state.Mode!=3)throw new IOException("Selected V2 must be in USB streaming or download mode.");
                return state;
            }
        }
        static List<string> DownloadPaths(string uid){return NpDownloadNative.EnumerateNpPaths().Where(path=>NpV1Management.UidFromPath(path)==null && String.Equals(Uid(path),uid,StringComparison.OrdinalIgnoreCase)).ToList();}
        internal static async Task<string> EnsureDownload(string selectedPath,Func<bool> cancelled=null)
        {
            string uid=Uid(selectedPath);
            if(uid==null || NpV1Management.UidFromPath(selectedPath)!=null)throw new IOException("Select the exact V2 USB logger before downloading.");
            if(cancelled!=null && cancelled())throw new OperationCanceledException();
            var paths=DownloadPaths(uid);
            if(paths.Count!=1 || !paths[0].Equals(selectedPath,StringComparison.OrdinalIgnoreCase))throw new IOException("Selected V2 is absent or ambiguous. Refresh sources; another logger was not substituted.");
            var state=await Task.Run(()=>DownloadStatus(selectedPath,uid));
            if(state.Busy!=0 || state.Target!=0)throw new IOException("Stop recording and finish the active V2 operation before downloading.");
            if(state.Mode==3)return selectedPath;
            if(cancelled!=null && cancelled())throw new OperationCanceledException();
            await SwitchUsbMode(selectedPath,3);
            var elapsed=Stopwatch.StartNew();
            while(elapsed.ElapsedMilliseconds<30000){
                if(cancelled!=null && cancelled())throw new OperationCanceledException();
                await Task.Delay(250);paths=DownloadPaths(uid);
                if(paths.Count>1)throw new IOException("Ambiguous V2 UID after download-mode entry.");
                if(paths.Count==0)continue;
                try{state=await Task.Run(()=>DownloadStatus(paths[0],uid));
                    if(state.Mode==3 && state.Busy==0 && state.Target==0)return paths[0];
                }catch(Win32Exception){ /* The selected device may still be re-enumerating. */ }
            }
            throw new IOException("Selected V2 did not return to idle USB download within 30 seconds. No mode request was replayed.");
        }
        internal static async Task<string> RunHelper(string uid,string operation,Action<string> append)
        {
            string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FirmwareUpdater", "XeNpFirmwareUpdater.exe");
            if (!File.Exists(executable)) throw new IOException("Bundled V2 firmware updater is missing; rebuild the WILDX application.");
            var arguments = new[] { operation, "--uid", uid };
            string command = String.Join(" ", Array.ConvertAll(arguments, NpFirmwareFlash.Quote));
            string output = await Task.Run(() => {
                var text = new StringBuilder(); object sync = new object();
                using (var process = new Process { StartInfo = new ProcessStartInfo(executable, command) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } }) {
                    DataReceivedEventHandler receive = (sender, e) => { if (e.Data == null) return; lock (sync) { if (text.Length < 4*1024*1024) text.AppendLine(e.Data); } if(append!=null)append(e.Data); };
                    process.OutputDataReceived += receive; process.ErrorDataReceived += receive;
                    if (!process.Start()) throw new IOException("V2 updater could not start.");
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    if (!process.WaitForExit(300000)) { process.Kill(); process.WaitForExit(); throw new IOException("V2 updater timed out; outcome unknown. No retry sent."); }
                    process.WaitForExit();
                    string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WILDX firmware logs"); Directory.CreateDirectory(folder);
                    string saved = Path.Combine(folder, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "_" + uid + ".log"); File.WriteAllText(saved, command + Environment.NewLine + text);
                    if (process.ExitCode != 0) throw new IOException("V2 operation failed; no automatic retry. See " + saved);
                    return text.ToString();
                }
            });
            return output.Trim();
        }
    }
}
