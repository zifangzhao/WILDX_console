using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace USB512_interface {
    public sealed class NpMetadataWriteException : IOException {
        public NpMetadataWriteException(string message,Exception error):base(message,error){}
    }
    // Recording sidecars only. Never use this writer for DAT or device files.
    public static class NpMetadataFile {
        static readonly object sync=new object();
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
        [return:MarshalAs(UnmanagedType.Bool)]
        static extern bool MoveFileExW(string source,string destination,uint flags);

        public static void WriteText(string path,string text) {
            byte[] bytes=new UTF8Encoding(false).GetBytes(text);
            Write(path,stream=>stream.Write(bytes,0,bytes.Length));
        }
        public static void Write(string path,Action<Stream> write) {
            // Boundary-only writes are serialized; each failed complete candidate
            // remains beside its destination for diagnosis/recovery.
            lock(sync) {
                path=Path.GetFullPath(path);
                string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                bool complete=false;
                try {
                    using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.Read)) {
                        write(stream);stream.Flush(true);complete=true;
                    }
                    int error=0;
                    for(int attempt=0;attempt<11;attempt++) {
                        // Same-directory rename replaces the name without the
                        // ReplaceFile metadata-merging/removal transaction.
                        if(MoveFileExW(temporary,path,1U|8U))return;
                        error=Marshal.GetLastWin32Error();
                        if(attempt==10 || (error!=5 && error!=32 && error!=33 && error!=1175))break;
                        Thread.Sleep(100);
                    }
                    throw new Win32Exception(error);
                } catch(Exception error) {
                    if(!complete)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}
                    throw new NpMetadataWriteException("Could not save metadata '"+path+"': "+error.Message+
                        (error is Win32Exception?" (Win32 "+((Win32Exception)error).NativeErrorCode+")":"")+
                        (complete?". Completed replacement retained at '"+temporary+"'.":""),error);
                }
            }
        }
    }
}
