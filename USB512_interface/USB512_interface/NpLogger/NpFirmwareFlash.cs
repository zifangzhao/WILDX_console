using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace USB512_interface
{
    internal sealed class NpFirmwareFlash
    {
        internal sealed class CliResult { internal int ExitCode; internal string Output; }
        internal sealed class DfuDevice { internal string Port,Serial; }
        readonly Func<string[],CliResult> invoke;
        readonly Action<string,int> progress;
        readonly string directory;
        string uid;
        int physicalFlashBytes;
        internal NpFirmwareFlash(Func<string[],CliResult> invoke,Action<string,int> progress,string directory)
        {this.invoke=invoke;this.progress=progress;this.directory=directory;}
        internal static string DefaultCli()
        {
            foreach(string root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}){
                string path=Path.Combine(root,"STMicroelectronics","STM32Cube","STM32CubeProgrammer","bin","STM32_Programmer_CLI.exe");
                if(File.Exists(path))return path;
            }
            return null;
        }
        internal static string Quote(string argument)
        {
            if(argument==null || argument.IndexOfAny(new[]{'\0','\r','\n'})>=0)throw new ArgumentException("Invalid CLI argument.");
            var result=new StringBuilder("\"");int slashes=0;
            foreach(char c in argument){if(c=='\\'){slashes++;continue;}if(c=='\"'){result.Append('\\',slashes*2+1);result.Append(c);}else{result.Append('\\',slashes);result.Append(c);}slashes=0;}
            return result.Append('\\',slashes*2).Append('"').ToString();
        }
        internal static CliResult RunProcess(string executable,string[] arguments)
        {
            if(!File.Exists(executable) || !Path.GetFileName(executable).Equals("STM32_Programmer_CLI.exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Locate STM32_Programmer_CLI.exe from an installed STM32CubeProgrammer distribution.");
            var command=new StringBuilder();foreach(string argument in arguments){if(command.Length>0)command.Append(' ');command.Append(Quote(argument));}
            var output=new StringBuilder();object sync=new object();
            using(var process=new Process{StartInfo=new ProcessStartInfo{FileName=Path.GetFullPath(executable),Arguments=command.ToString(),UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}}){
                DataReceivedEventHandler capture=delegate(object sender,DataReceivedEventArgs e){if(e.Data!=null)lock(sync){if(output.Length<4*1024*1024)output.AppendLine(e.Data);}};
                process.OutputDataReceived+=capture;process.ErrorDataReceived+=capture;
                if(!process.Start())throw new IOException("STM32CubeProgrammer could not start.");
                process.BeginOutputReadLine();process.BeginErrorReadLine();
                int timeout=Array.IndexOf(arguments,"-l")>=0?15000:Array.IndexOf(arguments,"-d")>=0?300000:120000;
                if(!process.WaitForExit(timeout)){process.Kill();process.WaitForExit();throw new IOException("STM32CubeProgrammer timed out. No subsequent write, retry or exit command was sent.");}
                process.WaitForExit();return new CliResult{ExitCode=process.ExitCode,Output=output.ToString()};
            }
        }
        internal static List<DfuDevice> ParseList(string output)
        {
            var devices=new List<DfuDevice>();
            var blocks=Regex.Matches(output??"",@"Device Index\s*:\s*(USB\d+)(.*?)(?=Device Index\s*:|\z)",RegexOptions.IgnoreCase|RegexOptions.Singleline);
            foreach(Match block in blocks){
                var serial=Regex.Match(block.Groups[2].Value,@"Serial number\s*:\s*([0-9a-f]{12})\b",RegexOptions.IgnoreCase);
                if(!serial.Success)throw new InvalidDataException("DFU list lacks an unambiguous 12-character USB serial.");
                devices.Add(new DfuDevice{Port=block.Groups[1].Value.ToUpperInvariant(),Serial=serial.Groups[1].Value.ToUpperInvariant()});
            }
            var total=Regex.Match(output??"",@"Total number of (?:available )?devices(?: in DFU mode)?\s*:\s*(\d+)",RegexOptions.IgnoreCase);
            bool empty=Regex.IsMatch(output??"",@"No (?:STM32 )?(?:DFU device|device in DFU mode|STM32 device in DFU mode)(?:s)? (?:is )?connected",RegexOptions.IgnoreCase);
            if(total.Success && Int32.Parse(total.Groups[1].Value)!=devices.Count)throw new InvalidDataException("DFU list count is inconsistent.");
            if(devices.Count==0 && !(total.Success && total.Groups[1].Value=="0") && !empty)throw new InvalidDataException("DFU enumeration could not be interpreted. No mode change or write is allowed.");
            if(devices.Count>1)throw new IOException("Multiple DFU devices are connected. Disconnect other DFU targets before updating; none was selected.");
            return devices;
        }
        string Call(string[] arguments,bool listing=false)
        {
            var result=invoke(arguments);
            File.AppendAllText(Path.Combine(directory,"operation.log"),String.Join(" ",arguments)+Environment.NewLine+(result.Output??"")+Environment.NewLine);
            if(listing && (result.ExitCode==0 || result.ExitCode==1)){
                string remainder=Regex.Replace(result.Output??"",@"(?im)^\s*(?:Error\s*:\s*)?No (?:STM32 )?(?:DFU device|device in DFU mode|STM32 device in DFU mode)(?:s)? (?:is )?connected\.?\s*$","");
                if(remainder!=result.Output && !Regex.IsMatch(remainder,@"(?im)^\s*Error\s*:"))return result.Output;
            }
            if(result.ExitCode!=0 || Regex.IsMatch(result.Output??"",@"(?im)^\s*Error\s*:|Verification[^\r\n]*(?:fail|error)|Download[^\r\n]*failed"))
                throw new IOException("STM32CubeProgrammer stopped (exit "+result.ExitCode+"). See "+Path.Combine(directory,"operation.log")+". No automatic retry was performed.");
            return result.Output;
        }
        List<DfuDevice> List(){return ParseList(Call(new[]{"-l","usb"},true));}
        static string[] Connect(DfuDevice device,params string[] commands)
        {
            var args=new List<string>{"-c","port="+device.Port,"sn="+device.Serial,"VID=0x0483","PID=0xDF11"};args.AddRange(commands);return args.ToArray();
        }
        internal static string ReadUid(byte[] bytes)
        {
            if(bytes==null || bytes.Length!=12)throw new InvalidDataException("MCU UID read must contain exactly 12 bytes.");
            return BitConverter.ToUInt32(bytes,0).ToString("X8")+BitConverter.ToUInt32(bytes,4).ToString("X8")+BitConverter.ToUInt32(bytes,8).ToString("X8");
        }
        DfuDevice CheckTarget(string serial=null)
        {
            var devices=List();
            if(devices.Count!=1 || (serial!=null && devices[0].Serial!=serial))throw new IOException("The bound DFU target disappeared or its identity changed. No other device will be substituted.");
            var device=devices[0];string identity=Path.Combine(directory,Guid.NewGuid().ToString("N")+"-uid.bin"),size=Path.Combine(directory,Guid.NewGuid().ToString("N")+"-size.bin");
            Call(Connect(device,"-u","0x0BFA0700","12",identity,"-u","0x0BFA07A0","2",size));
            if(ReadUid(File.ReadAllBytes(identity))!=uid)throw new IOException("DFU MCU UID does not match selected logger "+uid+". No write was allowed.");
            var bytes=File.ReadAllBytes(size);
            if(bytes.Length!=2 || (BitConverter.ToUInt16(bytes,0)!=2048 && BitConverter.ToUInt16(bytes,0)!=4096))throw new InvalidDataException("Unexpected STM32U5 flash geometry.");
            int reported=BitConverter.ToUInt16(bytes,0)*1024;
            if(physicalFlashBytes!=0 && physicalFlashBytes!=reported)throw new InvalidDataException("Physical flash geometry changed during the operation.");
            physicalFlashBytes=reported;
            return device;
        }
        byte[] ReadFlash(DfuDevice device,string name)
        {
            string path=Path.Combine(directory,name);Call(Connect(device,"-u","0x08000000",physicalFlashBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),path));
            var bytes=File.ReadAllBytes(path);if(bytes.Length!=physicalFlashBytes)throw new InvalidDataException("Incomplete physical flash readback.");return bytes;
        }
        internal string Execute(NpFirmwareImage image,string selectedUid,bool alreadyDfu,Action requestDfu,Func<bool> reconnect)
        {
            if(image==null || !Regex.IsMatch(selectedUid??"",@"\A[0-9A-Fa-f]{24}\z") || selectedUid==new string('0',24) || selectedUid.Equals(new string('F',24),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("An explicitly selected full logger MCU UID is required.");
            uid=selectedUid.ToUpperInvariant();Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory,"operation.log"),"Selected logger UID: "+uid+"; validated image SHA256: "+image.SourceSha256+"; scope: "+(image.Full?"complete fused firmware":"APP only")+Environment.NewLine);
            var initial=List();
            if(alreadyDfu){if(initial.Count!=1)throw new IOException("The previously selected logger is not uniquely present in DFU.");}
            else{
                if(initial.Count!=0)throw new IOException("A pre-existing DFU device is connected. No DFU request or write was sent.");
                progress("Requesting guarded DFU for logger "+uid,5);requestDfu();
                var watch=Stopwatch.StartNew();
                do{var available=List();if(available.Count==1)break;if(watch.ElapsedMilliseconds>=30000)throw new IOException("Selected logger did not enumerate in DFU within 30 seconds. No write was sent.");Thread.Sleep(250);}while(true);
            }
            progress("Checking full MCU UID and flash geometry",10);var device=CheckTarget();
            progress("Reading complete physical flash backup; preserving configuration and omitted bytes",15);var before=ReadFlash(device,"before.bin");var expected=image.ExpectedFlash(before);
            string hex=Path.Combine(directory,"validated-pages.hex");
            using(var operation=image.WritePageHex(hex,before,expected)){
                progress("Rechecking full MCU UID before programming or unchanged-image verification",25);device=CheckTarget(device.Serial);
                if(operation!=null){
                    string writtenUid=Path.Combine(directory,"write-uid.bin");
                    progress(image.Full?"Writing and verifying changed fused firmware pages; do not disconnect power":"Writing and verifying the changed APP pages",35);
                    string output=Call(Connect(device,"-d",hex,"-v","-u","0x0BFA0700","12",writtenUid));
                    if(!Regex.IsMatch(output,@"Download verified successfully|Verification[^\r\n]*(?:successful|successfully)",RegexOptions.IgnoreCase))throw new IOException("CubeProgrammer did not confirm verification. Remaining in DFU; no exit or retry was sent.");
                    if(ReadUid(File.ReadAllBytes(writtenUid))!=uid)throw new IOException("MCU identity changed during programming. No exit or retry was sent.");
                }else{
                    const string unchanged="Selected firmware already matches; no flash pages programmed.";
                    File.AppendAllText(Path.Combine(directory,"operation.log"),unchanged+Environment.NewLine);progress(unchanged,35);
                }
                progress("Checking exact flash readback and preserved configuration",75);device=CheckTarget(device.Serial);var after=ReadFlash(device,"after.bin");
                for(int i=0;i<expected.Length;i++)if(after[i]!=expected[i])throw new IOException("Flash readback or preserved byte differs at 0x"+(NpFirmwareImage.FlashBase+(uint)i).ToString("X8")+". Remaining in DFU.");
            }
            progress("Firmware bytes verified; exiting DFU for the same MCU UID",90);device=CheckTarget(device.Serial);Call(Connect(device,"-s","0x08000000"));
            if(!reconnect())throw new IOException("Firmware verified, but logger "+uid+" did not reconnect in a verified idle USB role. Update completion is not confirmed; see "+directory+".");
            string result="Verified "+(image.Full?"full firmware":"APP")+" and exact UID "+uid+" reconnected; configuration and omitted flash bytes preserved. Log/readbacks: "+directory;
            File.AppendAllText(Path.Combine(directory,"operation.log"),result+Environment.NewLine);progress(result,100);return result;
        }
    }
}
