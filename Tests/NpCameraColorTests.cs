using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Windows.Forms;
using CE32_console;
using USB512_interface;

static class NpCameraColorTests
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static byte[] Frame(Func<int,int,int> value)
    {
        var raw = new byte[160000];
        for (int y = 0; y < 320; y++) for (int n = 0; n < 12; n++) raw[y * 492 + n] = 0x55;
        for (int y = 0; y < 320; y++) for (int x = 0; x < 320; x += 2) {
            int a = value(x,y), b = value(x+1,y), at = y * 492 + 12 + x / 2 * 3;
            raw[at] = (byte)(0x80 | (a >> 3));
            raw[at+1] = (byte)(((a & 7) << 5) | 8 | (b >> 7));
            raw[at+2] = (byte)((b & 127) << 1);
        }
        return raw;
    }
    static Color Center(Bitmap image) { return image.GetPixel(160,160); }
    static byte[] Shift(byte[] raw, Func<int,int> shift)
    {
        var moved = new byte[raw.Length];
        for (int row = 0; row < 320; row++) for (int bit = 0; bit < 492 * 8; bit++) {
            int source = row * 492 * 8 + bit, target = source + shift(row);
            if (target >= 0 && target < 157452 * 8 && (raw[source >> 3] & (128 >> (source & 7))) != 0)
                moved[target >> 3] |= (byte)(128 >> (target & 7));
        }
        return moved;
    }
    static int Brightness(Color c) { return (c.R + c.G + c.B) / 3; }
    static void Same(Bitmap a, Bitmap b)
    {
        for (int y = 0; y < 320; y++) for (int x = 0; x < 320; x++)
            if (a.GetPixel(x,y) != b.GetPixel(x,y)) throw new Exception("Preview/export color mismatch");
        Check(true, "All preview pixels match");
    }
    [STAThread] static int Main(string[] args)
    {
        try {
            Directory.CreateDirectory(args[0]);
            for (int value = 0; value <= 1023; value++) {
                int v = value;
                Check(WILD_VideoDecoder.MeasureExposure(Frame((x,y)=>v)) == v, "Packed exposure code " + v);
            }
            var invalid = Frame((x,y)=>512);
            foreach (int row in new[]{80,160,240}) foreach (int col in new[]{80,160,240}) {
                int at = row*492+12+col/2*3; byte saved = invalid[at]; invalid[at] &= 0x7f;
                Check(WILD_VideoDecoder.MeasureExposure(invalid) == -1, "Invalid metering sample used"); invalid[at]=saved;
            }
            foreach (byte[] bad in new[]{null,new byte[159999],new byte[160001]}) {
                bool rejected=false;try { new WILD_VideoDecoder.PreviewDecoder().Decode(bad); } catch(InvalidDataException) { rejected=true; }
                Check(rejected,"Invalid frame size accepted");
            }
            var decoder = new WILD_VideoDecoder.PreviewDecoder();
            byte[] grey=Frame((x,y)=>265+(x+y)%21), highlighted=Frame((x,y)=>y==0||x>=40&&x<48&&y>=40&&y<48?1022:265+(x+y)%21);
            Color plain;using(var image=decoder.Decode(grey))plain=Center(image);
            using(var image=decoder.Decode(highlighted)) {
                Check(Brightness(Center(image)) >= 140,"Sparse highlights darkened ordinary scene");
                Check(Math.Abs(Brightness(Center(image))-Brightness(plain)) <= 1,"Sparse highlight changed display exposure");
                image.Save(Path.Combine(args[0],"highlight-resistant.png"));
            }
            byte[] bright=Frame((x,y)=>(x+y)%2==1?980:1000), original=(byte[])bright.Clone();
            decoder.Reset();
            using(var image=decoder.Decode(bright)) {
                Color c=Center(image);
                Check(c.R>=245&&c.G>=245&&c.B>=245&&Math.Abs(c.R-c.G)<=4,"Near-white became magenta");
                image.Save(Path.Combine(args[0],"near-white-fixed.png"));
            }
            Check(original.SequenceEqual(bright),"Raw camera bytes modified");
            using(var image=decoder.Decode(Frame((x,y)=>1023))) Check(Center(image).ToArgb()==Color.White.ToArgb(),"Saturated white became black");
            decoder.Reset();
            using(var image=decoder.Decode(Frame((x,y)=>0))) Check(Center(image).ToArgb()==Color.Black.ToArgb(),"Black offset invented");
            decoder.Reset();using(var image=decoder.Decode(grey))plain=Center(image);
            using(var image=decoder.Decode(Frame((x,y)=>x<80?1023:265+(x+y)%21)))
                Check(Math.Abs(Brightness(Center(image))-Brightness(plain))<=4,"Exposure jumped on scene change");
            var gap=new byte[160000];Buffer.BlockCopy(new byte[]{0x43,0x45,0x36,0x34,0x43,0x41,0x4d,0x47,0x41,0x50,1,0,0xa5,0x5a,0x69,0x96},0,gap,0,16);
            using(var previous=decoder.Decode(grey))using(var held=decoder.Decode(gap))Same(previous,held);
            decoder.Reset();using(var empty=decoder.Decode(gap))Check(Center(empty).ToArgb()==Color.Black.ToArgb(),"New playback held previous recording");
            // Native BGGR: blue at even/even, red at odd/odd. Rotation
            // produces RGGB; do not infer the fixture from decoder branches.
            var colorFrame=Frame((x,y)=>(x&1)==0&&(y&1)==0?200:(x&1)==1&&(y&1)==1?800:400);
            decoder.Reset();using(var image=decoder.Decode(colorFrame)){Color c=Center(image);Check(c.R>c.G&&c.G>c.B,"Rotated RGGB color order changed");}
            // A uniformly colored/gray wall is not a black reference, even
            // though its Bayer channel values differ substantially.
            decoder.Reset();using(var image=decoder.Decode(colorFrame)) {
                Color c=Center(image);Check(c.B>=120 && c.G>=165 && c.R>=225,"Flat color was stretched/clipped by shadow correction");
            }
            decoder.Reset();using(var image=decoder.Decode(Frame((x,y)=>275)))
                Check(Brightness(Center(image))>=180,"Flat mid-gray was mistaken for sensor black");
            // Pedestal + spatial scene contrast: blacks should no longer be
            // lifted gray, while shadow detail and highlight color survive.
            var contrast=Frame((x,y)=>x<80?950:x>=240?250:280+(y%21));
            decoder.Reset();using(var image=decoder.Decode(contrast)) {
                Check(Brightness(image.GetPixel(20,160))<40,"Shadows remain washed out");
                Check(Brightness(Center(image))>50,"Shadow detail was crushed");
                Check(Brightness(image.GetPixel(290,160))>=240,"Highlights were dimmed");
            }
            decoder.Reset();using(var image=decoder.Decode(Frame((x,y)=>1000)))
                Check(Brightness(Center(image))>=250,"Bright flat scene no longer stays white");
            var detail=Frame((x,y)=>(x*7+y*13+(x%2)*97)%1024);
            foreach(int shift in new[]{-96,-95,-78,-50,-49,-1,1,7,8,13,64,95,96}) {
                byte[] moved=Shift(detail,row=>shift), saved=(byte[])moved.Clone();
                decoder.Reset();
                using(var expected=new WILD_VideoDecoder.PreviewDecoder().Decode(detail))
                using(var actual=decoder.Decode(moved)) Same(expected,actual);
                Check(!decoder.LastRecovery.Held && decoder.LastRecovery.CorrectedRows==320,"Complete shifted raster was not recovered: "+shift);
                Check(saved.SequenceEqual(moved),"Recovery changed native bytes");
                Check(WILD_VideoDecoder.MeasureExposure(moved)==-1,"Shifted image drove automatic exposure");
            }
            foreach(int value in new[]{0,256,512,1023}) foreach(int shift in new[]{-1,1}) {
                var flat=Frame((x,y)=>value);
                decoder.Reset();
                using(var expected=new WILD_VideoDecoder.PreviewDecoder().Decode(flat))
                using(var actual=decoder.Decode(Shift(flat,row=>shift))) Same(expected,actual);
                Check(!decoder.LastRecovery.Held,"Flat/saturated phase was guessed incorrectly");
            }
            var ambiguous=Frame((x,y)=>341);
            for(int row=0;row<320;row++)for(int n=0;n<12;n++)ambiguous[row*492+n]=0xaa;
            decoder.Reset();using(var image=decoder.Decode(Shift(ambiguous,row=>1)))
                Check(decoder.HoldingImage,"Ambiguous training/pixel pattern was guessed");
            decoder.Reset();
            using(var expected=new WILD_VideoDecoder.PreviewDecoder().Decode(detail))
            using(var actual=decoder.Decode(Shift(detail,row=>row<160?0:1))) Same(expected,actual);
            Check(decoder.LastRecovery.CorrectedRows==160 && !decoder.LastRecovery.Held,"Within-frame row phase change failed");
            // Lost source pixels, malformed guards, or a truncated tail cannot
            // be recreated. Keep one whole previous image at the same time slot.
            foreach(byte[] damaged in new[]{Shift(detail,row=>-104),Shift(detail,row=>257)}) {
                decoder.Reset();using(var before=decoder.Decode(detail))using(var held=decoder.Decode(damaged))Same(before,held);
                Check(decoder.HoldingImage && decoder.LastRecovery.CorrectedRows==0,"Lost/out-of-range pixels invented");
            }
            byte[] torn=(byte[])detail.Clone();Array.Clear(torn,236*492,84*492);
            decoder.Reset();using(var before=decoder.Decode(detail))using(var held=decoder.Decode(torn))Same(before,held);
            Check(decoder.LastRecovery.InvalidRows==84 && decoder.HoldingImage,"Torn trailing rows were displayed");
            decoder.Reset();using(var first=decoder.Decode(torn))Check(Center(first).ToArgb()==Color.Black.ToArgb(),"Initial damaged frame did not stay black");
            Check(decoder.RecoverySummary!=null,"Held frame was not reported");
            decoder.Reset();Check(decoder.RecoverySummary==null&&!decoder.HoldingImage,"Recovery state leaked across recordings");
            foreach(int at in new[]{42*492+12,42*492+13,42*492+14}) {
                byte[] damaged=(byte[])detail.Clone();damaged[at]^=(byte)(at%492==12?128:at%492==13?8:1);
                decoder.Reset();using(var before=decoder.Decode(detail))using(var held=decoder.Decode(damaged))Same(before,held);
                Check(decoder.LastRecovery.InvalidRows==1,"Not every pixel guard was checked");
            }
            int writes=0;
            using(var live=new CameraPreviewForm((r,g)=>writes++)) {
                typeof(CameraPreviewForm).GetField("autoExposure",Instance).SetValue(live,false);
                var decode=typeof(CameraPreviewForm).GetMethod("DecodeCe64Frame",Instance);
                decoder.Reset();
                foreach(var frame in new[]{grey,highlighted,bright,colorFrame,Shift(colorFrame,row=>1),torn,gap})
                    using(var expected=decoder.Decode(frame))using(var actual=(Bitmap)decode.Invoke(live,new object[]{frame}))Same(expected,actual);
            }
            Check(writes==0,"Offscreen test sent an exposure command");
            if (args.Length > 1) {
                string rawPath=Path.Combine(args[0],"encoder.raw"), videoPath=Path.Combine(args[0],"encoder.mp4");
                var white=Frame((x,y)=>1023);var invalidWhite=(byte[])white.Clone();invalidWhite[42*492+12]^=128;
                byte[] native=grey.Concat(highlighted).Concat(bright).Concat(white).Concat(Shift(white,row=>1)).Concat(invalidWhite).Concat(gap).ToArray();
                File.WriteAllBytes(rawPath,native);
                Environment.SetEnvironmentVariable("CE32_FFMPEG",args[1]);
                Environment.SetEnvironmentVariable("CE32_FFMPEG_PROFILE","minimal");
                var states=new System.Collections.Generic.List<string>();
                WILD_VideoDecoder.DecodeHevcWithAudio(rawPath,16,200000,false,false,null,videoPath,null,(index,recovery)=>{
                    Check(index==states.Count,"Recovery report changed frame indices");states.Add(recovery.Status);
                });
                Check(string.Join(",",states)=="valid,valid,valid,valid,realigned,invalid-held,gap-held","MP4 recovery report disagrees with source frames");
                using(var process=new Process {StartInfo=new ProcessStartInfo(args[1],"-hide_banner -loglevel error -i \""+videoPath+"\" -f rawvideo -pix_fmt rgb24 -vsync 0 pipe:1") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}})
                using(var decoded=new MemoryStream()) {
                    process.Start();process.StandardOutput.BaseStream.CopyTo(decoded);string error=process.StandardError.ReadToEnd();
                    Check(process.WaitForExit(10000)&&process.ExitCode==0,"MP4 decode failed: "+error);
                    byte[] pixels=decoded.ToArray();int stride=320*320*3,center=(160*320+160)*3;
                    Check(pixels.Length==stride*7,"MP4 frame count changed");
                    Check(Math.Abs(pixels[center]-pixels[stride+center])<=4&&pixels[center]>=140,"MP4 highlight darkening");
                    Check(pixels[stride*2+center+1]>=235&&Math.Abs(pixels[stride*2+center]-pixels[stride*2+center+1])<=8,"MP4 purple flash");
                    for(int i=3;i<7;i++)Check(pixels[stride*i+center]>=245,"MP4 saturated/realigned/held frame became black");
                }
                Check(File.ReadAllBytes(rawPath).SequenceEqual(native),"Encoding modified native camera data");
            }
            File.WriteAllText(Path.Combine(args[0],"result.txt"),"PASS "+checks+" actual-source camera checks; all1024 exposure codes, color/tone, bit/row realignment, ambiguous/damaged holds, frame audit and identical USB/playback pixels. No hardware.");
            Console.WriteLine(File.ReadAllText(Path.Combine(args[0],"result.txt")));return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
