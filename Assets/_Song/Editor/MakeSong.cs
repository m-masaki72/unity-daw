using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UniDAW;
using UniDAW.Editor;
using UniDAW.Midi;

namespace UniDAW.Editor.SongMaker
{
    /// <summary>「Neon Dash」：Am 160 BPM のチップチューン。EQ / DUCK のオートメーション入り。</summary>
    public static class MakeSong
    {
        const string AssetPath = "Assets/UniDAW/Songs/NeonDash.asset";
        const string S = "C:/Users/memen/AppData/Local/Temp/claude/b2bf0c2f2095c5b4/471a08f3-bd89-406a-b4a5-f090e619ba7c/scratchpad/";

        static void Put(Pattern p, int ch, int row, string note, int inst = -1, int vol = -1, string fx1 = null, string fx2 = null, int prob = 100)
        {
            var c = p.tracks[ch].cells[row];
            if (note != null)
            {
                NoteText.TryParse(note, Song.Kinds[ch], out int n);
                c.note = n;
                c.instrument = inst;
            }
            c.volume = vol;
            if (fx1 != null) FxText.TryParse(fx1, out c.fx1);
            if (fx2 != null) FxText.TryParse(fx2, out c.fx2);
            c.probability = prob;
            p.tracks[ch].cells[row] = c;
        }

        static void Fx(Pattern p, int ch, int row, string fx)
        {
            var c = p.tracks[ch].cells[row];
            FxText.TryParse(fx, out c.fx2);
            p.tracks[ch].cells[row] = c;
        }

        static void Drum(Pattern p, int row, int gm, int vol = -1, int prob = 100)
        {
            var h = DrumMap.Map(gm);
            var c = Cell.NoteCell(h.pitch, h.instrument, vol);
            c.probability = prob;
            p.tracks[3].cells[row] = c;
        }

        // 1 小節 16 行。lo / hi は小節ごとのベースの低い音・高い音
        static void Bass(Pattern p, string[] lo, string[] hi, bool drive)
        {
            for (int bar = 0; bar < 4; bar++)
                for (int r = 0; r < 16; r += 2)
                {
                    bool up = drive && (r / 2) % 2 == 1;
                    Put(p, 2, bar * 16 + r, up ? hi[bar] : lo[bar], Song.InstBass);
                }
        }

        static void Beat(Pattern p, bool four, bool fill)
        {
            for (int bar = 0; bar < 4; bar++)
            {
                int o = bar * 16;
                if (four) foreach (int r in new[] { 0, 4, 8, 12 }) Drum(p, o + r, 36);
                else foreach (int r in new[] { 0, 6, 10 }) Drum(p, o + r, 36);
                Drum(p, o + 4, 38);
                Drum(p, o + 12, 38);
                foreach (int r in new[] { 2, 6, 10, 14 })
                    if (p.tracks[3].cells[o + r].note < 0) Drum(p, o + r, 42, -1, r == 14 ? 70 : 100);
            }
            if (fill)
                for (int r = 56; r < 64; r++) Drum(p, r, 38, 6 + (r - 56), r < 60 ? 75 : 100);
        }

        // ch1 のコード（アルペジオのエフェクトで刻む）
        static void Pad(Pattern p, string[] roots, string[] arps, int inst, int step, int vol)
        {
            for (int bar = 0; bar < 4; bar++)
                for (int r = 0; r < 16; r += step)
                    Put(p, 1, bar * 16 + r, roots[bar], inst, r == 0 ? vol : vol - 3, arps[bar]);
        }

        static void Lead(Pattern p, params (int row, string note, string fx)[] line)
        {
            foreach (var (row, note, fx) in line) Put(p, 0, row, note, note.StartsWith("=") ? -1 : Song.InstLead, -1, fx);
        }

