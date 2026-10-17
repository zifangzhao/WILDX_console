using System;
using System.IO;
using System.Threading.Tasks;
using CE32_console;
using USB512_interface;

static class NpV2BleCatalogTests
{
    static int checks;
    static void Need(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static void Put(byte[] body,int at,uint value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,body,at,4);}
    static uint U(byte[] body,int at){return BitConverter.ToUInt32(body,at);}
    static byte[] Seal(byte[] body){Put(body,60,NpSdControl.Crc(body,60));return body;}
    static void Reject(Action call,string reason){try{call();}catch(Exception ex) when(ex is IOException || ex is TimeoutException){checks++;return;}throw new Exception(reason);}
    sealed class Device
    {
        internal uint Count=3,Token=0,Cid=0,Flags=NpV2BleCatalog.Capability|128U|17U,State=1,LastId=40,Fault,PendingFault;
        internal int Infos,Entries,Deletes,StatusReads,PendingPolls=1,TokenChangeAt,RemoveCount=1;
        internal bool Pending,LoseAck,ChangedEpoch,ChangeEpochOnRead,ChangeCidOnDelete,ChangePrefixOnDelete,DeleteOff,WrongAck;
        internal uint TailState=3,CookieShift;
        internal const uint Capacity=8388608;
        uint Next{get{return Count==0?8192:Count*32768;}}
        uint Start(uint ordinal){return ordinal==0?8192:ordinal*32768;}
        uint Cookie(uint ordinal){return (ordinal==2?0:ordinal*13)+CookieShift;}
        uint EntryFlags(uint ordinal){bool last=ordinal==Count-1;return 13U|((last?TailState:3U)<<8)|NpV2BleCatalog.MetadataValid|(last?NpV2BleCatalog.Last:0U)|(last && !DeleteOff && (TailState==3||TailState==4)?NpV2BleCatalog.DeleteAllowed:0U);}
        internal Task<NpSdStatus> Status()
        {
            StatusReads++;
            if(Pending && PendingPolls--<=0){Pending=false;Fault=PendingFault;
                if(Fault==0){Count-=(uint)RemoveCount;TailState=3;if(ChangeCidOnDelete)Cid++;if(ChangePrefixOnDelete)CookieShift++;}
            }
            return Task.FromResult(new NpSdStatus{State=State,Flags=Flags|(Pending?8U:0U),Mask=3,Hz=7500,Start=Next,Capacity=Capacity,LastId=LastId,Fault=Fault});
        }
        byte[] Header(uint op,uint id){var b=new byte[64];Put(b,0,NpV2SdControl.ReplyMagic);Put(b,4,2);Put(b,8,op);Put(b,12,id);return b;}
        internal byte[] Info(uint id)
        {
            var b=Header(9,id);uint[] fields={Token,Count,Next,Capacity,Cid,Flags&NpV2BleCatalog.CatalogFlags,Count==0?UInt32.MaxValue:Count-1,Count==0?0:Start(Count-1),Count==0?0:Next,Count==0?0:Cookie(Count-1)};
            for(int i=0;i<fields.Length;i++)Put(b,20+4*i,fields[i]);return Seal(b);
        }
        internal byte[] Entry(uint id,uint ordinal)
        {
            var b=Header(10,id);uint[] fields={Token,Count,ordinal,Start(ordinal),(ordinal+1)*32768,Cookie(ordinal),ordinal+1,7500,3,EntryFlags(ordinal)};
            for(int i=0;i<fields.Length;i++)Put(b,20+4*i,fields[i]);return Seal(b);
        }
        internal Task<byte[]> Exchange(byte[] request)
        {
            Need(U(request,0)==NpV2SdControl.RequestMagic && U(request,4)==2 && U(request,60)==NpSdControl.Crc(request,60),"Bad catalog request envelope");
            if(ChangeEpochOnRead)ChangedEpoch=true;
            uint op=U(request,8),id=U(request,12);
            if(op==9){Infos++;if(Infos==TokenChangeAt)Token++;return Task.FromResult(Info(id));}
            Need(op==10,"Read-only transport received a mutation");Entries++;
            Need(U(request,16)==Token && U(request,20)==Count,"ENTRY request lost snapshot/count");return Task.FromResult(Entry(id,U(request,24)));
        }
        internal Task<NpSdStatus> Send(byte[] request)
        {
            Deletes++;Need(U(request,8)==11 && U(request,16)==Token && U(request,20)==Count && U(request,24)==Count-1,"DELETE lost snapshot/count/tail");
            Need(U(request,28)==Start(Count-1) && U(request,32)==Next && U(request,36)==Cookie(Count-1) && U(request,40)==0x4c44324e,"DELETE lost exact extent/cookie/confirmation key");
            for(int i=44;i<60;i++)Need(request[i]==0,"DELETE reserved bytes nonzero");Need(U(request,60)==NpSdControl.Crc(request,60),"DELETE CRC invalid");
            LastId=U(request,12);Pending=true;if(LoseAck)throw new TimeoutException("Lost queued ACK");
            return Task.FromResult(new NpSdStatus{LastId=LastId+(WrongAck?1U:0U),Flags=Flags|8,State=State,Mask=3,Hz=7500,Start=Next,Capacity=Capacity});
        }
        internal NpV2BleCatalog Client(){return new NpV2BleCatalog(Status,Exchange,Send,()=>{if(ChangedEpoch)throw new IOException("Changed epoch");});}
    }
    static void Codec()
    {
        var d=new Device();byte[] request=NpV2BleCatalog.InfoRequest(71);
        Need(U(request,8)==9 && U(request,12)==71 && U(request,60)==NpSdControl.Crc(request,60),"INFO request ABI changed");for(int i=16;i<60;i++)Need(request[i]==0,"INFO reserved bytes set");
        var info=NpV2BleCatalog.ParseInfo(d.Info(71),71);Need(info.Token==0 && info.CidCrc==0 && info.LastCookie==0,"CRC-derived zero values rejected");
        request=NpV2BleCatalog.EntryRequest(72,info,2);Need(U(request,8)==10 && U(request,16)==0 && U(request,20)==3 && U(request,24)==2,"ENTRY request ABI changed");for(int i=28;i<60;i++)Need(request[i]==0,"ENTRY reserved bytes set");
        foreach(int at in new[]{0,4,8,12,60}){var bad=d.Info(71);bad[at]^=1;Reject(()=>NpV2BleCatalog.ParseInfo(bad,71),"Uncorrelated/corrupt INFO accepted");}
        foreach(var damage in new[]{Tuple.Create(24,64513U),Tuple.Create(28,8193U),Tuple.Create(32,8192U),Tuple.Create(40,1U),Tuple.Create(40,NpV2BleCatalog.CatalogRecoverable),Tuple.Create(44,1U),Tuple.Create(48,8193U),Tuple.Create(52,Device.Capacity+1)}){var bad=d.Info(71);Put(bad,damage.Item1,damage.Item2);Seal(bad);Reject(()=>NpV2BleCatalog.ParseInfo(bad,71),"Invalid INFO geometry/tail accepted");}
        foreach(var damage in new[]{Tuple.Create(20,1U),Tuple.Create(24,4U),Tuple.Create(28,1U),Tuple.Create(32,65537U),Tuple.Create(36,Device.Capacity+1),Tuple.Create(40,1U),Tuple.Create(44,0U),Tuple.Create(48,10000U),Tuple.Create(52,2U),Tuple.Create(56,0x100000U)}){var bad=d.Entry(72,2);Put(bad,damage.Item1,damage.Item2);Seal(bad);Reject(()=>NpV2BleCatalog.ParseEntry(bad,72,info,2,65536),"Invalid ENTRY correlation/metadata accepted");}
        foreach(uint error in new uint[]{2,8,9,10}){
            var bad=new byte[64];uint[] words={NpV2SdControl.ReplyMagic,2,9,71,error,1,error,3,7500,0,8192,0,Device.Capacity,NpV2BleCatalog.Capability|128U|17U,40};for(int i=0;i<words.Length;i++)Put(bad,4*i,words[i]);Seal(bad);
            Reject(()=>NpV2BleCatalog.ParseInfo(bad,71),"Error STATUS layout interpreted as INFO");
        }
        d.Count=0;info=NpV2BleCatalog.ParseInfo(d.Info(71),71);Need(info.Count==0 && info.LastOrdinal==UInt32.MaxValue && info.Next==8192,"Empty INFO rejected");
        foreach(int at in new[]{48,52,56}){var bad=d.Info(71);Put(bad,at,8192);Seal(bad);Reject(()=>NpV2BleCatalog.ParseInfo(bad,71),"Stale empty-catalog tail accepted");}
        d=new Device();info=NpV2BleCatalog.ParseInfo(d.Info(71),71);
        foreach(uint flags in new uint[]{13U|(1U<<8)|NpV2BleCatalog.Last|NpV2BleCatalog.MetadataValid|NpV2BleCatalog.DeleteAllowed,13U|(3U<<8)|NpV2BleCatalog.Last|NpV2BleCatalog.DeleteAllowed,13U|(3U<<8)|NpV2BleCatalog.MetadataValid|NpV2BleCatalog.DeleteAllowed}){var bad=d.Entry(72,2);Put(bad,56,flags);Seal(bad);Reject(()=>NpV2BleCatalog.ParseEntry(bad,72,info,2,65536),"Invalid last/metadata/state delete eligibility accepted");}
        var media=d.Entry(72,2);Put(media,48,0);Put(media,52,0);Put(media,56,8U|(3U<<8)|NpV2BleCatalog.Last|NpV2BleCatalog.MetadataValid|NpV2BleCatalog.DeleteAllowed);Seal(media);Need(NpV2BleCatalog.ParseEntry(media,72,info,2,65536).Mask==0,"Typed media summary rejected");
    }
    static void Flows()
    {
        foreach(uint count in new uint[]{0,1,3,63,64}){
            var d=new Device{Count=count};var client=d.Client();var catalog=client.Read().GetAwaiter().GetResult();Need(catalog.Records.Count==count && d.Infos==2 && d.Entries==count,"V2 list lost an entry/page boundary");
            if(count==0){Need(!catalog.CanDeleteLast,"Empty catalog deletable");continue;}
            uint oldFlags=catalog.Records[0].Flags;var result=client.Delete(catalog).GetAwaiter().GetResult();Need(result.Records.Count==count-1 && d.Deletes==1 && !d.Pending,"Queued ACK mistaken for completion or delete replayed");Need(catalog.Records[0].Flags==oldFlags,"Confirmation snapshot mutated during verification");
        }
        foreach(uint state in new uint[]{0,2,3,4,6,255}){var d=new Device{State=state};Reject(()=>d.Client().Read().GetAwaiter().GetResult(),"Active/unmounted INFO admitted");Need(d.Infos+d.Entries+d.Deletes==0,"Refused state sent catalog traffic");}
        var unsupported=new Device{Flags=128|17};Reject(()=>unsupported.Client().Read().GetAwaiter().GetResult(),"Unadvertised catalog commands sent");Need(unsupported.Infos==0,"Capability refusal sent INFO");
        var changed=new Device{TokenChangeAt=2};Reject(()=>changed.Client().Read().GetAwaiter().GetResult(),"Changed listing snapshot accepted");Need(changed.Deletes==0,"Read failure mutated catalog");
        changed=new Device{ChangeEpochOnRead=true};Reject(()=>changed.Client().Read().GetAwaiter().GetResult(),"Changed connection accepted");Need(changed.Entries+changed.Deletes==0,"Changed-epoch listing continued");
        foreach(uint tailState in new uint[]{1,4}){var d=new Device{TailState=tailState};var client=d.Client();var snapshot=client.Read().GetAwaiter().GetResult();if(tailState==1){Need(!snapshot.CanDeleteLast,"Reserved last entry deletable");Reject(()=>client.Delete(snapshot).GetAwaiter().GetResult(),"Reserved tail deleted");Need(d.Deletes==0,"Ineligible delete sent");}else{Need(snapshot.CanDeleteLast,"Validated FAILED tail not deletable");client.Delete(snapshot).GetAwaiter().GetResult();Need(d.Deletes==1,"FAILED tail delete did not execute once");}}
        var stale=new Device();var staleClient=stale.Client();var confirmed=staleClient.Read().GetAwaiter().GetResult();stale.Token++;Reject(()=>staleClient.Delete(confirmed).GetAwaiter().GetResult(),"Stale confirmation deleted");Need(stale.Deletes==0,"Stale confirmation reached mutation transport");
        foreach(string failure in new[]{"ack","wrong_ack","stale","io","count","cid","prefix","timeout"}){
            var d=new Device();var client=d.Client();var snapshot=client.Read().GetAwaiter().GetResult();
            if(failure=="ack")d.LoseAck=true;if(failure=="wrong_ack")d.WrongAck=true;if(failure=="stale")d.PendingFault=9;if(failure=="io")d.PendingFault=10;if(failure=="count")d.RemoveCount=2;if(failure=="cid")d.ChangeCidOnDelete=true;if(failure=="prefix")d.ChangePrefixOnDelete=true;if(failure=="timeout")d.PendingPolls=Int32.MaxValue;
            Reject(()=>client.Delete(snapshot).GetAwaiter().GetResult(),"Unconfirmed/mismatched delete reported success: "+failure);Need(d.Deletes==1,"Uncertain delete replayed: "+failure);
        }
        var full=new Device{Flags=NpV2BleCatalog.Capability|128|17|65536};var fullClient=full.Client();var fullCatalog=fullClient.Read().GetAwaiter().GetResult();fullClient.Delete(fullCatalog).GetAwaiter().GetResult();Need(full.Deletes==1,"FULL alone prevented deleting the last entry");
        var recovery=new Device{Flags=NpV2BleCatalog.Capability|128|17|NpV2BleCatalog.CatalogRecovery|NpV2BleCatalog.CatalogRecoverable,DeleteOff=true};var recoveryClient=recovery.Client();var recoveryCatalog=recoveryClient.Read().GetAwaiter().GetResult();Need(!recoveryCatalog.CanDeleteLast,"Recovery catalog last entry deletable");Reject(()=>recoveryClient.Delete(recoveryCatalog).GetAwaiter().GetResult(),"Recovery catalog deleted");Need(recovery.Deletes==0,"Recovery attempted a delete");
    }
    static int Main(){try{Codec();Flows();Console.WriteLine("PASS "+checks+" production-source V2 BLE catalog contract/queued-delete checks; simulated firmware only.");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
}
