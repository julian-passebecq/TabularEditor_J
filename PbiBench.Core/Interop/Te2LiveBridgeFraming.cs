using System;
using System.IO;
using System.Text;

namespace PbiBench.Core.Interop
{
    /// <summary>One 4-byte big-endian length and strict UTF-8 JSON; bound before allocation.</summary>
    public static class Te2LiveBridgeFraming
    {
        public const int MaxFrameBytes = 1024 * 1024;
        public static string Read(Stream stream)
        {
            var header = new byte[4]; ReadAll(stream, header);
            var count = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
            if (count == 0 || count > MaxFrameBytes) throw new InvalidDataException("FRAME_BUDGET");
            var bytes = new byte[(int)count]; ReadAll(stream, bytes);
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        public static void Write(Stream stream, string json)
        {
            var bytes = new UTF8Encoding(false, true).GetBytes(json);
            if (bytes.Length == 0 || bytes.Length > MaxFrameBytes) throw new InvalidDataException("FRAME_BUDGET");
            var n = bytes.Length;
            stream.Write(new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n }, 0, 4);
            stream.Write(bytes, 0, bytes.Length); stream.Flush();
        }
        private static void ReadAll(Stream stream, byte[] bytes)
        {
            var offset = 0;
            while (offset < bytes.Length)
            { var count = stream.Read(bytes, offset, bytes.Length - offset); if (count <= 0) throw new EndOfStreamException("INCOMPLETE_FRAME"); offset += count; }
        }
    }
}