        static readonly string[] VerseLo = { "A-2", "F-2", "C-3", "G-2" }, VerseHi = { "A-3", "F-3", "C-4", "G-3" };
        static readonly string[] VersePad = { "A-4", "F-4", "C-5", "G-4" }, VerseArp = { "037", "047", "047", "047" };
        static readonly string[] ChorusLo = { "F-2", "G-2", "E-2", "A-2" }, ChorusHi = { "F-3", "G-3", "E-3", "A-3" };
        static readonly string[] ChorusPad = { "F-4", "G-4", "E-4", "A-4" }, ChorusArp = { "047", "047", "037", "037" };
        static readonly string[] BridgeLo = { "D-3", "E-2", "F-2", "G-2" }, BridgeHi = { "D-4", "E-3", "F-3", "G-3" };
        static readonly string[] BridgePad = { "D-5", "E-4", "F-4", "G-4" }, BridgeArp = { "037", "037", "047", "047" };

        static Pattern Intro()
        {
            var p = new Pattern(64, "Intro") { chords = "Am F C G" };
            Bass(p, VerseLo, VerseHi, false);
            Pad(p, VersePad, VerseArp, Song.InstArp, 2, 9);
            for (int i = 0; i < 8; i++) Fx(p, 1, i * 8, $"H{i * 3 / 2:X2}"); // ハイを閉じた状態から開いていく
            Fx(p, 1, 62, "H0C");
            for (int bar = 2; bar < 4; bar++) foreach (int r in new[] { 0, 4, 8, 12 }) Drum(p, bar * 16 + r, 36);
            for (int r = 50; r < 64; r += 4) Drum(p, r, 42, 4 + (r - 48) / 2);
            Fx(p, 2, 0, "D00");
            return p;
        }

        static Pattern Verse(string name, bool second)
        {
            var p = new Pattern(64, name) { chords = "Am F C G" };
            Bass(p, VerseLo, VerseHi, true);
            Beat(p, false, second);
            Pad(p, VersePad, VerseArp, Song.InstSoft, 8, 8);
            Fx(p, 1, 0, "D40");
            Fx(p, 2, 2, "D30");
            Lead(p, (0, "E-5", null), (3, "D-5", null), (6, "C-5", null), (8, "D-5", null), (10, "E-5", null), (12, "A-4", null), (15, "===", null),
                (16, "F-5", null), (19, "E-5", null), (22, "C-5", null), (24, "A-4", "446"), (30, "C-5", null),
                (32, "G-5", null), (35, "E-5", null), (38, "C-5", null), (40, "E-5", null), (42, "G-5", null), (44, "E-5", null),
                (48, "D-5", null), (50, "E-5", null), (52, "D-5", null), (54, "B-4", null),
                (56, second ? "D-5" : "G-4", second ? null : "446"), (62, "===", null));
            if (second) Put(p, 0, 58, "G-5", Song.InstLead, -1, "308");
            return p;
        }

        static Pattern Bridge()
        {
            var p = new Pattern(64, "Bridge") { chords = "Dm Em F G" };
            Bass(p, BridgeLo, BridgeHi, true);
            Beat(p, false, true);
            Pad(p, BridgePad, BridgeArp, Song.InstArp, 4, 8);
            for (int i = 0; i < 4; i++) Fx(p, 1, i * 16, $"L{0x0C - i * 3:X2}"); // ローを削って軽くし、サビ前に溜める
            Lead(p, (0, "F-5", null), (4, "E-5", null), (8, "D-5", null), (12, "A-4", null),
                (16, "G-5", null), (20, "F-5", null), (24, "E-5", null), (28, "B-4", null),
                (32, "A-5", null), (36, "G-5", null), (40, "F-5", null), (44, "C-6", "446"),
                (48, "B-5", null), (50, "A-5", null), (52, "G-5", null), (54, "F-5", null), (56, "G-5", "104"), (62, "===", null));
            return p;
        }

        static Pattern Chorus(string name, bool fill)
        {
            var p = new Pattern(64, name) { chords = "F G Em Am" };
            Bass(p, ChorusLo, ChorusHi, true);
            Beat(p, true, fill);
            Drum(p, 0, 49);
            Pad(p, ChorusPad, ChorusArp, Song.InstArp, 2, 10);
            Fx(p, 1, 0, "DC0"); // サビは深くポンプ
            Fx(p, 1, 2, "L0C"); // ブリッジで削ったローを戻す
            Fx(p, 2, 2, "D70");
            Lead(p, (0, "A-5", null), (4, "G-5", null), (6, "A-5", null), (8, "C-6", null), (12, "A-5", null), (14, "G-5", null),
                (16, "G-5", null), (20, "F-5", null), (22, "G-5", null), (24, "B-5", "446"),
                (32, "E-5", null), (36, "G-5", null), (38, "B-5", null), (40, "E-6", "446"), (44, "D-6", null), (46, "B-5", null),
                (48, "C-6", null), (52, "B-5", null), (54, "A-5", "446"), (62, "===", null));
            return p;
        }

