using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using PbiBench.Core.Dax;
internal static class QaRework
{
    [STAThread] static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var random = new Random(14015);
            int count = 0;
            foreach (var locale in new[] { "en-US", "fr-CH" })
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(locale);
                for (int i = 0; i < 2048; i++)
                {
                    var bits = new byte[8]; random.NextBytes(bits);
                    Check(BitConverter.ToDouble(bits, 0));
                    Check(BitConverter.ToSingle(bits, 0)); count += 2;
                }
                for (long i = -8; i <= 8; i++)
                {
                    Check(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1d) + i));
                    Check(BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(1f), 0) + (int)i), 0)); count += 2;
                }
            }
            Console.WriteLine("QA T14: " + count + " seeded bit-pattern/adjacent CSV numerical round-trips PASS across en-US/fr-CH");
            Rework01Smoke.Run();
            Console.WriteLine("QA T15 extended: dirty Cancel-New/Cancel-Open/cancelled or failed save-before-New and one-way object/property/handler transitions PASS");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Check(object value)
    {
        using (var writer = new StringWriter())
        {
            DaxCsvExport.Write(writer, new DaxQueryResult { Columns = new[] { "value" }, Rows = new[] { new[] { value } } });
            var line = writer.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)[1];
            if (line[0] != '"' || line[line.Length - 1] != '"') throw new Exception("CSV quoting");
            var field = line.Substring(1, line.Length - 2);
            bool equal = value is double ? double.Parse(field, CultureInfo.InvariantCulture).Equals((double)value) : float.Parse(field, CultureInfo.InvariantCulture).Equals((float)value);
            if (!equal) throw new Exception("Independent typed numeric round-trip failed: " + field);
        }
    }
}
