using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Microsoft.AnalysisServices;
using Tom = Microsoft.AnalysisServices.Tabular;

// Pilot tool for the "desktop" lane. Every UI command is scoped to the top-level windows of ONE process id.
public static class Program
{
    static readonly string[][] Queries = {
        new[]{"Q1","EVALUATE ROW(\"Total\", [Total Amount])"},
        new[]{"Q2","EVALUATE SUMMARIZECOLUMNS(Sales[Region], \"Total\", [Total Amount]) ORDER BY Sales[Region]"},
        new[]{"Q3","EVALUATE ROW(\"Rows\", COUNTROWS(Sales), \"Avg\", AVERAGE(Sales[Amount]), \"Max\", MAX(Sales[Amount]))"},
        new[]{"Q4","EVALUATE ROW(\"Share North\", DIVIDE(CALCULATE([Total Amount], Sales[Region] = \"North\"), [Total Amount]))"},
        new[]{"Q5","EVALUATE ADDCOLUMNS(VALUES(Sales[Region]), \"Rank\", RANKX(ALL(Sales[Region]), [Total Amount])) ORDER BY Sales[Region]"},
        new[]{"Q6","EVALUATE ROW(\"Blank\", CALCULATE([Total Amount], Sales[Region] = \"West\"), \"Div0\", DIVIDE(1, 0), \"Txt\", CONCATENATEX(Sales, Sales[Region], \",\", Sales[Amount], ASC))"},
    };

