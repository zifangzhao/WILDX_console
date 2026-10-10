using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using USB512_interface;

internal static class TestNpPreviewTheme
{
    static int checks;
    static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    static double Linear(byte value){double x=value/255.0;return x<=0.04045?x/12.92:Math.Pow((x+0.055)/1.055,2.4);}
    static double Luminance(Color color){return 0.2126*Linear(color.R)+0.7152*Linear(color.G)+0.0722*Linear(color.B);}
    static double Contrast(Color a,Color b){double x=Luminance(a),y=Luminance(b);return (Math.Max(x,y)+0.05)/(Math.Min(x,y)+0.05);}
    static void Run(string output)
    {
        var palettes=NpPreviewPalette.CreateAll();
        try {
            Check(Contrast(NpPreviewTheme.Text.Color,NpPreviewTheme.Canvas)>=7,"Primary labels are low contrast");
            Check(Contrast(NpPreviewTheme.Secondary.Color,NpPreviewTheme.Canvas)>=7,"Map captions are low contrast");
            Check(Contrast(NpPreviewTheme.Grid,NpPreviewTheme.Canvas)<2,"Grid overwhelms thin waveforms");
            var samples=new short[8*64];var original=(short[])samples.Clone();
            var sweep=new NpSweepBuffer(8);for(int row=0;row<8;row++)sweep.AddRow(samples,row*64,row);
            var page=NpPreviewDefinition.Hardware.Pages[0];
            foreach(var palette in palettes) {
                double lowest=Double.MaxValue;
                using(var trace=new Bitmap(520,2560))using(var map=new Bitmap(136,10000)) {
                    using(var g=Graphics.FromImage(trace))NeuropixelsPreviewForm.DrawSweepBank(g,sweep,0,1,0,512,null,trace.Width,trace.Height,null,page,palette);
                    using(var g=Graphics.FromImage(map))NeuropixelsPreviewForm.DrawElectrodeMap(g,NpPreviewDefinition.Hardware,page,palette,map.Width,map.Height);
                    Check(trace.GetPixel(510,1).ToArgb()==NpPreviewTheme.Canvas.ToArgb(),"Plot canvas mismatch");
                    Check(map.GetPixel(130,1).ToArgb()==NpPreviewTheme.Canvas.ToArgb(),"Map canvas mismatch");
                    for(int row=0;row<64;row++) {
                        var color=palette.Pens[row].Color;
                        lowest=Math.Min(lowest,Contrast(color,NpPreviewTheme.Canvas));
                        Check(Contrast(color,NpPreviewTheme.Canvas)>=4.5,"Low-contrast "+palette.Name+" row "+row);
                        Check(color.ToArgb()==palette.Brushes[row].Color.ToArgb(),"Map/trace colour mismatch");
                        Check(trace.GetPixel(350,row*40+20).ToArgb()==color.ToArgb(),"Rendered trace colour mismatch "+row);
                        float cellW=(map.Width-16)/16f,cellH=(map.Height-115)/24f;
                        Check(map.GetPixel((int)(8+(row%16)*cellW+2),(int)(65+(row/16)*cellH+2)).ToArgb()==color.ToArgb(),"Rendered map colour mismatch "+row);
                    }
                    Console.WriteLine(palette.Name+" minimum contrast: "+lowest.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                }
                // Readable overview at normal display size, all 64 rows.
                using(var trace=new Bitmap(1050,900)) {
                    var signal=new short[256*64];
                    for(int row=0;row<256;row++)for(int ch=0;ch<64;ch++)signal[row*64+ch]=(short)(90*Math.Sin(row*0.06+ch*0.1)+((row+ch*3)%67==0?160:0));
                    var display=new NpSweepBuffer(256);for(int row=0;row<256;row++)display.AddRow(signal,row*64,row);
                    using(var g=Graphics.FromImage(trace))NeuropixelsPreviewForm.DrawSweepBank(g,display,0,1,0,512,null,trace.Width,trace.Height,null,page,palette);
                    trace.Save(Path.Combine(output,"preview-theme-"+palette.Name.ToLowerInvariant()+".png"),ImageFormat.Png);
                }
            }
            for(int i=0;i<samples.Length;i++)Check(samples[i]==original[i],"Theme rendering mutated input samples");
        } finally {foreach(var palette in palettes)palette.Dispose();}
    }
    [STAThread] static int Main(string[] args)
    {
        try{Directory.CreateDirectory(args[0]);Run(args[0]);Console.WriteLine("PASS: "+checks+" palette contrast/all64-row map-trace pixels/canvas/labels/input-invariance checks");return 0;}
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
