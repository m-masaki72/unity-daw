using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UniDAW.Midi
{
    public struct MidiNote
    {
        public int track;
        public int channel;
        public int note;
        public int velocity;
        public long startTick;
        public long endTick;
    }

    public struct TempoChange
    {
        public long tick;
        public int microsecondsPerQuarter;
        public double Bpm => 60000000.0 / microsecondsPerQuarter;
    }

    public sealed class MidiFileData
    {
        public int format;
        public int division = 480;
        public readonly List<string> trackNames = new List<string>();
        public readonly List<MidiNote> notes = new List<MidiNote>();
        public readonly List<TempoChange> tempos = new List<TempoChange>();

        public double InitialBpm => tempos.Count > 0 ? tempos.OrderBy(t => t.tick).First().Bpm : 120.0;
        public long LastTick => notes.Count == 0 ? 0 : notes.Max(n => n.endTick);
    }

    /// <summary>Standard MIDI File (format 0/1) リーダー。ノート単位に解決して返す。</summary>
    public static class SmfReader
    {
        public static MidiFileData Read(byte[] bytes)
        {
            var d = new MidiFileData();
            int pos = 0;
            if (bytes.Length < 14 || Ascii(bytes, 0, 4) != "MThd") throw new InvalidDataException("MThd ヘッダがありません");
            int hlen = (int)ReadU32(bytes, 4);
            d.format = ReadU16(bytes, 8);
            int ntracks = ReadU16(bytes, 10);
            int div = ReadU16(bytes, 12);
            if ((div & 0x8000) != 0) throw new InvalidDataException("SMPTE タイムベースは未対応です");
            d.division = div;
            pos = 8 + hlen;

            for (int t = 0; t < ntracks && pos + 8 <= bytes.Length; t++)
            {
                string id = Ascii(bytes, pos, 4);
                int len = (int)ReadU32(bytes, pos + 4);
                pos += 8;
                int end = Math.Min(bytes.Length, pos + len);
                if (id == "MTrk") ReadTrack(bytes, pos, end, t, d);
                else d.trackNames.Add("");
                pos = end;
            }
            d.notes.Sort((a, b) => a.startTick != b.startTick ? a.startTick.CompareTo(b.startTick) : a.note.CompareTo(b.note));
            return d;
        }

        static void ReadTrack(byte[] b, int pos, int end, int trackIndex, MidiFileData d)
        {
            long tick = 0;
            int running = 0;
            string name = "";
            var open = new Dictionary<int, Queue<(long tick, int vel)>>();

            void CloseNote(int ch, int note, long at)
            {
                int key = ch * 128 + note;
                if (!open.TryGetValue(key, out var q) || q.Count == 0) return;
                var (st, vel) = q.Dequeue();
                d.notes.Add(new MidiNote
                {
                    track = trackIndex, channel = ch, note = note, velocity = vel, startTick = st, endTick = Math.Max(st, at),
                });
            }

            while (pos < end)
            {
                tick += ReadVlq(b, ref pos);
                if (pos >= end) break;
                int status = b[pos];
                if (status >= 0x80)
                {
                    pos++;
                    if (status < 0xF0) running = status;
                }
                else
                {
                    if (running == 0) throw new InvalidDataException("ランニングステータスが不正です");
                    status = running;
                }

                if (status == 0xFF)
                {
                    int type = b[pos++];
                    int len = (int)ReadVlq(b, ref pos);
                    if (type == 0x51 && len >= 3)
                    {
                        int us = (b[pos] << 16) | (b[pos + 1] << 8) | b[pos + 2];
                        if (us > 0) d.tempos.Add(new TempoChange { tick = tick, microsecondsPerQuarter = us });
                    }
                    else if (type == 0x03 && name.Length == 0)
                    {
                        name = DecodeText(b, pos, len);
                    }
                    pos += len;
                    if (type == 0x2F) break;
                    continue;
                }
                if (status == 0xF0 || status == 0xF7)
                {
                    int len = (int)ReadVlq(b, ref pos);
                    pos += len;
                    continue;
                }

                int kind = status & 0xF0;
                int chn = status & 0x0F;
                int d1 = b[pos++] & 0x7F;
                int d2 = 0;
                if (kind != 0xC0 && kind != 0xD0) d2 = b[pos++] & 0x7F;

                if (kind == 0x90 && d2 > 0)
                {
                    int key = chn * 128 + d1;
                    if (!open.TryGetValue(key, out var q)) open[key] = q = new Queue<(long, int)>();
                    q.Enqueue((tick, d2));
                }
                else if (kind == 0x80 || (kind == 0x90 && d2 == 0))
                {
                    CloseNote(chn, d1, tick);
                }
            }

            foreach (var kv in open)
                while (kv.Value.Count > 0) CloseNote(kv.Key / 128, kv.Key % 128, tick);
            d.trackNames.Add(name);
        }

        static string DecodeText(byte[] b, int pos, int len)
        {
            try
            {
                return Encoding.UTF8.GetString(b, pos, len).Trim('\0', ' ');
            }
            catch
            {
                return Encoding.ASCII.GetString(b, pos, len);
            }
        }

        internal static long ReadVlq(byte[] b, ref int pos)
        {
            long v = 0;
            for (int i = 0; i < 4 && pos < b.Length; i++)
            {
                int c = b[pos++];
                v = (v << 7) | (uint)(c & 0x7F);
                if ((c & 0x80) == 0) break;
            }
            return v;
        }

        static string Ascii(byte[] b, int pos, int len) => pos + len <= b.Length ? Encoding.ASCII.GetString(b, pos, len) : "";
        static int ReadU16(byte[] b, int p) => (b[p] << 8) | b[p + 1];
        static uint ReadU32(byte[] b, int p) => (uint)((b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3]);
    }

    /// <summary>テスト・デモ用の SMF (format 1) ライター。</summary>
    public static class SmfWriter
    {
        public sealed class TrackData
        {
            public string name = "";
            public readonly List<MidiNote> notes = new List<MidiNote>();
        }

        public static byte[] Write(IList<TrackData> tracks, double bpm, int division = 480)
        {
            using (var ms = new MemoryStream())
            {
                ms.Write(Encoding.ASCII.GetBytes("MThd"), 0, 4);
                WriteU32(ms, 6);
                WriteU16(ms, 1);
                WriteU16(ms, tracks.Count + 1);
                WriteU16(ms, division);

                // コンダクタートラック
                var cond = new MemoryStream();
                int us = (int)Math.Round(60000000.0 / bpm);
                WriteVlq(cond, 0);
                cond.Write(new byte[] { 0xFF, 0x51, 0x03, (byte)(us >> 16), (byte)(us >> 8), (byte)us }, 0, 6);
                WriteVlq(cond, 0);
                cond.Write(new byte[] { 0xFF, 0x58, 0x04, 4, 2, 24, 8 }, 0, 7);
                WriteVlq(cond, 0);
                cond.Write(new byte[] { 0xFF, 0x2F, 0x00 }, 0, 3);
                WriteChunk(ms, cond.ToArray());

                foreach (var t in tracks)
                {
                    var tr = new MemoryStream();
                    var nameBytes = Encoding.UTF8.GetBytes(t.name ?? "");
                    WriteVlq(tr, 0);
                    tr.WriteByte(0xFF);
                    tr.WriteByte(0x03);
                    WriteVlq(tr, nameBytes.Length);
                    tr.Write(nameBytes, 0, nameBytes.Length);

                    var evs = new List<(long tick, int order, byte[] data)>();
                    foreach (var n in t.notes)
                    {
                        evs.Add((n.startTick, 1, new[] { (byte)(0x90 | n.channel), (byte)n.note, (byte)Math.Max(1, n.velocity) }));
                        evs.Add((n.endTick, 0, new[] { (byte)(0x80 | n.channel), (byte)n.note, (byte)0 }));
                    }
                    evs.Sort((a, b) => a.tick != b.tick ? a.tick.CompareTo(b.tick) : a.order.CompareTo(b.order));
                    long last = 0;
                    foreach (var e in evs)
                    {
                        WriteVlq(tr, e.tick - last);
                        last = e.tick;
                        tr.Write(e.data, 0, e.data.Length);
                    }
                    WriteVlq(tr, 0);
                    tr.Write(new byte[] { 0xFF, 0x2F, 0x00 }, 0, 3);
                    WriteChunk(ms, tr.ToArray());
                }
                return ms.ToArray();
            }
        }

        static void WriteChunk(Stream s, byte[] data)
        {
            s.Write(Encoding.ASCII.GetBytes("MTrk"), 0, 4);
            WriteU32(s, (uint)data.Length);
            s.Write(data, 0, data.Length);
        }

        internal static void WriteVlq(Stream s, long v)
        {
            var buf = new Stack<byte>();
            buf.Push((byte)(v & 0x7F));
            v >>= 7;
            while (v > 0)
            {
                buf.Push((byte)((v & 0x7F) | 0x80));
                v >>= 7;
            }
            while (buf.Count > 0) s.WriteByte(buf.Pop());
        }

        static void WriteU16(Stream s, int v)
        {
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        static void WriteU32(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }
    }
}
