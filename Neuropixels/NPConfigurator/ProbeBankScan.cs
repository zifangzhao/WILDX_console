using System.Text.Json;

namespace NPConfigurator;

// A scan changes electrode routing, never the fixed acquisition-channel count.
public static class ProbeBankScan
{
    public sealed record Window(int Index,int Shank,int Bank,int FirstSite,int LastSite,int NewSites,int OverlapSites);
    public static Window[] Windows(ProbeConfiguration template)
    {
        template.RequireValid();
        if(template.Profile is not (ProbeProfile.Np1 or ProbeProfile.Np2Single or ProbeProfile.Np2Multi))
            throw new NotSupportedException("Exploration requires a Neuropixels probe configuration.");
        var result=new List<Window>();
        for(int shank=0;shank<template.Shanks;shank++){
            var covered=new HashSet<int>();
            for(int bank=0;bank*384<template.SitesPerShank;bank++){
                int first=Math.Min(bank*384,template.SitesPerShank-384),added=0,overlap=0;
                for(int site=first;site<first+384;site++){
                    if(template.Profile==ProbeProfile.Np1 && site is 191 or 575 or 959)continue;
                    if(covered.Add(site))added++;else overlap++;
                }
                result.Add(new Window(result.Count,shank,bank,first,first+383,added,overlap));
            }
            int expected=template.SitesPerShank-(template.Profile==ProbeProfile.Np1?3:0);
            if(covered.Count!=expected)throw new InvalidDataException("Scan does not cover all usable sites.");
        }
        return result.ToArray();
    }
    public static ProbeConfiguration Select(ProbeConfiguration template,Window window,int connector)
    {
        var c=template.Clone();c.Connector=connector;c.OriginalImro=null;c.HardwareState="NotApplied";
        if(ProbeConfiguration.IsNp2(c.Profile)&&c.ModelRevision.Length==0)c.ModelRevision="2013";
        c.SelectRange(window.Shank,window.FirstSite,384);c.RequireValid();return c;
    }
    public static void Write(string input,string output,int connector)
    {
        if(connector is <1 or >3)throw new ArgumentOutOfRangeException(nameof(connector));
        var template=StandardFormats.Load(input);
        if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new IOException("Scan output must be new or empty.");
        Directory.CreateDirectory(output);var windows=Windows(template);var steps=new List<object>();
        foreach(var window in windows){
            var c=Select(template,window,connector);
            // Use the same device compiler as ordinary recording, so unsupported
            // models/routing fail during plan preparation, before any START.
            byte[] selection=ProbeConfiguration.IsNp2(c.Profile)?Np2DeviceSelection.Compile(c):DeviceSelection.Compile(c);
            string stem="step"+(window.Index+1).ToString("D4"),json=stem+".npconfig.json",imro=stem+".imro",binary=stem+".selection.bin";
            c.Save(Path.Combine(output,json));File.WriteAllText(Path.Combine(output,imro),StandardFormats.ToImro(c));File.WriteAllBytes(Path.Combine(output,binary),selection);
            steps.Add(new {index=window.Index,shank=window.Shank,bank=window.Bank,firstSite=window.FirstSite,lastSite=window.LastSite,newSites=window.NewSites,overlapSites=window.OverlapSites,configuration=json,imro,selection=binary,configurationId=c.Id});
        }
        ProbeConfiguration.AtomicWrite(Path.Combine(output,"plan.json"),JsonSerializer.Serialize(new{schemaVersion=1,numbering="zero-based",connector,profile=template.Profile.ToString(),serial=template.SerialNumber,templateId=template.Id,sitesPerShank=template.SitesPerShank,shanks=template.Shanks,steps},ProbeConfiguration.JsonOptions));
    }
}
