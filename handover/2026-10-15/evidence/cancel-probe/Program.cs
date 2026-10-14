using System;
public static class Program {
  public static int Main(string[] a) {
    // a: server db methods(csv) cancelAfterMs [query] [keep]
    var server = a[0]; var db = a[1]; var methods = a[2].Split(','); var after = int.Parse(a[3]);
    var q = a.Length > 4 && a[4] != "-" ? a[4] : "EVALUATE ROW(\"n\", COUNTROWS(FILTER(CROSSJOIN(SELECTCOLUMNS(GENERATESERIES(1,6000),\"a\",[Value]),SELECTCOLUMNS(GENERATESERIES(1,6000),\"b\",[Value])), MOD([a]*[b],7)=3)))";
    if (methods[0] == "drop") { CancelProbe.DropDb(server, db); return 0; }
    if (methods[0] == "create") { CancelProbe.CreateDb(server, db); return 0; }
    CancelProbe.CreateDb(server, db);
    try { foreach (var m in methods) CancelProbe.Run(server, db, q, m, after); }
    finally { CancelProbe.DropDb(server, db); }
    return 0;
  }
}

