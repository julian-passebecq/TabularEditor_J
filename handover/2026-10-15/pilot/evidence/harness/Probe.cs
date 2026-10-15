using System;using System.IO;using System.Reflection;using System.Security.Cryptography;using System.Text.RegularExpressions;
static class P{
 static string Bin; static bool busy;
 static string Sha(string f){using(var s=SHA256.Create())using(var i=File.OpenRead(f))return BitConverter.ToString(s.ComputeHash(i)).Replace("-","").ToLower();}
 [STAThread] static int Main(string[] a){
  Bin=a[0].TrimEnd(Path.DirectorySeparatorChar);
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{ if(busy)return null; busy=true; try{
    var n=new AssemblyName(e.Name).Name; foreach(var x in new[]{".dll",".exe"}){var f=Path.Combine(Bin,n+x); if(File.Exists(f))return Assembly.LoadFrom(f);} return null;}finally{busy=false;}};
  var smoke=Assembly.LoadFrom(Path.Combine(Bin,"PbiBench.Host.Smoke.exe"));
  var code=(int)smoke.EntryPoint.Invoke(null,null);
  Console.WriteLine("smoke Main returned "+code);
  foreach(var asm in AppDomain.CurrentDomain.GetAssemblies()){var nm=asm.GetName();
    if(!Regex.IsMatch(nm.Name,"^(TabularEditor|PbiBench|TOMWrapper|Newtonsoft|Antlr4|Microsoft[.]AnalysisServices)"))continue;
    string loc=null; try{loc=asm.Location;}catch{}
    if(string.IsNullOrEmpty(loc))Console.WriteLine(nm.Name+" "+nm.Version+" (in-memory, costura-embedded)");
    else Console.WriteLine(nm.Name+" "+nm.Version+" "+(loc.StartsWith(Bin)?loc.Substring(Bin.Length+1):"OUTSIDE:"+loc)+" "+Sha(loc));}
  return code;}
}
