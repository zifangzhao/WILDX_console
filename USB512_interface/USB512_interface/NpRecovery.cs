// Optional, explicit-gap recovery. Each attempt owns new files and a decoder.
using System;
using System.IO;
using System.Drawing;
using System.ComponentModel;
using System.Threading;
using System.Windows.Forms;
namespace USB512_interface {
 public partial class Form1 {
  private CheckBox npAutoRecover;
  private System.Windows.Forms.Timer npRetryTimer;
  private string npSessionFolder;
  private string npLatestRecordingFolder,npPreviousRecordingFolder;
  private string npSessionDevicePath;
  private string npSessionDeviceUid;
  private DeviceKind npSessionDeviceKind;
  private int npSegmentNumber,npEmptyAttempts,npConsecutiveShortFailures;
  private bool npInternalRestart,npRecoveryCancelled=true,npSessionHasGap;
  private bool npRecoveryAttempt,npV2RecoverySafe,npV2RecoveryComplete;
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
    devicePath=npSessionDevicePath;selectedDeviceKind=npSessionDeviceKind;npInternalRestart=true;
    btnStart_Click(null,EventArgs.Empty);
   };
  }
  private string NpSegmentFolder(string fallback) {
   npRecoveryAttempt=npInternalRestart;npV2RecoverySafe=false;npV2RecoveryComplete=false;
   if((selectedDeviceKind!=DeviceKind.XeNp && selectedDeviceKind!=DeviceKind.NpV2) || !npAutoRecover.Checked) {
    npSessionFolder=null;npLatestRecordingFolder=npPreviousRecordingFolder=null;npRecoveryCancelled=true;npInternalRestart=false;return fallback;
   }
   DateTime start=DateTime.Now;
   string folder=NpFolderNaming.CreateFresh(saveFolder,NpFolderNaming.Name(npSessionLabel.Text,start,
    "WILDX_"+start.ToString("yyyy-MM-dd_HH-mm-ss_fff")));
   if(!npInternalRestart) {
    npSessionFolder=folder;npLatestRecordingFolder=npPreviousRecordingFolder=null;
    Directory.CreateDirectory(npSessionFolder);npSegmentNumber=0;npEmptyAttempts=0;npConsecutiveShortFailures=0;npSessionHasGap=false;
    npSessionDevicePath=devicePath;npSessionDeviceKind=selectedDeviceKind;
    npSessionDeviceUid=selectedDeviceKind==DeviceKind.NpV2?NpV2FirmwareForm.Uid(devicePath):NpV1Management.UidFromPath(devicePath);
    npPreviousReceiveUtc=DateTime.MinValue;
    File.WriteAllText(Path.Combine(npSessionFolder,"session_events.csv"),"event_utc,event,recording,folder,raw_bytes,ap_rows,lfp_rows,previous_last_host_receive_utc,current_first_or_last_host_receive_utc,detail\r\n");
    File.WriteAllText(Path.Combine(npSessionFolder,"README.txt"),"AUTOMATIC RESTARTS CREATE SEPARATE RECORDINGS. Each fresh START creates a sibling folder with a new PC start-attempt timestamp.\r\nEach recording has independent files, metadata and decoder state. Host receive timestamps are observations, not exact sample timestamps.\r\nMissing samples and exact gap duration between recordings are unknown; no zero padding or invented samples are inserted.\r\nA failed recording is an incomplete prefix; consult its capture status.\r\nDevice kind="+npSessionDeviceKind+"; target UID="+(npSessionDeviceUid??"unknown")+".\r\nRecovery reopens only this target, with the selected rate/probes/configuration retained.\r\n");
   }
   npPreviousRecordingFolder=npLatestRecordingFolder;npLatestRecordingFolder=folder;
   npInternalRestart=false;npRecoveryCancelled=false;npLastReceiveUtc=DateTime.MinValue;
   npFreshStartAvailable=false;npSegmentStartUtc=DateTime.UtcNow;
   ++npSegmentNumber;
   if(npPreviousRecordingFolder!=null)File.WriteAllText(Path.Combine(folder,"recovery_link.json"),
    new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(NpRecordingSettings.Fields(
     "previous_recording_folder",npPreviousRecordingFolder,"first_recording_folder",npSessionFolder,
     "recovery_journal",Path.Combine(npSessionFolder,"session_events.csv"),"recording_number",npSegmentNumber,
     "gap_duration_known",false,"missing_sample_count_known",false,"device_uid",npSessionDeviceUid))+"\n");
   NpJournal("recording_attempt",folder,"Fresh START; no continuation of previous files");
   npAutoRecover.Enabled=false;return folder;
  }
  private static string Csv(string s){return "\""+(s??"").Replace("\"","\"\"")+"\"";}
  private static string Utc(DateTime t){return t==DateTime.MinValue?"unknown":t.ToString("O");}
  private void NpJournal(string kind,string folder,string detail) {
   if(npSessionFolder==null)return;
   bool attempt=kind=="recording_attempt";
   File.AppendAllText(Path.Combine(npSessionFolder,"session_events.csv"),String.Join(",",new[]{
    Csv(DateTime.UtcNow.ToString("O")),Csv(kind),Csv(npSegmentNumber.ToString()),Csv(folder),
    Csv((attempt?0:Interlocked.Read(ref neuropixelsPayloadBytes)).ToString()),Csv((attempt?0:Interlocked.Read(ref neuropixelsApRows)).ToString()),
    Csv((attempt?0:Interlocked.Read(ref neuropixelsLfpRows)).ToString()),Csv(Utc(npPreviousReceiveUtc)),Csv(Utc(npLastReceiveUtc)),Csv(detail)})+"\r\n");
  }
  private void NpFirstReceive(DateTime when,string folder) {
   ObserveRecordingSummary(when,folder);
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
    NpRecordingSettings.RefreshBundle(npSessionFolder);
    lblRecInfo.Text="Stopped gap-aware NP session";
    UpdateStartEnabled();
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
  private static bool NpV2TransportCode(int code) {
   return code==2 || code==6 || code==31 || code==121 || code==1167;
  }
  internal static bool NpV2TransportRetryable(string message) {
   if(message==null)return false;
   // Scope the numeric error to WinUSB open/read, not an equally numbered
   // disk error or a command whose application outcome is unknown.
   if(message.StartsWith("IOException: ",StringComparison.Ordinal))message=message.Substring(13);
   if(message.StartsWith("Win32Exception: ",StringComparison.Ordinal))message=message.Substring(16);
   bool read=message.StartsWith("V2 bulk completion failed (Win32 ",StringComparison.Ordinal) ||
    message.StartsWith("V2 queue bulk read failed",StringComparison.Ordinal) ||
    message.StartsWith("V2 finish bulk read failed",StringComparison.Ordinal) ||
    message.StartsWith("V2 wait for bulk read failed",StringComparison.Ordinal) ||
    message.StartsWith("CreateFile for V2 WinUSB failed",StringComparison.Ordinal) ||
    message.StartsWith("WinUsb_Initialize for V2 failed",StringComparison.Ordinal);
   if(!read)return false;
   foreach(int code in new[]{2,6,31,121,1167})
    if(message.Contains("(Win32 "+code+";") || message.Contains("(Win32 error "+code+")"))return true;
   return false;
  }
  internal static bool NpV2Retryable(Exception error) {
   if(error is IOException && error.Message=="Selected NP2 USB device is no longer connected; waiting for the same UID.")return true;
   if(error is TimeoutException)return error.Message=="WILD-X V2 did not re-enumerate in idle USB-stream mode within 30 seconds.";
   var win32=error as Win32Exception;
   return win32!=null && NpV2TransportCode(win32.NativeErrorCode) &&
    NpV2TransportRetryable(win32.Message+" (Win32 error "+win32.NativeErrorCode+")");
  }
  internal static bool NpV2Retryable(NpV2RecordingResult result) {
   if(result==null || result.Complete || result.CancelledBeforeCapture ||
    result.RawWriterError!=null || result.DecodeError!=null ||
    result.RawDroppedBlocks!=0 || result.DecodedDroppedLaneBlocks!=0 ||
    result.VerifiedBlocks!=result.ReceivedBlocks)return false;
   // Best effort: the exact stopped ring-overrun fault can start a NEW recording.
   // Never resume the overwritten ring or append across the unknown gap. The
   // existing idle/busy/UID handshake still owns any subsequent mode reset.
   var capture=result.FinalCapture;var source=result.FinalSource;
   if(result.ReceivedBlocks>0 && capture!=null && capture.Valid &&
    capture.State==255U && capture.Error==4U && capture.ProducerError==6U && capture.SourceError==0U &&
    source!=null && source.Valid && source.State==11U && source.Error==0U && source.I2cError==0U &&
    (source.ChainFailMask&0xFFFF0C30U)==0U &&
    (source.ChainFailMask==0U || (source.Flags&16U)!=0U))return true;
   return (capture==null || (capture.State!=255U && capture.Error==0U &&
     capture.ProducerError==0U && capture.SourceError==0U)) &&
    (source==null || (source.State!=255U && source.Error==0U)) && NpV2TransportRetryable(result.Error);
  }
  private static bool NpFreshStartExporterFault(string message) {
   // This exact firmware fault was observed after a structurally clean prefix.
   // It cannot resume the old file, but a firmware-advertised fresh START can
   // establish a new independent recording. Other BADA codes stay fail-closed.
   return message!=null && message.IndexOf("V1 USB exporter fault is latched (0xBADA0000)",StringComparison.OrdinalIgnoreCase)>=0;
  }
  private bool NpFinishRecovery(string message) {
   if(npSessionFolder==null || (selectedDeviceKind!=DeviceKind.XeNp && selectedDeviceKind!=DeviceKind.NpV2))return false;
   bool failure=message.StartsWith("Capture error:",StringComparison.Ordinal);
   bool v2=selectedDeviceKind==DeviceKind.NpV2;
   bool intact=!failure && (!v2 || npV2RecoveryComplete);
   NpJournal(failure?"recording_failed":intact?"recording_stopped":"recording_incomplete",runFolder,message);
   bool freshExporterRetry=!v2 && npFreshStartAvailable && NpFreshStartExporterFault(message);
   bool retry=v2?npV2RecoverySafe && npSessionDeviceUid!=null:NpRetryable(message) || freshExporterRetry;
   if(!npRecoveryCancelled && failure && retry) {
    npSessionHasGap=true;
    npEmptyAttempts=Interlocked.Read(ref neuropixelsPayloadBytes)>0?0:npEmptyAttempts+1;
    npConsecutiveShortFailures=(npSegmentStartUtc!=DateTime.MinValue &&
     DateTime.UtcNow-npSegmentStartUtc>=TimeSpan.FromSeconds(10))?0:npConsecutiveShortFailures+1;
    if(npLastReceiveUtc!=DateTime.MinValue)npPreviousReceiveUtc=npLastReceiveUtc;
    if(npEmptyAttempts<12 && npConsecutiveShortFailures<12) {
     NpJournal("gap_reconnect_pending",runFolder,"Reopen same UID with fresh START; previous recording incomplete; missing samples UNKNOWN");
     File.WriteAllText(Path.Combine(npSessionFolder,"session_status.txt"),"state=reconnecting\r\nhas_gaps=True\r\nlossless=False\r\n");
     btnStart.Enabled=false;btnStop.Enabled=true;btnRefresh.Enabled=false;comboDrives.Enabled=false;
     btnNpLogger.Enabled=false;btnSdInfo.Enabled=false;
     npAutoRecover.Enabled=false;lblRecInfo.Text="NP interrupted; starting a new timestamped recording. Stop cancels.";
     npRetryTimer.Interval=npEmptyAttempts==0?100:2000;
     npRetryTimer.Start();return true;
    }
    NpJournal("recovery_limit",runFolder,"Stopped after repeated empty or short failed recordings");
   }
   npRecoveryCancelled=true;npAutoRecover.Enabled=true;
   File.WriteAllText(Path.Combine(npSessionFolder,"session_status.txt"),"state="+(failure?"failed":intact?"stopped":"incomplete")+"\r\nhas_gaps="+npSessionHasGap+"\r\nlossless="+(intact && !npSessionHasGap)+"\r\n");
   NpRecordingSettings.RefreshBundle(npSessionFolder);
   return false;
  }
 }
}