    static int Main(string[] a)
    {
        try
        {
            switch (a[0])
            {
                case "dbs": return Dbs(a[1]);
                case "query": return Query(a[1], a[2], a[3]);
                case "engine": return Engine(a[1], a[2], a[3], a[4], a[5], a[6]);
                case "measure": return MeasureCheck(a[1], a[2], a[3]);
                case "calc": return Calc(a[1], a[2]);
                case "tree": return Tree(int.Parse(a[1]), int.Parse(a[2]), a.Length > 3 ? a[3] : null);
                case "find": return Find(int.Parse(a[1]), a[2]);
                case "invoke": return InvokeByName(int.Parse(a[1]), a[2], a.Length > 3 ? a[3] : null);
                case "wait": return WaitName(int.Parse(a[1]), a[2], int.Parse(a[3]));
                case "keys": return Keys(int.Parse(a[1]), a[2]);
                case "focuskeys": { var el = All(int.Parse(a[1])).First(x => Safe(() => x.AutomationId) == a[2]); el.Focus(); Thread.Sleep(300); int rc = 0; foreach (var k in a.Skip(3)) { rc = Keys(int.Parse(a[1]), k); Thread.Sleep(250); if (rc != 0) break; } return rc; }
                case "type": return TypeText(int.Parse(a[1]), a[2]);
                case "shot": return Shot(int.Parse(a[1]), a[2], a.Length > 3 ? a[3] : null);
                case "windows": return Windows(int.Parse(a[1]));
                case "close": return CloseMain(int.Parse(a[1]));
                default: Console.Error.WriteLine("unknown command"); return 2;
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("ERROR " + ex.GetType().Name + ": " + ex.Message); return 1; }
    }

    // ---------------- engine side ----------------
    static Tom.Server Connect(string server) { var s = new Tom.Server(); s.Connect("Data Source=" + server); return s; }

    static int Dbs(string server)
    {
        using (var s = Connect(server))
            foreach (Tom.Database d in s.Databases) Console.WriteLine(d.Name + "\t" + d.ID + "\t" + d.CompatibilityLevel);
        return 0;
    }

    static object Val(object v)
    {
        if (v == null || v is DBNull) return null;
        if (v is decimal || v is double || v is float || v is long || v is int || v is short || v is bool || v is string) return v;
        if (v is DateTime dt) return dt.ToString("o");
        return v.ToString();
    }

    static List<Dictionary<string, object>> RunQueries(string server, string db)
    {
        var results = new List<Dictionary<string, object>>();
        var q = new Server(); q.Connect("Data Source=" + server + ";Initial Catalog=" + db);
        try
        {
            foreach (var qq in Queries)
            {
                var r = new Dictionary<string, object> { ["id"] = qq[0], ["query"] = qq[1], ["columns"] = new List<string>(), ["rows"] = new List<object>(), ["error"] = null };
                try
                {
                    XmlaResultCollection res;
                    using (var reader = q.ExecuteReader("<Statement>" + SecurityElement.Escape(qq[1]) + "</Statement>", out res,
                        new Dictionary<string, string> { { "Catalog", db }, { "Format", "Tabular" } }))
                    {
                        if (res != null) foreach (XmlaResult x in res) foreach (XmlaMessage m in x.Messages) if (m is XmlaError) r["error"] = m.Description;
                        if (reader != null)
                        {
                            var cols = (List<string>)r["columns"];
                            for (int i = 0; i < reader.FieldCount; i++) cols.Add(reader.GetName(i));
                            while (reader.Read())
                            {
                                var row = new List<object>();
                                for (int i = 0; i < reader.FieldCount; i++) row.Add(Val(reader.GetValue(i)));
                                ((List<object>)r["rows"]).Add(row);
                            }
                        }
                    }
                }
                catch (Exception ex) { r["error"] = ex.GetType().Name + ": " + ex.Message.Replace("\r", " ").Replace("\n", " "); }
                results.Add(r);
            }
        }
        finally { q.Disconnect(); q.Dispose(); }
        return results;
    }

    static int Query(string server, string db, string outFile)
    {
        var res = RunQueries(server, db);
        var json = new JavaScriptSerializer().Serialize(res);
        File.WriteAllText(outFile, json, new UTF8Encoding(false));
        Console.WriteLine(json);
        return res.Any(r => r["error"] != null) ? 3 : 0;
    }

    static int Engine(string server, string db, string outFile, string desktopVersion, string msmdsrvVersion, string extraJson)
    {
        using (var s = Connect(server))
        {
            var d = s.Databases.GetByName(db);
            var info = new Dictionary<string, object>
            {
                ["desktopPackageVersion"] = desktopVersion,
                ["msmdsrvFileVersion"] = msmdsrvVersion,
                ["serverVersion"] = s.Version,
                ["serverEdition"] = s.Edition.ToString(),
                ["serverMode"] = s.ServerMode.ToString(),
                ["compatibilityLevel"] = d.CompatibilityLevel,
                ["compatibilityMode"] = d.CompatibilityMode.ToString(),
                ["databaseIsGuidNamed"] = Guid.TryParse(d.Name, out _),
            };
            // DBMSVersion via DISCOVER_PROPERTIES
            XmlaResultCollection r;
            using (var reader = s.ExecuteReader("<Statement>SELECT * FROM $SYSTEM.DISCOVER_PROPERTIES</Statement>", out r, new Dictionary<string, string> { { "Format", "Tabular" } }))
            {
                if (reader != null) while (reader.Read()) { var n = reader["PropertyName"]?.ToString(); if (n == "DBMSVersion" || n == "ProviderVersion" || n == "DBMSName") info[n] = reader["Value"]?.ToString(); }
            }
            if (!info.ContainsKey("DBMSVersion"))
            {
                info["DBMSVersion"] = "not returned by DISCOVER_PROPERTIES";
            }
            if (extraJson != "-") info["extra"] = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(extraJson));
            var json = new JavaScriptSerializer().Serialize(info);
            File.WriteAllText(outFile, json, new UTF8Encoding(false));
            Console.WriteLine(json);
        }
        return 0;
    }

    static int MeasureCheck(string server, string db, string name)
    {
        using (var s = Connect(server))
        {
            var d = s.Databases.GetByName(db);
            var t = d.Model.Tables.Find("Sales");
            var present = t != null && t.Measures.ContainsName(name);
            Console.WriteLine((present ? "PRESENT" : "ABSENT") + " measure=" + name + " salesTable=" + (t != null) + " measures=" + (t == null ? "" : string.Join("|", t.Measures.Select(m => m.Name))));
            return present ? 0 : 4;
        }
    }

