using System.Collections.Generic;
using UniDAW.Midi;

namespace UniDAW
{
    /// <summary>機能紹介用のデモ曲（オリジナル）とデモ MIDI の生成。</summary>
    public static class DemoSongs
    {
        static void Put(Pattern p, int ch, int row, string note, int inst = -1, int vol = -1, string fx1 = null, string fx2 = null, int prob = 100)
        {
            var kind = Song.Kinds[ch];
            NoteText.TryParse(note, kind, out int n);
            var c = Cell.Blank;
            c.note = n;
            c.instrument = inst;
            c.volume = vol;
            if (fx1 != null) FxText.TryParse(fx1, out c.fx1);
            if (fx2 != null) FxText.TryParse(fx2, out c.fx2);
            c.probability = prob;
            p.tracks[ch].cells[row] = c;
        }

        static void Bass(Pattern p, string low1, string high1, string low2, string high2)
        {
            for (int r = 0; r < 32; r += 2)
            {
                bool first = r < 16;
                bool up = (r / 2) % 2 == 1;
                Put(p, 2, r, first ? (up ? high1 : low1) : (up ? high2 : low2), Song.InstBass);
            }
        }

        static void Drums(Pattern p, bool fill)
        {
            for (int bar = 0; bar < 2; bar++)
            {
                int o = bar * 16;
                Put(p, 3, o + 0, "3-#", Song.InstKick);
                Put(p, 3, o + 10, "3-#", Song.InstKick);
                Put(p, 3, o + 4, "9-#", Song.InstSnare);
                Put(p, 3, o + 12, "9-#", Song.InstSnare);
                foreach (int r in new[] { 2, 6, 8, 14 })
                    Put(p, 3, o + r, "E-#", Song.InstHat, -1, null, null, r == 8 ? 100 : 65);
            }
            if (fill)
            {
                Put(p, 3, 29, "9-#", Song.InstSnare, 9);
                Put(p, 3, 30, "9-#", Song.InstSnare, 11);
                var c = p.tracks[3].cells[31];
                c = Cell.NoteCell(9, Song.InstSnare, 13);
                c.condA = 2;
                c.condB = 2; // 2回に1回だけ鳴るフィル
                p.tracks[3].cells[31] = c;
            }
        }

        static void Chords(Pattern p, string n1, string fx1, string n2, string fx2)
        {
            for (int r = 0; r < 32; r += 4)
            {
                bool first = r < 16;
                int vol = (r / 4) % 2 == 0 ? 10 : 6; // ボリュームのパラメータロック
                Put(p, 1, r, first ? n1 : n2, Song.InstSoft, vol, first ? fx1 : fx2);
            }
        }

        static void MainLead(Pattern p, bool dutyVariation)
        {
            Put(p, 0, 0, "G-5", Song.InstLead, -1, dutyVariation ? "V01" : null);
            Put(p, 0, 3, "D#5");
            Put(p, 0, 6, "C-5", -1, -1, "434");
            Put(p, 0, 10, "D-5");
            Put(p, 0, 12, "D#5");
            Put(p, 0, 14, "F-5");
            Put(p, 0, 16, "G-5", -1, -1, "446");
            Put(p, 0, 22, "G#5", -1, -1, dutyVariation ? "V00" : null);
            Put(p, 0, 24, "G-5");
            Put(p, 0, 26, "F-5");
            Put(p, 0, 28, "D#5", -1, -1, "A02");
            Put(p, 0, 31, "===");
        }

