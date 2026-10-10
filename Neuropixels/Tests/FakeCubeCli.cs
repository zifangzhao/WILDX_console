using System;
using System.Threading;
internal static class FakeCubeCli
{
    static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="-l"){Thread.Sleep(60000);return 0;}
        if(args.Length>0 && args[0]=="fail"){Console.Error.WriteLine("Error: simulated failure");return 7;}
        if(args.Length>0 && args[0]=="output"){
            for(int i=0;i<2000;i++){Console.WriteLine("stdout "+i);Console.Error.WriteLine("stderr "+i);}
            Console.WriteLine("FINAL OUTPUT COMPLETE");return 0;
        }
        foreach(string arg in args)Console.WriteLine(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(arg)));
        return 0;
    }
}
