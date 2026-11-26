using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UniDAW.Editor
{
    /// <summary>FamiTracker テキスト取り込みの結果（落とした要素の数など）。</summary>
    public struct FamiTrackerReport
    {
        public int tracks, patterns, instruments, skippedFx, skippedChannels;

        public override string ToString() =>
            $"{patterns} patterns, {instruments} instruments, skipped fx {skippedFx}" +
            (tracks > 1 ? $", 先頭の曲のみ（全 {tracks} 曲）" : "") +
            (skippedChannels > 0 ? $", 2A03 以外の {skippedChannels} ch は無視" : "");
    }

    /// <summary>
    /// FamiTracker / 0CC-FamiTracker の「Export text」（.txt）を Song にする。
    /// 2A03 の Pulse1 / Pulse2 / Triangle / Noise と 2A03 楽器のマクロ、先頭の曲（TRACK）だけを読む。
    /// FamiTracker のパターンはチャンネルごとなので、順序表の行（各チャンネルのパターン番号の組）ごとに 1 パターンを作る。
    /// </summary>
    public static class FamiTrackerImport
    {
        const int FtChannels = 5; // 2A03：Pulse1 Pulse2 Triangle Noise DPCM
        /// <summary>そのまま持ち込めるエフェクト（文字が同じ意味のもの）。B / C / D（ジャンプ・停止）や H / I（スイープ）などは落とす。</summary>
        const string PassFx = "01234AVFSGQR";

        sealed class Track
        {
            public int rows = 64, speed = 6, tempo = 150;
            public string name = "";
            public int[] columns = { 1, 1, 1, 1, 1 };
            public readonly List<int[]> order = new List<int[]>();
            /// <summary>(チャンネル, パターン番号) → 行の文字列（チャンネルの列）。</summary>
            public readonly Dictionary<(int, int), Dictionary<int, string>> pats = new Dictionary<(int, int), Dictionary<int, string>>();
        }

        public static Song Import(string text, string fallbackTitle, out FamiTrackerReport report)
        {
            report = default;
            var macros = new Dictionary<(int type, int index), Sequence>();
            var insts = new SortedDictionary<int, (string name, int[] seq)>();
            var tracks = new List<Track>();
            string title = null;
            Track cur = null;
            int curPat = -1;
            int expansion = 0;

            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int sp = line.IndexOfAny(new[] { ' ', '\t' });
                var key = (sp < 0 ? line : line.Substring(0, sp)).ToUpperInvariant();
                var rest = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                switch (key)
                {
                    case "TITLE":
                        title = Quoted(rest);
                        break;
                    case "EXPANSION":
                        expansion = Int(rest);
                        break;
                    case "MACRO":
                    {
                        // MACRO type index loop release setting : values
                        var (head, values) = SplitColon(rest);
                        var h = Ints(head);
                        if (h.Length < 4) break;
                        var vals = Ints(values);
                        var seq = new Sequence { values = vals };
                        seq.loop = h[2] >= 0 && h[2] < vals.Length ? h[2] : -1;
                        // FamiTracker のリリース点 R は「R で止まり、離したら R+1 から」。UniDAW は「release の手前で保持」
                        seq.release = h[3] >= 0 && h[3] + 1 < vals.Length ? h[3] + 1 : -1;
                        macros[(h[0], h[1])] = seq;
                        break;
                    }
                    case "INST2A03":
                    {
                        // INST2A03 index vol arp pitch hipitch duty "name"
                        var h = Ints(rest.Split('"')[0]);
                        if (h.Length < 6) break;
                        insts[h[0]] = (Quoted(rest), new[] { h[1], h[2], h[3], h[4], h[5] });
                        break;
                    }
                    case "TRACK":
                    {
                        var h = Ints(rest.Split('"')[0]);
                        cur = new Track { name = Quoted(rest) };
                        if (h.Length >= 3)
                        {
                            cur.rows = Clamp(h[0], 1, Pattern.MaxRows);
                            cur.speed = Clamp(h[1], 1, 31);
                            cur.tempo = Math.Max(1, h[2]);
                        }
                        tracks.Add(cur);
                        curPat = -1;
                        break;
                    }
                    case "COLUMNS":
                        if (cur != null) cur.columns = Ints(SplitColon(rest).right);
                        break;
                    case "ORDER":
                        if (cur != null) cur.order.Add(Hexes(SplitColon(rest).right));
                        break;
                    case "PATTERN":
                        curPat = Hex(rest);
                        break;
                    case "ROW":
                    {
                        if (cur == null || curPat < 0) break;
                        var parts = rest.Split(':');
                        int row = Hex(parts[0]);
                        for (int c = 0; c + 1 < parts.Length; c++)
                        {
                            if (!cur.pats.TryGetValue((c, curPat), out var rows)) cur.pats[(c, curPat)] = rows = new Dictionary<int, string>();
                            rows[row] = parts[c + 1].Trim();
                        }
                        break;
                    }
                }
            }
            if (tracks.Count == 0) throw new FormatException("TRACK が見つかりません（FamiTracker の File > Export text で書き出した .txt を選んでくださいませ）");

            var t = tracks[0];
            report.tracks = tracks.Count;
            var song = Song.CreateDefault(t.rows);
            song.title = !string.IsNullOrEmpty(title) ? title : !string.IsNullOrEmpty(t.name) ? t.name : fallbackTitle;
            song.rowsPerBeat = 4;
            song.speed = t.speed;
            // FamiTracker の行/分 = tempo * 24 / speed。UniDAW は bpm * rowsPerBeat
            song.bpm = Math.Max(20f, Math.Min(400f, t.tempo * 6f / t.speed));

            // 楽器：FamiTracker の番号 → Song の番号
            var instMap = new Dictionary<int, int>();
            if (insts.Count > 0)
            {
                song.instruments.Clear();
                foreach (var kv in insts)
                {
                    var inst = new Instrument { name = string.IsNullOrEmpty(kv.Value.name) ? $"Inst {kv.Key:X2}" : kv.Value.name };
                    var s = kv.Value.seq;
                    inst.volume = Macro(macros, 0, s[0]);
                    inst.arpeggio = Macro(macros, 1, s[1]);
                    // ピッチは FamiTracker では周期の増減（+ で下がる）なので符号を反転する
                    inst.pitch = Macro(macros, 2, s[2]);
                    for (int i = 0; i < inst.pitch.values.Length; i++) inst.pitch.values[i] = Clamp(-inst.pitch.values[i], -32, 32);
                    inst.duty = Macro(macros, 4, s[4]);
                    inst.EnsureShape();
                    instMap[kv.Key] = song.instruments.Count;
                    song.instruments.Add(inst);
                }
            }
            report.instruments = insts.Count;

            // 順序表の行ごとにパターン（同じ組は使い回す）
            song.patterns.Clear();
            song.order.Clear();
            song.orderTranspose.Clear();
            var made = new Dictionary<string, int>();
            foreach (var frame in t.order)
            {
                var keyText = string.Join(",", System.Linq.Enumerable.Take(frame, Song.ChannelCount));
                if (!made.TryGetValue(keyText, out int pi))
                {
                    pi = song.patterns.Count;
                    made[keyText] = pi;
                    song.patterns.Add(BuildPattern(t, frame, pi, instMap, ref report));
                }
                song.order.Add(pi);
                song.orderTranspose.Add(0);
            }
            if (song.patterns.Count == 0)
            {
                song.patterns.Add(new Pattern(t.rows, "A"));
                song.order.Add(0);
                song.orderTranspose.Add(0);
            }
            report.patterns = song.patterns.Count;
            if (expansion != 0) report.skippedChannels = Math.Max(0, (t.columns.Length) - FtChannels);
            song.Validate();
            return song;
        }

        static Pattern BuildPattern(Track t, int[] frame, int index, Dictionary<int, int> instMap, ref FamiTrackerReport report)
        {
            var p = new Pattern(t.rows, PatternName(index));
            for (int c = 0; c < Song.ChannelCount && c < frame.Length; c++)
            {
                if (!t.pats.TryGetValue((c, frame[c]), out var rows)) continue;
                var kind = Song.Kinds[c];
                foreach (var kv in rows)
                {
                    if (kv.Key < 0 || kv.Key >= t.rows) continue;
                    p.tracks[c].cells[kv.Key] = ParseCell(kv.Value, kind, instMap, ref report);
                }
            }
            return p;
        }

        /// <summary>"C-4 00 F A01 ..." のようなチャンネル 1 つ分の列。</summary>
        public static Cell ParseCell(string text, ChannelKind kind, IReadOnlyDictionary<int, int> instMap, ref FamiTrackerReport report)
        {
            var cell = Cell.Blank;
            var tok = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length == 0) return cell;
            var n = tok[0];
            // FamiTracker：... 空 / --- ノートカット / === リリース（どちらもノートオフにする）
            if (n == "---" || n == "===") cell.note = Cell.Off;
            else if (!n.StartsWith(".") && NoteText.TryParse(n, kind, out int note)) cell.note = note;
            if (tok.Length > 1 && TryHex(tok[1], out int ins))
                cell.instrument = instMap != null && instMap.TryGetValue(ins, out int mapped) ? mapped : -1;
            if (tok.Length > 2 && TryHex(tok[2], out int vol)) cell.volume = Clamp(vol, 0, 15);
            int slot = 0;
            for (int i = 3; i < tok.Length; i++)
            {
                var f = tok[i];
                if (f.Length < 3 || f[0] == '.') continue;
                if (PassFx.IndexOf(char.ToUpperInvariant(f[0])) < 0 || slot >= 2 || !FxText.TryParse(f, out var fx))
                {
                    report.skippedFx++;
                    continue;
                }
                if (slot++ == 0) cell.fx1 = fx;
                else cell.fx2 = fx;
            }
            return cell;
        }

        static Sequence Macro(Dictionary<(int, int), Sequence> macros, int type, int index) =>
            index >= 0 && macros.TryGetValue((type, index), out var s) ? s.Clone() : new Sequence();

        static string PatternName(int i)
        {
            var sb = new StringBuilder();
            do
            {
                sb.Insert(0, (char)('A' + i % 26));
                i = i / 26 - 1;
            } while (i >= 0);
            return sb.ToString();
        }

        static (string left, string right) SplitColon(string s)
        {
            int i = s.IndexOf(':');
            return i < 0 ? (s, "") : (s.Substring(0, i), s.Substring(i + 1));
        }

        static string Quoted(string s)
        {
            int a = s.IndexOf('"'), b = s.LastIndexOf('"');
            return a >= 0 && b > a ? s.Substring(a + 1, b - a - 1).Replace("\\\"", "\"") : "";
        }

        static int Clamp(int v, int lo, int hi) => Math.Max(lo, Math.Min(hi, v));
        static int Int(string s) => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
        static int Hex(string s) => TryHex(s.Trim(), out int v) ? v : -1;
        static bool TryHex(string s, out int v) => int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);

        static int[] Ints(string s)
        {
            var list = new List<int>();
            foreach (var tok in s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) list.Add(v);
            return list.ToArray();
        }

        static int[] Hexes(string s)
        {
            var list = new List<int>();
            foreach (var tok in s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                if (TryHex(tok, out int v)) list.Add(v);
            return list.ToArray();
        }
    }
}
