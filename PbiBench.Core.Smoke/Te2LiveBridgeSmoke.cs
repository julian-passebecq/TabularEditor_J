using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PbiBench.Core.Interop;

internal static class Te2LiveBridgeSmoke
{
    internal static void Run()
    {
        var request = new Dictionary<string, string> { ["schema"] = Te2LiveBridgeV1.Schema, ["requestId"] = Guid.NewGuid().ToString("N"), ["instance"] = Guid.NewGuid().ToString("N"), ["session"] = Guid.NewGuid().ToString("N"), ["action"] = "read" };
        var good = Te2LiveBridgeV1.Json(request);
        var wrongSchema = new Dictionary<string, string>(request) { ["schema"] = "unsupported" };
        Check(Te2LiveBridgeV1.ParseRequest(good)["action"] == "read", "strict valid read");
        foreach (var bad in new[] { "[]", "null", "{}", good.Replace("\"read\"", "true"), good.Replace("\"read\"", "null"), good.Replace("\"read\"", "[]"), good.Replace("\"read\"", "\"execute_csharp\""), Te2LiveBridgeV1.Json(wrongSchema), good.TrimEnd('}') + ",\"rows\":\"yes\"}", good.TrimEnd('}') + ",\"action\":\"read\"}", good.Replace(request["instance"], "unknown") })
            Reject(() => Te2LiveBridgeV1.ParseRequest(bad));
        request["action"] = "propose"; request["modelId"] = Guid.NewGuid().ToString("N"); request["revision"] = "r1"; request["selection"] = "s1"; request["table"] = "T"; request["measure"] = "M";
        request["changes"] = Te2LiveBridgeV1.Json(new Dictionary<string, string> { ["description"] = "line1\nline2", ["expression"] = "SUM('T'[Amount])", ["folder"] = "Metrics" });
        Check(Te2LiveBridgeV1.Changes(Te2LiveBridgeV1.ParseRequest(Te2LiveBridgeV1.Json(request))).Count == 3, "three typed changes");
        foreach (var bad in new[] { "{}", "{\"arbitraryCsharp\":\"x\"}", "{\"description\":null}", "{\"folder\":\"x\",\"folder\":\"y\"}", Te2LiveBridgeV1.Json(new Dictionary<string, string> { ["expression"] = new string('x', Te2LiveBridgeV1.MaxValueChars + 1) }) })
        { request["changes"] = bad; Reject(() => Te2LiveBridgeV1.ParseRequest(Te2LiveBridgeV1.Json(request))); }
        using (var framed = new MemoryStream())
        { Te2LiveBridgeFraming.Write(framed, good); framed.Position = 0; Check(Te2LiveBridgeFraming.Read(framed) == good, "framed UTF8 round trip"); }
        foreach (var bytes in new[] { Array.Empty<byte>(), new byte[] { 0, 0, 0 }, new byte[] { 0, 0, 0, 5, 1 }, new byte[] { 127, 255, 255, 255 }, new byte[] { 0, 0, 0, 1, 255 } })
            Reject(() => Te2LiveBridgeFraming.Read(new MemoryStream(bytes)));
        Console.WriteLine("TE2 live Core: strict fields/types/duplicates/version/identity/allowlist/budgets and truncated/invalid UTF8 framing PASS");
    }
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); }
    private static void Reject(Action action) { try { action(); } catch (Exception error) when (error is InvalidDataException || error is IOException || error is System.Xml.XmlException || error is System.Runtime.Serialization.SerializationException || error is ArgumentException) { return; } throw new Exception("Expected strict refusal."); }
}
