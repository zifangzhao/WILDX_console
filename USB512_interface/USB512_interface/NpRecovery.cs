// Optional, explicit-gap recovery. Each attempt owns new files and a decoder.
using System;
using System.IO;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
namespace USB512_interface {
 public partial class Form1 {
  private CheckBox npAutoRecover;
  private System.Windows.Forms.Timer npRetryTimer;
  private string npSessionFolder;
  private string npSessionDevicePath;
  private int npSegmentNumber,npEmptyAttempts,npConsecutiveShortFailures;
  private bool npInternalRestart,npRecoveryCancelled=true,npSessionHasGap;
  private volatile bool npFreshStartAvailable;
  private DateTime npLastReceiveUtc=DateTime.MinValue,npPreviousReceiveUtc=DateTime.MinValue,npSegmentStartUtc=DateTime.MinValue;
  private void NpInitializeRecovery() {
   npAutoRecover=new CheckBox {Text="NP auto-recover (gaps allowed)",Location=new Point(175,8),Size=new Size(265,22),Checked=true};
   Controls.Add(npAutoRecover);
   // A failed stream with saved data gets one prompt retry. If the board is
   // actually disconnected, later empty attempts back off to allow replug.
   npRetryTimer=new System.Windows.Forms.Timer {Interval=100};
   npRetryTimer.Tick+=delegate {
    npRetryTimer.Stop();
    if(npRecoveryCancelled || IsDisposed || Disposing)return;
    // Preserve the original target; EnsureStreaming never substitutes a board.
    devicePath=npSessionDevicePath;npInternalRestart=true;
    btnStart_Click(null,EventArgs.Empty);
   };
  }
  private string NpSegmentFolder(string fallback) {
   if(selectedDeviceKind!=DeviceKind.XeNp || !npAutoRecover.Checked) {npSessionFolder=null;npRecoveryCancelled=true;return fallback;}
   if(!npInternalRestart) {
    npSessionFolder=Path.Combine(saveFolder,"WILDX_SESSION_"+DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_fff")+"_"+Guid.NewGuid().ToString("N").Substring(0,6));
    Directory.CreateDirectory(npSessionFolder);npSegmentNumber=0;npEmptyAttempts=0;npConsecutiveShortFailures=0;npSessionHasGap=false;
    npSessionDevicePath=devicePath;npPreviousReceiveUtc=DateTime.MinValue;
    File.WriteAllText(Path.Combine(npSessionFolder,"session_events.csv"),"event_utc,event,segment,folder,raw_bytes,ap_rows,lfp_rows,previous_last_host_receive_utc,current_first_or_last_host_receive_utc,detail\r\n");
    File.WriteAllText(Path.Combine(npSessionFolder,"README.txt"),"GAP-AWARE RECORDING. Segments must NOT be concatenated as a continuous sample timeline.\r\nEach segment has independent framing/decoder state. Host receive timestamps are observations, not exact sample timestamps.\r\nMissing samples and exact gap duration are unknown; no zero padding or invented samples are inserted.\r\nA failed segment is an incomplete prefix; consult its capture status.\r\n");
   }
   npInternalRestart=false;npRecoveryCancelled=false;npLastReceiveUtc=DateTime.MinValue;
   npFreshStartAvailable=false;npSegmentStartUtc=DateTime.UtcNow;
   string folder=Path.Combine(npSessionFolder,"segment_"+(++npSegmentNumber).ToString("D4"));
   NpJournal("segment_attempt",folder,"Fresh START; no continuation of previous files");
   npAutoRecover.Enabled=false;return folder;
  }
  private static string Csv(string s){return "\""+(s??"").Replace("\"","\"\"")+"\"";}
  private static string Utc(DateTime t){return t==DateTime.MinValue?"unknown":t.ToString("O");}
  private void NpJournal(string kind,string folder,string detail) {
   if(npSessionFolder==null)return;
   bool attempt=kind=="segment_attempt";
   File.AppendAllText(Path.Combine(npSessionFolder,"session_events.csv"),String.Join(",",new[]{
    Csv(DateTime.UtcNow.ToString("O")),Csv(kind),Csv(npSegmentNumber.ToString()),Csv(folder),
    Csv((attempt?0:Interlocked.Read(ref neuropixelsPayloadBytes)).ToString()),Csv((attempt?0:Interlocked.Read(ref neuropixelsApRows)).ToString()),
    Csv((attempt?0:Interlocked.Read(ref neuropixelsLfpRows)).ToString()),Csv(Utc(npPreviousReceiveUtc)),Csv(Utc(npLastReceiveUtc)),Csv(detail)})+"\r\n");
  }
  private void NpFirstReceive(DateTime when,string folder) {
   if(npSessionFolder==null || folder!=runFolder)return;
   npLastReceiveUtc=when;
   NpJournal(npSessionHasGap?"resumed_first_host_receive":"first_host_receive",folder,
    npSessionHasGap?"Gap exists; exact missing sample count/duration UNKNOWN":"Host timestamp only");
  }
  private void NpCancelRecovery() {
   bool wasWaiting=npRetryTimer!=null && npRetryTimer.Enabled;
   npRecoveryCancelled=true;if(npRetryTimer!=null)npRetryTimer.Stop();
   if(npAutoRecover!=null)npAutoRecover.Enabled=true;
   if(wasWaiting && npSessionFolder!=null && captureThread==null) {
    NpJournal("session_stopped",runFolder,"User stopped during reconnect wait");
    File.WriteAllText(Path.Combine(npSessionFolder,"session_status.txt"),"state=stopped\r\nhas_gaps="+npSessionHasGap+"\r\nlossless=False\r\n");
    lblRecInfo.Text="Stopped gap-aware NP session";UpdateStartEnabled();
   }
  }
  internal static bool NpRetryable(string message) {
   if(message==null)return false;
   // Do not retry decoder/counter corruption, disk errors or unknown setup.
   return message.IndexOf("0xBADA0013",StringComparison.OrdinalIgnoreCase)>=0 ||
    message.Contains("NP USB read timed out") || message.Contains("no longer connected") ||
    message.Contains("Win32 error 1167") || message.Contains("Win32 error 31") ||
    message.Contains("Win32 error 2)") || message.Contains("Win32 error 6)") ||
    message.Contains("No such device");
  }
  private static bool NpFreshStartExporterFault(string message) {
   // This exact firmware fault was observed after a structurally clean prefix.
   // It cannot resume the old file, but a firmware-advertised fresh START can
   // establish a new independent segment. Other BADA codes stay fail-closed.
   return message!=null && message.IndexOf("V1 USB exporter fault is latched (0xBADA0000)",StringComparison.OrdinalIgnoreCase)>=0;
  }
  private bool NpFinishRecovery(string message) {
   if(npSessionFolder==null || selectedDeviceKind!=DeviceKind.XeNp)return false;
   bool failure=message.StartsWith("Capture error:",StringComparison.Ordinal);
   NpJournal(failure?"segment_failed":"segment_stopped",runFolder,message);
   bool freshExporterRetry=npFreshStartAvailable && NpFreshStartExporterFault(message);
   if(!npRecoveryCancelled && failure && (NpRetryable(message) || freshExporterRetry)) {
    npSessionHasGap=true;
    npEmptyAttempts=Interlocked.Read(ref neuropixelsPayloadBytes)>0?0:npEmptyAttempts+1;
    npConsecutiveShortFailures=(npSegmentStartUtc!=DateTime.MinValue &&
     DateTime.UtcNow-npSegmentStartUtc>=TimeSpan.FromSeconds(10))?0:npConsecutiveShortFailures+1;
    if(npLastReceiveUtc!=DateTime.MinValue)npPreviousReceiveUtc=npLastReceiveUtc;
    if(npEmptyAttempts<12 && npConsecutiveShortFailures<12) {
     NpJournal("gap_reconnect_pending",runFolder,"Reopen same UID with fresh START; previous segment incomplete; missing samples UNKNOWN");
     File.WriteAllText(Path.Combine(npSessionFolder,"session_status.txt"),"state=reconnecting\r\nhas_gaps=True\r\nlossless=False\r\n");
     btnStart.Enabled=false;btnStop.Enabled=true;btnRefresh.Enabled=false;comboDrives.Enabled=false;
     btnNpLogger.Enabled=false;btnSdInfo.Enabled=false;
     npAutoRecover.Enabled=false;lblRecInfo.Text="NP interrupted; reconnecting into a NEW segment (gap recorded). Stop cancels.";
     npRetryTimer.Interval=npEmptyAttempts==0?100:2000;
     npRetryTimer.Start();return true;
    }
    NpJournal("recovery_limit",runFolder,"Stopped after repeated empty or short failed segments");
   }
   npRecoveryCancelled=true;npAutoRecover.Enabled=true;
   File.WriteAllText(Path.Combine(npSessionFolder,"session_status.txt"),"state="+(failure?"failed":"stopped")+"\r\nhas_gaps="+npSessionHasGap+"\r\nlossless="+(!failure && !npSessionHasGap)+"\r\n");
   return false;
  }
 }
}