        static Pattern Outro()
        {
            var p = new Pattern(64, "Outro") { chords = "F G Am Am" };
            Bass(p, new[] { "F-2", "G-2", "A-2", "A-2" }, new[] { "F-3", "G-3", "A-3", "A-3" }, false);
            Pad(p, new[] { "F-4", "G-4", "A-4", "A-4" }, new[] { "047", "047", "037", "037" }, Song.InstArp, 2, 9);
            for (int i = 0; i < 8; i++) Fx(p, 1, i * 8, $"H{0x0C - i * 3 / 2:X2}"); // ハイを閉じていく
            Fx(p, 1, 2, "D00");
            for (int bar = 0; bar < 2; bar++) foreach (int r in new[] { 0, 4, 8, 12 }) Drum(p, bar * 16 + r, 36);
            Drum(p, 32, 49);
            Lead(p, (0, "C-6", null), (8, "B-5", null), (16, "D-6", null), (24, "B-5", null), (32, "A-5", "446"), (56, "===", null));
            return p;
        }

        public static Song Build()
        {
            var s = Song.CreateDefault(64);
            s.title = s.name = "Neon Dash";
            s.bpm = 160;
            s.rowsPerBeat = 4;
            s.speed = 6;
            s.keyRoot = 9;
            s.scaleMode = ScaleMode.Minor;
            s.patterns.Clear();
            s.patterns.Add(Intro());                 // 0
            s.patterns.Add(Verse("Verse", false));   // 1
            s.patterns.Add(Verse("Verse 2", true));  // 2
            s.patterns.Add(Bridge());                // 3
            s.patterns.Add(Chorus("Chorus", false)); // 4
            s.patterns.Add(Chorus("Chorus 2", true));// 5
            s.patterns.Add(Outro());                 // 6
            s.order = new List<int> { 0, 1, 2, 3, 4, 5, 1, 2, 3, 4, 5, 6 };
            s.orderTranspose = new List<int>();
            s.Validate();
            return s;
        }

        /// <summary>1 回目：曲を作って保存し、Claude へのミックス依頼文を書き出す。</summary>
        public static void Step1()
        {
            try
            {
                var s = Build();
                AssetDatabase.DeleteAsset(AssetPath);
                AssetDatabase.CreateAsset(s, AssetPath);
                AssetDatabase.SaveAssets();
                File.WriteAllText(S + "mixmsg.txt", ClaudeMix.BuildUserMessage(s, "疾走感のあるチップチューン。サビでしっかりポンプさせ、リードは埋もれないように"));
                Debug.Log("SONG step1 ok");
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.Log("SONG error " + e); EditorApplication.Exit(1); }
        }

        /// <summary>2 回目：Claude の案 1 を適用して保存し、WAV を書き出す。</summary>
        public static void Step2()
        {
            try
            {
                var s = AssetDatabase.LoadAssetAtPath<Song>(AssetPath);
                string inner = File.ReadAllText(S + "mixout.json");
                string reply = "{\"type\":\"message\",\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":" + ClaudeClient.JsonString(inner) + "}]}";
                var list = ClaudeReply.Parse(reply, out string err);
                if (list == null) throw new System.Exception(err);
                string label = ClaudeMix.Apply(s, list[0]);
                Debug.Log($"SONG mix: {list[0].comment} [{label}]");
                EditorUtility.SetDirty(s);
                AssetDatabase.SaveAssets();
                UniDAWAssets.ExportWav(s, "Assets/UniDAW/Songs/NeonDash.wav");
                Debug.Log("SONG step2 ok");
                EditorApplication.Exit(0);
            }
            catch (System.Exception e) { Debug.Log("SONG error " + e); EditorApplication.Exit(1); }
        }
    }
}
