using System;
using System.Collections.Generic;
using System.IO;
using CE32_console;

namespace USB512_interface
{
    // Presents an extent-only archive in its original card address space.
    // Seeks outside that extent are errors, not fabricated zero-filled data.
    internal sealed class NpArchive : Stream
    {
        private readonly FileStream source;
        internal readonly long StartLba, EndLba;
        internal NpArchive(string path)
        {
            var fields=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var line in File.ReadAllLines(path+".status.txt")) {
                int split=line.IndexOf('=');if(split>0)fields.Add(line.Substring(0,split),line.Substring(split+1));
            }
            string complete,passed,start,blocks;
            long count;
            if(!fields.TryGetValue("complete",out complete) || complete!="True" ||
               !fields.TryGetValue("transport_crc_sequence_epoch",out passed) || passed!="passed" ||
               !fields.TryGetValue("start_lba",out start) || !long.TryParse(start,out StartLba) || StartLba<0 ||
               !fields.TryGetValue("blocks",out blocks) || !long.TryParse(blocks,out count) || count<=0)
                throw new InvalidDataException("Archive is incomplete or lacks a valid transport completion report.");
            EndLba=checked(StartLba+count);
            source=File.OpenRead(path);
            if(source.Length!=checked(count*512)){source.Dispose();throw new InvalidDataException("Archive length does not match its completed extent.");}
        }
        public override bool CanRead{get{return true;}}
        public override bool CanSeek{get{return true;}}
        public override bool CanWrite{get{return false;}}
        public override long Length{get{return checked(EndLba*512);}}
        public override long Position{get{return checked(StartLba*512+source.Position);}set{Seek(value,SeekOrigin.Begin);}}
        public override long Seek(long offset,SeekOrigin origin)
        {
            long at=checked(offset+(origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?Position:origin==SeekOrigin.End?Length:throw new ArgumentOutOfRangeException("origin")));
            if(at<StartLba*512 || at>Length)throw new IOException("Seek outside downloaded NP extent.");
            source.Position=at-StartLba*512;return at;
        }
        public override int Read(byte[] b,int at,int n){return source.Read(b,at,n);}
        public override void Flush(){}
        public override void Write(byte[] b,int at,int n){throw new NotSupportedException();}
        public override void SetLength(long n){throw new NotSupportedException();}
        protected override void Dispose(bool disposing){if(disposing)source.Dispose();base.Dispose(disposing);}
    }
}
