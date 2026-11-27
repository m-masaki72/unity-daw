using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace UniDAW
{
    /// <summary>FamiTracker 取り込みの結果（落とした要素の数など）。</summary>
    public struct FamiTrackerReport
    {
        public int tracks, patterns, instruments, skippedFx, skippedMacros, skippedChannels, clampedTempo;

        public override string ToString() =>
            $"{patterns} patterns, {instruments} instruments, skipped fx {skippedFx}" +
            (skippedMacros > 0 ? $", 未対応マクロ {skippedMacros}" : "") +
            (clampedTempo > 0 ? $", 範囲外テンポ {clampedTempo}" : "") +
            (tracks > 1 ? $", 先頭の曲のみ（全 {tracks} 曲）" : "") +
            (skippedChannels > 0 ? $", 2A03 以外の {skippedChannels} ch は無視" : "");
    }

    /// <summary>
    /// FamiTracker / 0CC-FamiTracker の「Export text」（.txt）を Song にする。
    /// 2A03 の Pulse1 / Pulse2 / Triangle / Noise と 2A03 楽器のマクロ、先頭の曲（TRACK）だけを読む。
    /// FamiTracker のパターンはチャンネルごとなので、順序表の行（各チャンネルのパターン番号の組）ごとに 1 パターンを作る。
    /// 鳴り方を合わせるため、順序表の順にたどりながら次を変換する：
    /// ・FamiTracker はチャンネルに音量と 0 / 1 / 2 / 3 / 4 / A / V を持ち越すが、UniDAW は発音で戻すので、ノートの行に書き足す
    /// ・Fxx（speed / tempo）は同じ行の速さになる BPM（Fxx ≥ 20）にする
    /// ・三角波は 1 オクターブ下げ、音量は 0 か最大だけにする
    /// ・--- はノートオフ + S00（即カット）、=== はリリース点のある楽器だけノートオフ
    /// ・Bxx / Cxx / Dxx のある行でそのフレームを終える（パターンを短くする）
    /// </summary>
    public static class FamiTrackerImport
    {
        const int FtChannels = 5; // 2A03：Pulse1 Pulse2 Triangle Noise DPCM
        /// <summary>そのまま持ち込めるエフェクト。F は BPM に直す。H / I（スイープ）などは落とす。</summary>
        const string PassFx = "01234AVSGQR";
        /// <summary>FamiTracker でチャンネルに持ち越される（UniDAW では発音で戻る）エフェクト。書き足す優先順。</summary>
        static readonly FxType[] Carried = { FxType.Arpeggio, FxType.Vibrato, FxType.Duty, FxType.Portamento, FxType.SlideUp, FxType.SlideDown, FxType.VolumeSlide };

        sealed class Track
        {
            public int rows = 64, speed = 6, tempo = 150;
            public string name = "";
            public int[] columns = { 1, 1, 1, 1, 1 };
            public readonly List<int[]> order = new List<int[]>();
            /// <summary>(チャンネル, パターン番号) → 行の文字列（チャンネルの列）。</summary>
            public readonly Dictionary<(int, int), Dictionary<int, string>> pats = new Dictionary<(int, int), Dictionary<int, string>>();
        }

        /// <summary>順序表をたどるあいだの状態（テンポとチャンネルごとの持ち越し）。</summary>
        sealed class Playhead
        {
            public int tempo, speed, rowsPerBeat;
            public readonly int[] volume = { 15, 15, 15, 15 };
            public readonly int[] instrument = { -1, -1, -1, -1 };
            public readonly Dictionary<FxType, Fx>[] fx =
                { new Dictionary<FxType, Fx>(), new Dictionary<FxType, Fx>(), new Dictionary<FxType, Fx>(), new Dictionary<FxType, Fx>() };
        }

        /// <summary>ファイルの中身を文字列にする。UTF-8（BOM あり / なし）でなければ Shift_JIS（日本語版 FamiTracker の書き出し）。</summary>
        public static string Decode(byte[] bytes)
        {
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
            }
            catch (DecoderFallbackException) { }
            try
            {
                return Encoding.GetEncoding(932).GetString(bytes);
            }
            catch (Exception)
            {
                return Encoding.GetEncoding(28591).GetString(bytes); // Latin-1：最低限 ASCII 部分は読める
            }
        }

        public static Song Import(string text, string fallbackTitle, out FamiTrackerReport report)
        {
            report = default;
            var macros = new Dictionary<(int type, int index), (Sequence seq, int setting)>();
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
                        if (h.Length < 5) break;
                        var vals = Ints(values);
                        var seq = new Sequence { values = vals };
                        seq.loop = h[2] >= 0 && h[2] < vals.Length ? h[2] : -1;
                        // FamiTracker のリリース点 R は「R で止まり、離したら R+1 から」。UniDAW は「release の手前で保持」
                        seq.release = h[3] >= 0 && h[3] + 1 < vals.Length ? h[3] + 1 : -1;
                        macros[(h[0], h[1])] = (seq, h[4]);
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
            song.speed = t.speed;
            // FamiTracker の行/分 = tempo * 24 / speed。UniDAW は bpm * rowsPerBeat。BPM が 20〜400 に収まる rowsPerBeat を選ぶ
            float rowsPerMin = t.tempo * 24f / t.speed;
            int rpb = 4;
            while (rowsPerMin / rpb > 400f && rpb > 1) rpb /= 2;
            while (rowsPerMin / rpb < 20f && rpb < 16) rpb *= 2;
            song.rowsPerBeat = rpb;
            song.bpm = Math.Max(20f, Math.Min(400f, rowsPerMin / rpb));

            // 楽器：FamiTracker の番号 → Song の番号
            var instMap = new Dictionary<int, int>();
            if (insts.Count > 0)
            {
                song.instruments.Clear();
                foreach (var kv in insts)
                {
                    var inst = new Instrument { name = string.IsNullOrEmpty(kv.Value.name) ? $"Inst {kv.Key:X2}" : kv.Value.name };
                    var s = kv.Value.seq;
                    inst.volume = Macro(macros, 0, s[0]).seq;
                    inst.arpeggio = ArpMacro(macros, s[1], ref report);
                    inst.pitch = PitchMacro(macros, s[2]);
                    inst.duty = Macro(macros, 4, s[4]).seq;
                    inst.EnsureShape();
                    instMap[kv.Key] = song.instruments.Count;
                    song.instruments.Add(inst);
                }
            }
            report.instruments = insts.Count;

            // 順序表の行ごとにパターン（同じ組は使い回す。持ち越しは最初に作ったときの流れで決まる）
            song.patterns.Clear();
            song.order.Clear();
            song.orderTranspose.Clear();
            var made = new Dictionary<string, int>();
            var playhead = new Playhead { tempo = t.tempo, speed = t.speed, rowsPerBeat = rpb };
            foreach (var frame in t.order)
            {
                var keyText = string.Join(",", frame.Take(Song.ChannelCount));
                if (!made.TryGetValue(keyText, out int pi))
                {
                    pi = song.patterns.Count;
                    made[keyText] = pi;
                    song.patterns.Add(BuildPattern(t, frame, pi, instMap, song, playhead, ref report));
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
            if (expansion != 0) report.skippedChannels = Math.Max(0, t.columns.Length - FtChannels);
            song.Validate();
            return song;
        }

        static Pattern BuildPattern(Track t, int[] frame, int index, Dictionary<int, int> instMap, Song song, Playhead head,
            ref FamiTrackerReport report)
        {
            var rows = new Dictionary<int, string>[Song.ChannelCount];
            for (int c = 0; c < Song.ChannelCount && c < frame.Length; c++) t.pats.TryGetValue((c, frame[c]), out rows[c]);

            // Bxx / Cxx / Dxx でフレームが終わる行
            int length = t.rows;
            for (int r = 0; r < length; r++)
                for (int c = 0; c < FtChannels && c < frame.Length; c++)
                {
                    string text = null;
                    if (c < Song.ChannelCount ? rows[c] != null && rows[c].TryGetValue(r, out text) : TryRaw(t, c, frame[c], r, out text))
                        if (EndsFrame(text)) length = r + 1;
                }

            var p = new Pattern(length, PatternName(index));
            for (int r = 0; r < length; r++)
            {
                // テンポはどのチャンネルの Fxx でも変わるので、行ごとに全チャンネルを見る
                int bpmParam = -1;
                for (int c = 0; c < FtChannels && c < frame.Length; c++)
                {
                    string text = null;
                    if (c < Song.ChannelCount ? rows[c] != null && rows[c].TryGetValue(r, out text) : TryRaw(t, c, frame[c], r, out text))
                        bpmParam = Math.Max(bpmParam, TempoChange(text, head, ref report));
                }
                for (int c = 0; c < Song.ChannelCount; c++)
                {
                    string text = null;
                    var cell = rows[c] != null && rows[c].TryGetValue(r, out text)
                        ? ParseCell(text, Song.Kinds[c], instMap, ref report)
                        : Cell.Blank;
                    Carry(ref cell, c, text, song, head);
                    if (c == 0 && bpmParam >= 0) AddFx(ref cell, new Fx(FxType.Speed, bpmParam), ref report);
                    p.tracks[c].cells[r] = cell;
                }
            }
            return p;
        }

        static bool TryRaw(Track t, int c, int pat, int r, out string text)
        {
            text = null;
            return t.pats.TryGetValue((c, pat), out var rows) && rows.TryGetValue(r, out text);
        }

        static IEnumerable<string> FxTokens(string text)
        {
            var tok = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 3; i < tok.Length; i++)
                if (tok[i].Length >= 3 && tok[i][0] != '.') yield return tok[i];
        }

        static bool EndsFrame(string text) => FxTokens(text).Any(f => "BCD".IndexOf(char.ToUpperInvariant(f[0])) >= 0);

        /// <summary>Fxx でテンポの状態を進め、その行の速さになる UniDAW の Fxx（BPM）を返す。無ければ -1。</summary>
        static int TempoChange(string text, Playhead head, ref FamiTrackerReport report)
        {
            bool changed = false;
            foreach (var f in FxTokens(text))
            {
                if (char.ToUpperInvariant(f[0]) != 'F' || !TryHex(f.Substring(1), out int v) || v == 0) continue;
                if (v < 0x20) head.speed = v;
                else head.tempo = v;
                changed = true;
            }
            if (!changed) return -1;
            int bpm = (int)Math.Round(head.tempo * 24.0 / head.speed / head.rowsPerBeat);
            if (bpm < 0x20 || bpm > 0xFF) report.clampedTempo++;
            return Clamp(bpm, 0x20, 0xFF);
        }

        /// <summary>FamiTracker がチャンネルに持ち越す音量・エフェクト・楽器を、UniDAW のセルに書き足す。</summary>
        static void Carry(ref Cell cell, int c, string text, Song song, Playhead head)
        {
            var kind = Song.Kinds[c];
            if (cell.instrument >= 0) head.instrument[c] = cell.instrument;

            // '===' はリリース点のある楽器だけノートオフ（無ければ FamiTracker では鳴り続ける）
            if (cell.IsOff && text != null && text.StartsWith("==="))
            {
                var inst = head.instrument[c] >= 0 ? song.GetInstrument(head.instrument[c]) : null;
                if (inst != null && inst.volume.release <= 0) cell.note = Cell.Empty;
            }

            if (cell.volume >= 0)
            {
                if (kind == ChannelKind.Triangle) cell.volume = cell.volume == 0 ? 0 : 15;
                head.volume[c] = cell.volume;
            }
            foreach (var fx in new[] { cell.fx1, cell.fx2 })
                if (Array.IndexOf(Carried, fx.type) >= 0)
                {
                    // 1xx / 2xx / 3xx はどれか 1 つだけが効く
                    if (fx.type == FxType.SlideUp || fx.type == FxType.SlideDown || fx.type == FxType.Portamento)
                    {
                        head.fx[c].Remove(FxType.SlideUp);
                        head.fx[c].Remove(FxType.SlideDown);
                        head.fx[c].Remove(FxType.Portamento);
                    }
                    if (fx.param == 0) head.fx[c].Remove(fx.type);
                    else head.fx[c][fx.type] = fx;
                }

            if (!cell.HasNote) return;
            if (cell.volume < 0 && head.volume[c] != 15) cell.volume = head.volume[c];
            var dummy = default(FamiTrackerReport);
            foreach (var type in Carried)
                if (head.fx[c].TryGetValue(type, out var fx) && cell.fx1.type != type && cell.fx2.type != type)
                    AddFx(ref cell, fx, ref dummy);
        }

        static void AddFx(ref Cell cell, Fx fx, ref FamiTrackerReport report)
        {
            if (cell.fx1.IsEmpty) cell.fx1 = fx;
            else if (cell.fx2.IsEmpty) cell.fx2 = fx;
            else report.skippedFx++;
        }

        /// <summary>"C-4 00 F A01 ..." のようなチャンネル 1 つ分の列。持ち越しや Fxx の変換はしない。</summary>
        public static Cell ParseCell(string text, ChannelKind kind, IReadOnlyDictionary<int, int> instMap, ref FamiTrackerReport report)
        {
            var cell = Cell.Blank;
            var tok = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length == 0) return cell;
            var n = tok[0];
            // FamiTracker：... 空 / --- ノートカット / === リリース
            if (n == "---" || n == "===") cell.note = Cell.Off;
            else if (!n.StartsWith(".") && NoteText.TryParse(n, kind, out int note))
                // FamiTracker の三角波は書いた音名より 1 オクターブ低く鳴る
                cell.note = kind == ChannelKind.Triangle ? Math.Max(NoteText.MinNote, note - 12) : note;
            if (tok.Length > 1 && TryHex(tok[1], out int ins))
                cell.instrument = instMap != null && instMap.TryGetValue(ins, out int mapped) ? mapped : -1;
            if (tok.Length > 2 && TryHex(tok[2], out int vol)) cell.volume = Clamp(vol, 0, 15);
            if (n == "---") AddFx(ref cell, new Fx(FxType.NoteCut, 0), ref report); // リリースの尾を鳴らさず即止める
            foreach (var f in FxTokens(text))
            {
                char ch = char.ToUpperInvariant(f[0]);
                if (ch == 'F' || "BCD".IndexOf(ch) >= 0) continue; // F は BPM に、B / C / D はパターンの長さに直す
                if (PassFx.IndexOf(ch) < 0 || !FxText.TryParse(f, out var fx))
                {
                    report.skippedFx++;
                    continue;
                }
                AddFx(ref cell, fx, ref report);
            }
            return cell;
        }

        static (Sequence seq, int setting) Macro(Dictionary<(int, int), (Sequence seq, int setting)> macros, int type, int index) =>
            index >= 0 && macros.TryGetValue((type, index), out var m) ? (m.seq.Clone(), m.setting) : (new Sequence(), 0);

        /// <summary>アルペジオ：Absolute はそのまま、Relative は累積に直す。Fixed（音名そのもの）は表せないので落とす。</summary>
        static Sequence ArpMacro(Dictionary<(int, int), (Sequence seq, int setting)> macros, int index, ref FamiTrackerReport report)
        {
            var (seq, setting) = Macro(macros, 1, index);
            if (seq.IsEmpty) return seq;
            if (setting == 1)
            {
                report.skippedMacros++;
                return new Sequence();
            }
            if (setting == 2)
                for (int i = 1; i < seq.values.Length; i++) seq.values[i] += seq.values[i - 1];
            for (int i = 0; i < seq.values.Length; i++) seq.values[i] = Clamp(seq.values[i], -24, 24);
            return seq;
        }

        /// <summary>
        /// ピッチ：FamiTracker は周期の増減（+ で下がる）なので符号を反転する。
        /// Relative（既定）は UniDAW と同じ累積、Absolute は差分に直す。
        /// </summary>
        static Sequence PitchMacro(Dictionary<(int, int), (Sequence seq, int setting)> macros, int index)
        {
            var (seq, setting) = Macro(macros, 2, index);
            var v = seq.values;
            if (setting == 1)
                for (int i = v.Length - 1; i > 0; i--) v[i] -= v[i - 1];
            for (int i = 0; i < v.Length; i++) v[i] = Clamp(-v[i], -32, 32);
            return seq;
        }

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

        /// <summary>"..." の中身。書き出し側の "" と \" のどちらのエスケープも戻す。</summary>
        static string Quoted(string s)
        {
            int a = s.IndexOf('"'), b = s.LastIndexOf('"');
            return a >= 0 && b > a ? s.Substring(a + 1, b - a - 1).Replace("\"\"", "\"").Replace("\\\"", "\"") : "";
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
