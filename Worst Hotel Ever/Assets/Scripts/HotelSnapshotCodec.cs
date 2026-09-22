using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace WorstHotel
{
    // Protocol-4 world payloads: bounded UTF-8 + Deflate. Commands remain tiny plain JSON.
    // Decode bounds both the compressed packet and the expanded stream before parsing JSON.
    public static class HotelSnapshotCodec
    {
        // The model budget is UTF-16 plus framing. A BMP code unit can require
        // three UTF-8 bytes; valid CJK snapshots must not fail only on the receiver.
        public const int MaxExpandedUtf8Bytes = (HotelMvpValidation.MaxSnapshotBytes - 16) / 2 * 3;
        public static byte[] Encode(string json)
        {
            if (json == null || Encoding.Unicode.GetByteCount(json) + 16 > HotelMvpValidation.MaxSnapshotBytes)
                throw new InvalidDataException("Снимок отеля превышает сетевой лимит.");
            byte[] source = Encoding.UTF8.GetBytes(json);
            using (var output = new MemoryStream())
            {
                using (var compressed = new DeflateStream(output, CompressionLevel.Fastest, true)) compressed.Write(source, 0, source.Length);
                byte[] result = output.ToArray();
                if (result.Length + 16 > HotelMvpValidation.MaxSnapshotBytes) throw new InvalidDataException("Слишком большой сетевой пакет.");
                return result;
            }
        }

        public static string Decode(byte[] packet)
        {
            if (packet == null || packet.Length == 0 || packet.Length + 16 > HotelMvpValidation.MaxSnapshotBytes)
                throw new InvalidDataException("Некорректный размер сетевого пакета.");
            using (var source = new MemoryStream(packet, false))
            using (var compressed = new DeflateStream(source, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[4096];
                int length;
                while ((length = compressed.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + length > MaxExpandedUtf8Bytes) throw new InvalidDataException("Распакованный снимок слишком велик.");
                    output.Write(buffer, 0, length);
                }
                string json = new UTF8Encoding(false, true).GetString(output.ToArray());
                if (json.Length == 0 || Encoding.Unicode.GetByteCount(json) + 16 > HotelMvpValidation.MaxSnapshotBytes)
                    throw new InvalidDataException("Некорректный распакованный снимок.");
                return json;
            }
        }
    }
}