        public static Song BuildDemoSong()
        {
            var s = Song.CreateDefault(32);
            s.title = "UniDAW Demo";
            s.name = "UniDAW Demo";
            s.bpm = 140;
            s.rowsPerBeat = 4;
            s.speed = 6;
            s.patterns.Clear();

            // 00 Intro : ベース＋ドラム＋和音
            var intro = new Pattern(32, "Intro");
            Bass(intro, "C-3", "C-4", "G#2", "G#3");
            Drums(intro, false);
            Chords(intro, "C-4", "037", "G#3", "047");
            s.patterns.Add(intro);

            // 01 Main : メロディ追加
            var main = new Pattern(32, "Main");
            Bass(main, "C-3", "C-4", "G#2", "G#3");
            Drums(main, true);
            Chords(main, "C-4", "037", "G#3", "047");
            MainLead(main, false);
            s.patterns.Add(main);

            // 02 Bridge : ポルタメント・スライド
            var bridge = new Pattern(32, "Bridge");
            Bass(bridge, "D#3", "D#4", "A#2", "A#3");
            Drums(bridge, true);
            Chords(bridge, "D#4", "047", "A#3", "047");
            Put(bridge, 0, 0, "A#5", Song.InstLead);
            Put(bridge, 0, 4, "G-5");
            Put(bridge, 0, 8, "D#5");
            Put(bridge, 0, 12, "F-5");
            Put(bridge, 0, 14, "G-5", -1, -1, "308");
            Put(bridge, 0, 16, "F-5");
            Put(bridge, 0, 20, "D#5");
            Put(bridge, 0, 22, "D-5");
            Put(bridge, 0, 24, "D#5", -1, -1, "446");
            Put(bridge, 0, 30, "---", -1, -1, "203");
            s.patterns.Add(bridge);

            // 03 Main' : Pulse2 がポリメーター（6 ステップループ）
            var poly = new Pattern(32, "Main Poly");
            Bass(poly, "C-3", "C-4", "G#2", "G#3");
            Drums(poly, true);
            MainLead(poly, true);
            poly.tracks[1].length = 6;
            Put(poly, 1, 0, "C-5", Song.InstArp, 9);
            Put(poly, 1, 3, "G-4", Song.InstArp, 6);
            s.patterns.Add(poly);

            s.order = new List<int> { 0, 1, 2, 3 };
            s.Validate();
            return s;
        }

        /// <summary>デモ用 MIDI（4トラック: Lead / Chords / Bass / Drums, 8小節）。</summary>
        public static byte[] BuildDemoMidi()
        {
            const int q = 480;
            var lead = new SmfWriter.TrackData { name = "Lead" };
            var chords = new SmfWriter.TrackData { name = "Chords" };
            var bass = new SmfWriter.TrackData { name = "Bass" };
            var drums = new SmfWriter.TrackData { name = "Drums" };

            // 0 = タイ（前の音を延ばす）, -1 = 休符。1拍単位。
            int[] melody =
            {
                76, 0, 72, 74,  72, 0, 69, -1,  67, 69, 72, 76,  74, 0, 0, 71,
                76, 77, 79, 76, 77, 76, 74, 72, 72, 74, 76, 79,  76, 0, 0, -1,
            };
            AddLine(lead, 0, melody, q, 100);

            int[][] chordTones = { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 55, 60, 64 }, new[] { 55, 59, 62 } };
            int[] roots = { 45, 41, 48, 43 };
            for (int bar = 0; bar < 8; bar++)
            {
                int ci = bar % 4;
                long t0 = bar * 4L * q;
                foreach (var n in chordTones[ci])
                    chords.notes.Add(new MidiNote { channel = 1, note = n, velocity = 70, startTick = t0, endTick = t0 + 4 * q - 20 });
                for (int e = 0; e < 8; e++)
                {
                    int note = roots[ci] + (e % 2 == 1 ? 12 : 0);
                    bass.notes.Add(new MidiNote { channel = 2, note = note, velocity = 110, startTick = t0 + e * q / 2, endTick = t0 + e * q / 2 + q / 2 - 30 });
                }
                for (int e = 0; e < 8; e++)
                {
                    long t = t0 + e * q / 2;
                    if (e == 0 || e == 4 || (bar % 2 == 1 && e == 5)) drums.notes.Add(new MidiNote { channel = 9, note = 36, velocity = 120, startTick = t, endTick = t + 60 });
                    if (e == 2 || e == 6) drums.notes.Add(new MidiNote { channel = 9, note = 38, velocity = 110, startTick = t, endTick = t + 60 });
                    drums.notes.Add(new MidiNote { channel = 9, note = 42, velocity = 70, startTick = t, endTick = t + 30 });
                }
            }
            return SmfWriter.Write(new List<SmfWriter.TrackData> { lead, chords, bass, drums }, 128);
        }

        static void AddLine(SmfWriter.TrackData t, int channel, int[] beats, int q, int vel)
        {
            int i = 0;
            while (i < beats.Length)
            {
                if (beats[i] <= 0) { i++; continue; }
                int start = i;
                int note = beats[i++];
                while (i < beats.Length && beats[i] == 0) i++;
                t.notes.Add(new MidiNote { channel = channel, note = note, velocity = vel, startTick = (long)start * q, endTick = (long)i * q - 30 });
            }
        }
    }
}