    static int Calc(string server, string db)
    {
        using (var s = Connect(server))
        {
            var d = s.Databases.GetByName(db);
            d.Model.RequestRefresh(Tom.RefreshType.Calculate);
            d.Model.SaveChanges();
            Console.WriteLine("calculated " + db);
        }
        return 0;
    }

    // ---------------- UI side (scoped to one process id) ----------------
    static UIA3Automation Auto = new UIA3Automation();

    static List<Window> Wins(int pid)
    {
        var app = FlaUI.Core.Application.Attach(pid);
        return app.GetAllTopLevelWindows(Auto).ToList();
    }

    static IEnumerable<AutomationElement> All(int pid)
    {
        foreach (var w in Wins(pid))
        {
            yield return w;
            AutomationElement[] desc;
            try { desc = w.FindAllDescendants(); } catch { continue; }
            foreach (var e in desc) yield return e;
        }
    }

    static string Safe(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }

    static string Describe(AutomationElement e) =>
        Safe(() => e.ControlType.ToString()) + " name=[" + Safe(() => e.Name) + "] id=[" + Safe(() => e.AutomationId) + "] class=[" + Safe(() => e.ClassName) + "]";

    static int Windows(int pid)
    {
        foreach (var w in Wins(pid)) Console.WriteLine(Safe(() => w.Properties.NativeWindowHandle.Value.ToString()) + " " + Describe(w) + " modal=" + Safe(() => w.IsModal.ToString()));
        return 0;
    }

    static int Tree(int pid, int depth, string filter)
    {
        Regex rx = filter == null ? null : new Regex(filter, RegexOptions.IgnoreCase);
        foreach (var w in Wins(pid)) Walk(w, 0, depth, rx);
        return 0;
    }

    static void Walk(AutomationElement e, int lvl, int max, Regex rx)
    {
        var d = Describe(e);
        if (rx == null || rx.IsMatch(d)) Console.WriteLine(new string(' ', lvl * 2) + d);
        if (lvl >= max) return;
        AutomationElement[] kids;
        try { kids = e.FindAllChildren(); } catch { return; }
        foreach (var k in kids) Walk(k, lvl + 1, max, rx);
    }

    static int Find(int pid, string pattern)
    {
        var rx = new Regex(pattern, RegexOptions.IgnoreCase);
        int n = 0;
        foreach (var e in All(pid)) { var d = Describe(e); if (rx.IsMatch(d)) { Console.WriteLine(d); n++; } }
        Console.WriteLine("matches=" + n);
        return n > 0 ? 0 : 5;
    }

