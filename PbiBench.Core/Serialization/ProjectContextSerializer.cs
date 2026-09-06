using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using PbiBench.Core.Project;

namespace PbiBench.Core.Serialization
{
    public static class ProjectContextSerializer
    {
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(PbipProjectContext));

        public static string Serialize(PbipProjectContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.ContractVersion != PbipProjectContext.CurrentContractVersion)
                throw new InvalidOperationException($"Unsupported project context version {context.ContractVersion}.");

            using (var stream = new MemoryStream())
            {
                Serializer.WriteObject(stream, context);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static PbipProjectContext Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("JSON is required.", nameof(json));

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                var context = Serializer.ReadObject(stream) as PbipProjectContext
                    ?? throw new InvalidDataException("Project context JSON could not be read.");

                if (context.ContractVersion != PbipProjectContext.CurrentContractVersion)
                    throw new InvalidDataException($"Unsupported project context version {context.ContractVersion}.");

                return context;
            }
        }

        public static void WriteFile(string path, PbipProjectContext context)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Destination path is required.", nameof(path));

            var destination = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Could not resolve destination directory.");
            Directory.CreateDirectory(directory);

            var json = Serialize(context);
            var temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, json, new UTF8Encoding(false));

            try
            {
                if (File.Exists(destination))
                    File.Replace(temp, destination, null);
                else
                    File.Move(temp, destination);
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
