/*
 * Parts of this code has been copied from the DAX Studio source code:
 * https://github.com/DaxStudio/DaxStudio
 * 
 * Please review their license here: 
 * https://github.com/DaxStudio/DaxStudio/blob/master/license.rtf
 */
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using TabularEditor.UIServices;

namespace TabularEditor.Dax
{
    public class DaxFormatterError
    {
        public int line;
        public int column;
        public string message;
    }

    public class ServerDatabaseInfo
    {
        public string ServerName { get; set; } // SHA-256 hash of server name
        public string ServerEdition { get; set; } // # Values: null, "Enterprise64", "Developer64", "Standard64"
        public string ServerType { get; set; } // Values: null, "SSAS", "PBI Desktop", "SSDT Workspace", "Tabular Editor"
        public string ServerMode { get; set; } // Values: null, "SharePoint", "Tabular"
        public string ServerLocation { get; set; } // Values: null, "OnPremise", "Azure"
        public string ServerVersion { get; set; } // Example: "14.0.800.192"
        public string DatabaseName { get; set; } // SHA-256 hash of database name
        public string DatabaseCompatibilityLevel { get; set; } // Values: 1200, 1400
    }

    public class DaxFormatterRequestSingle : DaxFormatterRequest
    {
        public string Dax { get; set; }

        public DaxFormatterRequestSingle(bool useSemicolonsAsSeparators, bool shortFormat, bool skipSpaceAfterFunctionName) : base(useSemicolonsAsSeparators, shortFormat, skipSpaceAfterFunctionName)
        {

        }
    }

    public class DaxFormatterRequestMulti : DaxFormatterRequest
    {
        public List<string> Dax { get; set; }

        public DaxFormatterRequestMulti(bool useSemicolonsAsSeparators, bool shortFormat, bool skipSpaceAfterFunctionName) : base(useSemicolonsAsSeparators, shortFormat, skipSpaceAfterFunctionName)
        {

        }
    }

    public class DaxFormatterRequest
    {
        public int? MaxLineLenght { get; set; }
        public bool? SkipSpaceAfterFunctionName { get; set; }
        public char ListSeparator { get; set; }
        public char DecimalSeparator { get; set; }
        public string CallerApp { get; set; }
        public string CallerVersion { get; set; }

        public DaxFormatterRequest()
        {
            this.ListSeparator = ',';
            this.DecimalSeparator = '.';

            // Save caller app and version
            var assemblyName = System.Reflection.Assembly.GetEntryAssembly().GetName();
            this.CallerApp = assemblyName.Name;
            this.CallerVersion = assemblyName.Version.ToString();

            // S001 never collects model telemetry.
        }

        public DaxFormatterRequest(bool useSemicolonsAsSeparators, bool shortFormat, bool skipSpaceAfterFunctionName)
        {
            this.ListSeparator = useSemicolonsAsSeparators ? ';' : ',';
            this.DecimalSeparator = useSemicolonsAsSeparators ? ',' : '.';
            this.MaxLineLenght = shortFormat ? 1 : 0;
            this.SkipSpaceAfterFunctionName = skipSpaceAfterFunctionName;

            // Save caller app and version
            var assemblyName = System.Reflection.Assembly.GetEntryAssembly()?.GetName();
            this.CallerApp = assemblyName?.Name;
            this.CallerVersion = assemblyName?.Version.ToString();

            // S001 never collects model telemetry.
        }

    }

    public class DaxFormatterResult
    {
        [JsonProperty(PropertyName = "formatted")]
        public string FormattedDax;
        public List<DaxFormatterError> errors;
    }

    public class DaxFormatterProxy : IDaxFormatterProxy
    {
        public static DaxFormatterProxy Instance = new DaxFormatterProxy();
        private DaxFormatterProxy()
        {
            // force the use of TLS 1.2
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        }

        public const string DaxTextFormatUri = "https://www.daxformatter.com/api/daxformatter/daxtextformat";
        public const string DaxTextFormatMultiUri = "https://www.daxformatter.com/api/daxformatter/daxtextformatmulti";

