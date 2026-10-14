using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace USB512_interface
{
    internal static class NpFolderNaming
    {
        internal static string Label(string value)
        {
            string label=(value??"").Trim();
            if(label.Length==0)return "";
            if(label.Length>80 || label.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || label.EndsWith(".") || label=="." || label=="..")
                throw new ArgumentException("Session label must be a folder name of up to 80 characters, without path separators or a trailing dot.");
            return label;
        }
        internal static string Name(string label,DateTime? start,string fallback)
        {
            label=Label(label);
            return label.Length==0?fallback:label+"_wild_"+(start.HasValue?
                start.Value.ToString("yyyyMMdd_HHmmss.fff",CultureInfo.InvariantCulture):"time_unknown_"+fallback);
        }
        // A sidecar claim coordinates simultaneous exports without precreating
        // their output folder (the strict exporters require a fresh path).
        internal sealed class Destination : IDisposable
        {
            internal readonly string Path;
            readonly FileStream claim;
            internal Destination(string path,FileStream claim){Path=path;this.claim=claim;}
            internal void Create(){if(!CreateDirectory(Path,IntPtr.Zero))throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not create a fresh recording folder.");}
            public void Dispose(){claim.Dispose();}
        }
        internal static Destination Reserve(string root,string name)
        {
            Directory.CreateDirectory(root);
            for(int n=1;;n++) {
                string path=Path.Combine(root,name+(n==1?"":"_"+n.ToString(CultureInfo.InvariantCulture)));
                if(Directory.Exists(path)||File.Exists(path))continue;
                FileStream claim;
                try {claim=new FileStream(path+".wildx-reservation",FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose);}
                catch(IOException ex){int code=ex.HResult&0xffff;if(code==32||code==80||code==183)continue;throw;}
                catch(UnauthorizedAccessException){if(Directory.Exists(path)||File.Exists(path+".wildx-reservation"))continue;throw;}
                if(Directory.Exists(path)||File.Exists(path)){claim.Dispose();continue;}
                return new Destination(path,claim);
            }
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        static extern bool CreateDirectory(string path,IntPtr security);
        internal static string CreateFresh(string root,string name)
        {
            using(var destination=Reserve(root,name)) {
                destination.Create();
                return destination.Path;
            }
        }
    }
}