    static int WaitName(int pid, string pattern, int timeoutSec)
    {
        var rx = new Regex(pattern, RegexOptions.IgnoreCase);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            try
            {
                foreach (var e in All(pid)) { var d = Describe(e); if (rx.IsMatch(d)) { Console.WriteLine("found after " + sw.ElapsedMilliseconds + " ms: " + d); return 0; } }
            }
            catch { }
            Thread.Sleep(1000);
        }
        Console.WriteLine("timeout " + timeoutSec + " s: " + pattern);
        return 6;
    }

    static int InvokeByName(int pid, string pattern, string controlType)
    {
        var rx = new Regex(pattern, RegexOptions.IgnoreCase);
        foreach (var e in All(pid))
        {
            var name = Safe(() => e.Name);
            if (!rx.IsMatch(name)) continue;
            if (controlType != null && !string.Equals(Safe(() => e.ControlType.ToString()), controlType, StringComparison.OrdinalIgnoreCase)) continue;
            var p = e.Patterns;
            if (p.Invoke.IsSupported) { p.Invoke.Pattern.Invoke(); Console.WriteLine("invoked " + Describe(e)); return 0; }
            if (p.Toggle.IsSupported) { p.Toggle.Pattern.Toggle(); Console.WriteLine("toggled " + Describe(e)); return 0; }
            if (p.SelectionItem.IsSupported) { p.SelectionItem.Pattern.Select(); Console.WriteLine("selected " + Describe(e)); return 0; }
            if (p.ExpandCollapse.IsSupported) { p.ExpandCollapse.Pattern.Expand(); Console.WriteLine("expanded " + Describe(e)); return 0; }
            if (p.LegacyIAccessible.IsSupported) { p.LegacyIAccessible.Pattern.DoDefaultAction(); Console.WriteLine("defaultAction " + Describe(e)); return 0; }
        }
        Console.WriteLine("no invokable element for " + pattern);
        return 7;
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    static bool ForegroundMine(int pid, string titleRx)
    {
        var target = Wins(pid).FirstOrDefault(w => titleRx == null || Regex.IsMatch(Safe(() => w.Name), titleRx, RegexOptions.IgnoreCase));
        if (target == null) { Console.WriteLine("no window"); return false; }
        var h = target.Properties.NativeWindowHandle.Value;
        for (int i = 0; i < 5; i++)
        {
            try { target.Focus(); } catch { }
            SetForegroundWindow(h);
            Thread.Sleep(300);
            GetWindowThreadProcessId(GetForegroundWindow(), out var fp);
            if (fp == pid) return true;
        }
        Console.WriteLine("foreground is not pid " + pid);
        return false;
    }

    // keys: e.g. "ctrl+s", "alt+f4", "tab", "enter", "f5"
    static int Keys(int pid, string spec)
    {
        if (!ForegroundMine(pid, null)) return 8;
        var parts = spec.ToLowerInvariant().Split('+');
        var mods = new List<VirtualKeyShort>();
        VirtualKeyShort key = 0;
        foreach (var p in parts)
        {
            switch (p)
            {
                case "ctrl": mods.Add(VirtualKeyShort.CONTROL); break;
                case "alt": mods.Add(VirtualKeyShort.ALT); break;
                case "shift": mods.Add(VirtualKeyShort.SHIFT); break;
                case "enter": key = VirtualKeyShort.ENTER; break;
                case "tab": key = VirtualKeyShort.TAB; break;
                case "esc": key = VirtualKeyShort.ESCAPE; break;
                case "f4": key = VirtualKeyShort.F4; break;
                case "f5": key = VirtualKeyShort.F5; break;
                default: key = Enum.TryParse<VirtualKeyShort>(p.ToUpperInvariant(), out var vk) ? vk : (VirtualKeyShort)Enum.Parse(typeof(VirtualKeyShort), "KEY_" + p.ToUpperInvariant()); break;
            }
        }
        // re-check right before sending
        GetWindowThreadProcessId(GetForegroundWindow(), out var fp2);
        if (fp2 != pid) { Console.WriteLine("foreground changed; not sent"); return 8; }
        Keyboard.TypeSimultaneously(mods.Concat(new[] { key }).ToArray());
        Console.WriteLine("sent " + spec + " to pid " + pid);
        return 0;
    }

    static int TypeText(int pid, string text)
    {
        if (!ForegroundMine(pid, null)) return 8;
        Keyboard.Type(text);
        Console.WriteLine("typed " + text.Length + " chars to pid " + pid);
        return 0;
    }

    static int Shot(int pid, string file, string titleRx)
    {
        SetProcessDPIAware();
        var w = Wins(pid).Where(x => titleRx == null || Regex.IsMatch(Safe(() => x.Name), titleRx, RegexOptions.IgnoreCase))
            .OrderByDescending(x => { GetWindowRect(x.Properties.NativeWindowHandle.Value, out var rr); return (rr.R - rr.L) * (rr.B - rr.T); }).First();
        var h = w.Properties.NativeWindowHandle.Value;
        GetWindowRect(h, out var r);
        using (var bmp = new Bitmap(r.R - r.L, r.B - r.T))
        {
            using (var g = Graphics.FromImage(bmp)) { var hdc = g.GetHdc(); PrintWindow(h, hdc, 2); g.ReleaseHdc(hdc); }
            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine("shot " + Path.GetFileName(file) + " " + (r.R - r.L) + "x" + (r.B - r.T) + " title=[" + Safe(() => w.Name) + "]");
        return 0;
    }

    static int CloseMain(int pid)
    {
        var w = Wins(pid).First();
        w.Patterns.Window.Pattern.Close();
        Console.WriteLine("close requested via WindowPattern: " + Describe(w));
        return 0;
    }
}

