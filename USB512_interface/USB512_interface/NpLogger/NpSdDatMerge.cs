using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using USB512_interface;

namespace CE32_console {
    // File-only SD export option. Originals survive cancellation or any failed
    // merge; they are removed only after every output and its metadata is saved.
    internal static class NpSdDatMerge {
        const int RowBytes=384*2,BatchRows=1024;
        sealed class Range {
            internal long[] Offsets,Rows;
            internal ulong? Start,End;
        }
        sealed class Band {
            internal string Name;internal string[] Inputs;internal List<Range> Ranges;
        }
        static long Number(string value){return long.Parse(value,CultureInfo.InvariantCulture);}
        static void CheckCancel(Func<bool> cancelled){if(cancelled!=null && cancelled())throw new OperationCanceledException("DAT merge cancelled; per-probe files retained.");}
        static void Exact(Stream input,byte[] bytes,int count){int at=0,n;while(at<count){n=input.Read(bytes,at,count-at);if(n==0)throw new EndOfStreamException("Truncated per-probe DAT during merge.");at+=n;}}

        static List<Range> Ranges(string folder,bool v2,int[] ports,string[] paths) {
            var totals=paths.Select(path=>{long size=new FileInfo(path).Length;if(size%RowBytes!=0)throw new InvalidDataException("Incomplete DAT row: "+Path.GetFileName(path));return size/RowBytes;}).ToArray();
            if(!v2)return new List<Range>{new Range{Offsets=new long[ports.Length],Rows=totals}};
            string report=File.ReadAllText(Path.Combine(folder,"np2_sd_export_status.txt"));
            var headers=Regex.Matches(report,@"^Segment (\d+): first_stored_block=\d+; stored_blocks=\d+; source_start_tick=(\d+); source_end_tick=(\d+)\r?$",RegexOptions.Multiline);
            if(headers.Count==0) {
                if(Regex.IsMatch(report,@"Source gap count: [1-9]") || report.Contains("Best-effort source loss"))throw new InvalidDataException("Source-gap segment offsets are required before merging this export.");
                return new List<Range>{new Range{Offsets=new long[ports.Length],Rows=totals}};
            }
            var ranges=new List<Range>();var next=new long[ports.Length];ulong lastEnd=0;
            foreach(Match header in headers) {
                int index=ranges.Count;
                var range=new Range{Offsets=new long[ports.Length],Rows=new long[ports.Length],Start=ulong.Parse(header.Groups[2].Value,CultureInfo.InvariantCulture),End=ulong.Parse(header.Groups[3].Value,CultureInfo.InvariantCulture)};
                if(Number(header.Groups[1].Value)!=index || range.End<range.Start || (index>0 && range.Start<lastEnd))throw new InvalidDataException("Invalid SD source segment order.");
                for(int p=0;p<ports.Length;p++) {
                    var matches=Regex.Matches(report,@"^Segment "+index+" probe "+ports[p]+@": row_offset=(\d+); rows=(\d+); first_source_tick=(\d+)\r?$",RegexOptions.Multiline);
                    if(matches.Count!=1)throw new InvalidDataException("Missing or duplicate per-probe SD segment receipt.");
                    range.Offsets[p]=Number(matches[0].Groups[1].Value);range.Rows[p]=Number(matches[0].Groups[2].Value);
                    ulong first=ulong.Parse(matches[0].Groups[3].Value,CultureInfo.InvariantCulture);
                    long expected=range.End<=first?0:checked((long)((range.End.Value-first)/1600));
                    if(first<range.Start || first-range.Start>=1600 || expected!=range.Rows[p] || range.Offsets[p]!=next[p])throw new InvalidDataException("Inconsistent SD segment row geometry.");
                    next[p]=checked(next[p]+range.Rows[p]);
                }
                lastEnd=range.End.Value;ranges.Add(range);
            }
            if(!next.SequenceEqual(totals))throw new InvalidDataException("SD segment receipts do not cover every DAT row.");
            return ranges;
        }

