using System;
using System.Collections.Generic;
using USB512_interface;

static class TestNp2ConfigurationPreferences
{
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    static string Untrusted(string json, int port)
    {
        if (json == "bad") throw new InvalidOperationException();
        return json.Replace("\"HardwareState\":\"Applied\"", "\"HardwareState\":\"NotApplied\"");
    }
    static int Main()
    {
        try
        {
            var device = new Np2ConfigurationPreferences.Device();
            device.Requests.Add(1, "{\"Profile\":\"Np2Multi\",\"HardwareState\":\"Applied\"}");
            device.Requests.Add(2, "{\"Profile\":\"Np2Multi\"}"); device.Apply.Add(1);
            var stored = new Dictionary<string, Np2ConfigurationPreferences.Device> { { "001A0004434250062039314E", device } };
            string wire = Np2ConfigurationPreferences.Save(stored);
            var restored = Np2ConfigurationPreferences.Load(wire, Untrusted);
            Np2ConfigurationPreferences.Device loaded;
            Check(restored.TryGetValue("001A0004434250062039314E", out loaded) && loaded.Requests.Count == 2 && loaded.Apply.SetEquals(new[] { 1 }), "NP2 intent roundtrip changed ports or apply choice");
            Check(loaded.Requests[1].Contains("NotApplied") && !loaded.Requests[1].Contains("\"HardwareState\":\"Applied\""), "Persisted NP2 map restored trusted applied state");
            Check(Np2ConfigurationPreferences.Load("{", Untrusted).Count == 0, "Corrupt NP2 preference payload was accepted");
            Check(Np2ConfigurationPreferences.Load("{\"version\":1,\"devices\":{\"uid\":{\"requests\":{\"1\":\"bad\"},\"apply\":[1]}}}", Untrusted).Count == 0, "Invalid retained NP2 request was retained");
            Console.WriteLine("PASS " + checks + " NP2 preference roundtrip/corruption checks"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
