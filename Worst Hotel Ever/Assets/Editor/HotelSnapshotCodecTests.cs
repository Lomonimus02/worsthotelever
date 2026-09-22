using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace WorstHotel
{
    public static class HotelSnapshotCodecTests
    {
        public static List<string> RunAll()
        {
            var results = new List<string>();
            var state = HotelSimulation.CreateNewMvp(8171).State;
            string source = JsonUtility.ToJson(state);
            byte[] packet = HotelSnapshotCodec.Encode(source);
            Require(HotelSnapshotCodec.Decode(packet) == source, "Unicode snapshot changed");
            Require(packet.Length < Encoding.UTF8.GetByteCount(source) / 2, "Compression not effective for actual MVP state");
            HotelSaveStore.Validate(JsonUtility.FromJson<HotelState>(HotelSnapshotCodec.Decode(packet)));
            results.Add("Protocol-4 complete MVP snapshot UTF-8/Deflate roundtrip and compression budget");
            for(int i=0;i<180;i++)state.ledger.Add(new string('漢',512));
            HotelSaveStore.Validate(state);
            source=JsonUtility.ToJson(state);
            Require(Encoding.UTF8.GetByteCount(source)>HotelMvpValidation.MaxSnapshotBytes,"CJK fixture misses the former decoder boundary");
            Require(HotelSnapshotCodec.Decode(HotelSnapshotCodec.Encode(source))==source,"Accepted CJK snapshot rejected or altered by decoder");
            results.Add("Valid CJK snapshot larger in UTF-8 than UTF-16 roundtrips within bounded expansion");
            Reject(() => HotelSnapshotCodec.Encode(new string('x', HotelMvpValidation.MaxSnapshotBytes)));
            Reject(() => HotelSnapshotCodec.Decode(null));
            Reject(() => HotelSnapshotCodec.Decode(new byte[0]));
            Reject(() => HotelSnapshotCodec.Decode(new byte[HotelMvpValidation.MaxSnapshotBytes]));
            results.Add("Snapshot encoder/decoder reject missing and oversized data before parsing");
            byte[] bomb;
            using (var output = new MemoryStream())
            {
                using (var zip = new DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
                {
                    byte[] block = new byte[HotelSnapshotCodec.MaxExpandedUtf8Bytes + 1];
                    zip.Write(block, 0, block.Length);
                }
                bomb = output.ToArray();
            }
            Reject(() => HotelSnapshotCodec.Decode(bomb));
            results.Add("Decompression has a hard expanded-size limit");
            return results;
        }
        static void Require(bool value, string text) { if (!value) throw new Exception(text); }
        static void Reject(Action action)
        {
            try { action(); } catch (InvalidDataException) { return; }
            throw new Exception("Invalid snapshot was accepted");
        }
    }
}