        internal static void Export(string folder,bool v2,int mask,int apHz,Func<bool> cancelled,Action<string> progress=null) {
            ExportTimed(folder,v2,mask,apHz,cancelled,progress==null?(Action<int,string>)null:(percent,message)=>progress(message));
        }
        internal static void ExportTimed(string folder,bool v2,int mask,int apHz,Func<bool> cancelled,Action<int,string> progress) {
            if(mask<1 || mask>7 || apHz<=0)throw new ArgumentOutOfRangeException("mask");
            var ports=Enumerable.Range(1,3).Where(p=>(mask&(1<<(p-1)))!=0).ToArray();
            var bands=new List<Band>();
            foreach(string name in v2?new[]{"ap"}:new[]{"ap","lfp"}) {
                var inputs=ports.Select(p=>Path.Combine(folder,v2?"neuropixels_probe"+p+"_ap.dat":"np"+p+"_"+name+".dat")).ToArray();
                bands.Add(new Band{Name=name,Inputs=inputs,Ranges=Ranges(folder,v2,ports,inputs)});
            }
            long totalBytes=bands.Sum(b=>b.Inputs.Sum(p=>new FileInfo(p).Length)),doneBytes=0;string phase="Merging DAT";
            var estimate=new NpProgressEstimate();var update=System.Diagnostics.Stopwatch.StartNew();
            Action<bool> report=force=>{if(progress!=null&&(force||update.ElapsedMilliseconds>=250)){update.Restart();int percent=totalBytes<=0?0:(int)Math.Min(99,doneBytes*100.0/totalBytes);progress(percent,phase+": "+percent+"% · "+estimate.Describe(doneBytes,totalBytes));}};
            report(true);
            var publications=new List<Tuple<string,string>>();var receipts=new List<object>();
            var serializer=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
            string receipt=Path.Combine(folder,"dat_merge.json");
            if(File.Exists(receipt))throw new IOException("DAT merge receipt already exists.");
            foreach(var band in bands) {
                phase="Merging "+band.Name.ToUpperInvariant()+" DAT";
                string target=Path.Combine(folder,"neuropixels_"+band.Name+".dat");
                if(File.Exists(target))throw new IOException("Merged DAT already exists: "+target);
                publications.Add(Tuple.Create(target+".partial",target));
                var inputs=new List<FileStream>();var tails=new FileStream[ports.Length];var tailRows=new long[ports.Length];
                var blocks=ports.Select(p=>new byte[BatchRows*RowBytes]).ToArray();var combined=new byte[BatchRows*RowBytes*ports.Length];
                var segments=new List<object>();long outputRows=0;
                try {
                    foreach(string input in band.Inputs)inputs.Add(new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024));
                    using(var output=new FileStream(target+".partial",FileMode.CreateNew,FileAccess.Write,FileShare.Read,1024*1024)) {
                        foreach(var range in band.Ranges) {
                            CheckCancel(cancelled);long common=range.Rows.Min();var tailOffsets=(long[])tailRows.Clone();
                            report(true);
                            for(int p=0;p<ports.Length;p++)inputs[p].Position=checked(range.Offsets[p]*RowBytes);
                            for(long row=0;row<common;) {
                                CheckCancel(cancelled);int count=(int)Math.Min(BatchRows,common-row);
                                for(int p=0;p<ports.Length;p++)Exact(inputs[p],blocks[p],count*RowBytes);
                                for(int r=0;r<count;r++)for(int p=0;p<ports.Length;p++)Buffer.BlockCopy(blocks[p],r*RowBytes,combined,(r*ports.Length+p)*RowBytes,RowBytes);
                                output.Write(combined,0,count*RowBytes*ports.Length);row+=count;doneBytes=checked(doneBytes+(long)count*RowBytes*ports.Length);report(false);
                            }
                            for(int p=0;p<ports.Length;p++) {
                                long extra=range.Rows[p]-common;
                                if(extra>0 && tails[p]==null) {
                                    string directory=Path.Combine(folder,"unpaired_tail");Directory.CreateDirectory(directory);
                                    string tail=Path.Combine(directory,"probe"+ports[p]+"_"+band.Name+".dat");
                                    if(File.Exists(tail))throw new IOException("Unpaired DAT already exists.");
                                    tails[p]=new FileStream(tail+".partial",FileMode.CreateNew,FileAccess.Write,FileShare.Read,1024*1024);
                                    publications.Add(Tuple.Create(tail+".partial",tail));
                                }
                                while(extra>0) {CheckCancel(cancelled);int count=(int)Math.Min(BatchRows,extra);Exact(inputs[p],blocks[p],count*RowBytes);tails[p].Write(blocks[p],0,count*RowBytes);tailRows[p]+=count;extra-=count;doneBytes=checked(doneBytes+(long)count*RowBytes);report(false);}
                            }
                            segments.Add(new{merged_row_offset=outputRows,merged_rows=common,source_start_tick=range.Start,source_end_tick=range.End,
                                input_row_offsets=range.Offsets,input_rows=range.Rows,unpaired_row_offsets=tailOffsets,unpaired_rows=range.Rows.Select(n=>n-common).ToArray()});
                            outputRows=checked(outputRows+common);
                        }
                        output.Flush(true);
                        if(output.Length!=checked(outputRows*ports.Length*RowBytes))throw new IOException("Merged DAT length mismatch.");
                    }
                    for(int p=0;p<ports.Length;p++)if(tails[p]!=null){tails[p].Flush(true);if(tails[p].Length!=checked(tailRows[p]*RowBytes))throw new IOException("Unpaired DAT length mismatch.");}
                } finally {foreach(var input in inputs)input.Dispose();foreach(var tail in tails)if(tail!=null)tail.Dispose();}
                receipts.Add(new{band=band.Name,file=Path.GetFileName(target),rows=outputRows,channels=384*ports.Length,
                    input_files=band.Inputs.Select(Path.GetFileName).ToArray(),segments=segments,unpaired_rows=tailRows});
            }
            CheckCancel(cancelled);
            if(progress!=null)progress(99,"Saving merged DAT metadata · "+estimate.Percent(100));
            foreach(var item in publications)File.Move(item.Item1,item.Item2);
            NpMetadataFile.WriteText(receipt,serializer.Serialize(new{schema_version=1,physical_probe_order=ports,channels_per_probe=384,
                sample_format="little-endian int16",channel_order="[S0 probe1 channels1-384][S0 probe2 channels1-384]…; active physical ports ascending",
                alignment="Ordinal complete rows within each source segment. Original per-probe phase offsets remain; merging is not proof of simultaneous ADC sampling. No new padding or gap bridging.",
                unmatched_rows="Retained in unpaired_tail, indexed by the segment receipts. Original SD validation/status reports remain source evidence.",bands=receipts})+Environment.NewLine);
            USB512_interface.NpRecordingSettings.MergedSd(folder,mask,apHz,v2);
            USB512_interface.NpTimeCorrection.AfterMerge(folder);
            foreach(var band in bands)foreach(string input in band.Inputs){File.Delete(input);string xml=Path.ChangeExtension(input,".xml");if(File.Exists(xml))File.Delete(xml);}
            USB512_interface.NpRecordingSettings.RefreshBundle(folder);
            if(progress!=null)progress(100,"DAT merge complete · "+estimate.Elapsed);
        }
    }
}