        internal IDaxFormatterTransport Transport { get; set; } = new WebDaxFormatterTransport();
        public DaxFormatterResult FormatDax(string query, bool useSemicolonsAsSeparators, bool shortFormat, bool skipSpaceAfterFunctionName)
        {
            string output = CallDaxFormatterSingle(DaxTextFormatUri, query, useSemicolonsAsSeparators, shortFormat, skipSpaceAfterFunctionName);
            var res2 = new DaxFormatterResult();
            if (output.StartsWith("{"))
            {
                JsonConvert.PopulateObject(output, res2);
            } else
            {
                res2.FormattedDax = JsonConvert.DeserializeObject<string>(output);
            }
            return res2;
        }

        public List<DaxFormatterResult> FormatDaxMulti(List<string> dax, bool useSemicolonsAsSeparators, bool shortFormat, bool skipSpaceAfterFunctionName)
        {
            string output = CallDaxFormatterMulti(DaxTextFormatMultiUri, dax, useSemicolonsAsSeparators, shortFormat, skipSpaceAfterFunctionName);

            List<DaxFormatterResult> res;
            if (output.StartsWith("["))
            {
                res = JsonConvert.DeserializeObject<List<DaxFormatterResult>>(output);
            }
            else
            {
                res = new List<DaxFormatterResult>();
            }
            return res;
        }

        private string CallDaxFormatterSingle(string uri, string dax, bool separators, bool shortFormat, bool skipSpace)
        {
            PbiBench.Dax.RemoteFormatterConsent.RequireEnabled();
            var req = new DaxFormatterRequestSingle(separators, shortFormat, skipSpace) { Dax = dax };
            return Send(uri, req);
        }

        private string CallDaxFormatterMulti(string uri, List<string> dax, bool separators, bool shortFormat, bool skipSpace)
        {
            PbiBench.Dax.RemoteFormatterConsent.RequireEnabled();
            var req = new DaxFormatterRequestMulti(separators, shortFormat, skipSpace) { Dax = dax };
            return Send(uri, req);
        }

        private string Send(string uri, DaxFormatterRequest request)
        {
            var consent = PbiBench.Dax.RemoteFormatterConsent.Revision;
            PbiBench.Dax.RemoteFormatterConsent.RequireEnabled();
            var data = JsonConvert.SerializeObject(request, Formatting.Indented);
            var transport = Transport;
            var timeout = Preferences.Current.DaxFormatterRequestTimeout;
            var destination = transport.Prime(uri, timeout);
            PbiBench.Dax.RemoteFormatterConsent.RequireEnabled();
            if (consent != PbiBench.Dax.RemoteFormatterConsent.Revision)
                throw new InvalidOperationException("Remote formatter consent changed. Retry explicitly.");
            return transport.Post(destination, data, timeout);
        }
    }

    internal interface IDaxFormatterTransport
    {
        string Prime(string uri, int timeout);
        string Post(string uri, string body, int timeout);
    }

    internal sealed class WebDaxFormatterTransport : IDaxFormatterTransport
    {
        public string Prime(string uri, int timeout)
        {
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Proxy = ProxyCache.GetProxy(uri);
            request.Timeout = request.ReadWriteTimeout = timeout;
            request.AllowAutoRedirect = false;
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                var location = response.Headers["Location"];
                if (string.IsNullOrEmpty(location)) return uri;
                var redirect = new Uri(new Uri(uri), location);
                if (redirect.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("Formatter requires HTTPS.");
                return new UriBuilder(new Uri(uri)) { Host = redirect.Host }.ToString();
            }
        }
        public string Post(string uri, string body, int timeout)
        {
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Proxy = ProxyCache.GetProxy(uri);
            request.Timeout = request.ReadWriteTimeout = timeout;
            request.AllowAutoRedirect = false;
            request.Method = "POST";
            request.Accept = "application/json";
            request.ContentType = "application/json; charset=UTF-8";
            request.AutomaticDecompression = DecompressionMethods.GZip;
            var bytes = System.Text.Encoding.UTF8.GetBytes(body);
            using (var stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            using (var response = request.GetResponse())
            using (var reader = new System.IO.StreamReader(response.GetResponseStream()))
                return reader.ReadToEnd().Trim();
        }
    }
}