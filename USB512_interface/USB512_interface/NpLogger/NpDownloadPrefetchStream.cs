using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace CE32_console
{
    // One worker owns the existing transport; two bounded buffers overlap its
    // validated reads with the exporter's synchronous decode/output work.
    internal sealed class NpDownloadPrefetchStream : Stream
    {
        readonly Stream source;
        readonly Func<bool> cancelled;
        readonly long first,last,length;
        byte[] current,next;
        long position,currentStart,nextStart;
        int currentCount;
        Task<int> pending;
        bool disposed;
        ExceptionDispatchInfo failure;
        internal long SourceBytes,SourceReadTicks,ConsumerWaitTicks;

        internal NpDownloadPrefetchStream(Stream source,long first,long last,Func<bool> cancelled,int bufferBytes=1024*1024)
        {
            if(source==null||!source.CanRead||!source.CanSeek)throw new ArgumentException("A readable seekable download is required.");
            if(first<0||last<first||last>source.Length)throw new ArgumentOutOfRangeException("last");
            if(bufferBytes<512||bufferBytes>4*1024*1024)throw new ArgumentOutOfRangeException("bufferBytes");
            this.source=source;this.first=first;this.last=last;this.cancelled=cancelled;length=source.Length;position=first;
            current=new byte[bufferBytes];next=new byte[bufferBytes];
        }
        void Check()
        {
            if(disposed)throw new ObjectDisposedException(GetType().Name);
            if(failure!=null)failure.Throw();
            if(cancelled!=null&&cancelled())throw new OperationCanceledException();
        }
        void Start(long at)
        {
            nextStart=at;int count=(int)Math.Min(next.Length,last-at);byte[] buffer=next;
            pending=Task.Run(()=>{
                long started=Stopwatch.GetTimestamp();
                try{
                    Check();source.Position=at;int done=0;
                    while(done<count){Check();int n=source.Read(buffer,done,count-done);if(n==0)throw new EndOfStreamException("Truncated SD download.");done+=n;SourceBytes+=n;}
                    return done;
                }finally{SourceReadTicks+=Stopwatch.GetTimestamp()-started;}
            });
        }
        int Finish()
        {
            long started=Stopwatch.GetTimestamp();
            try{return pending.GetAwaiter().GetResult();}
            catch(Exception ex){failure=ExceptionDispatchInfo.Capture(ex);throw;}
            finally{pending=null;ConsumerWaitTicks+=Stopwatch.GetTimestamp()-started;}
        }
        void Fill()
        {
            if(pending!=null&&(position<nextStart||position>=nextStart+next.Length)){Finish();}
            if(pending==null)Start(position);
            long at=nextStart;int count=Finish();
            byte[] spare=current;current=next;next=spare;currentStart=at;currentCount=count;
            if(currentStart+currentCount<last)Start(currentStart+currentCount);
        }
        public override int Read(byte[] buffer,int offset,int count)
        {
            Check();if(buffer==null)throw new ArgumentNullException("buffer");
            if(offset<0||count<0||offset>buffer.Length-count)throw new ArgumentOutOfRangeException();
            int copied=0;
            while(count>0&&position<last){
                Check();if(currentCount==0||position<currentStart||position>=currentStart+currentCount)Fill();
                int at=(int)(position-currentStart),n=Math.Min(count,currentCount-at);
                Buffer.BlockCopy(current,at,buffer,offset,n);position+=n;offset+=n;count-=n;copied+=n;
            }
            return copied;
        }
        public override long Position{get{Check();return position;}set{Check();if(value<first||value>last)throw new ArgumentOutOfRangeException("value");position=value;}}
        public override long Length{get{Check();return length;}}
        public override long Seek(long offset,SeekOrigin origin)
        {
            if(origin!=SeekOrigin.Begin&&origin!=SeekOrigin.Current&&origin!=SeekOrigin.End)throw new ArgumentOutOfRangeException("origin");
            Position=checked(offset+(origin==SeekOrigin.Begin?0:origin==SeekOrigin.Current?position:length));return position;
        }
        public override bool CanRead{get{return !disposed;}}
        public override bool CanSeek{get{return !disposed;}}
        public override bool CanWrite{get{return false;}}
        public override void Flush(){}
        public override void Write(byte[] b,int o,int n){throw new NotSupportedException();}
        public override void SetLength(long value){throw new NotSupportedException();}
        protected override void Dispose(bool disposing)
        {
            if(disposed)return;
            try{if(pending!=null)Finish();}
            finally{disposed=true;current=next=null;base.Dispose(disposing);}
            // The caller keeps ownership of source, and can safely inspect or
            // reuse its USB handle only after the worker has joined here.
        }
    }
}
