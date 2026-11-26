using System.Collections.Generic;

namespace UniDAW
{
    /// <summary>
    /// 追加のデモ曲（オリジナル）：雰囲気の違う 2 曲。どちらもマスターチェーンの見本を兼ねる。
    /// <list type="bullet">
    /// <item>Sunset Lo-Fi：84 BPM・ヘ長調のゆったりした曲。セブンスの分散和音、ノートディレイ（Gxx）のスウィング、
    /// レコードのノイズ風の確率トリガー、最後はテンポを落とすリタルダンド。</item>
    /// <item>Iron Fortress：168 BPM・7/8 拍子（14 行で 1 小節、2+2+3 のグルーヴ）・ニ短調（ハーモニックマイナー）のボス戦。
    /// 0xy の高速和音、ブレイクは 10 行で回る Pulse 2 のポリメーター、後半は順序表で半音上へ転調。</item>
    /// </list>
    /// </summary>
    public static partial class DemoSongs
    {
        // ================= Sunset Lo-Fi =================

        // 既定の 8 楽器の後ろ
        const int LfKeys = 8, LfFlute = 9, LfRim = 10, LfCrackle = 11;

        /// <summary>セブンスの分散和音（Pulse 2）：1 小節に 4 音、付点のリズム。</summary>
        static void LofiKeys(Pattern p, int bar, string[] notes, int vol)
        {
            int o = bar * 16;
            int[] rows = { 0, 3, 6, 10 };
            for (int i = 0; i < rows.Length; i++)
                Put(p, P2, o + rows[i], notes[i], bar == 0 && i == 0 ? LfKeys : -1, i == 0 ? vol : vol - 2);
        }

        /// <summary>ベース：1 拍目のルートを伸ばし、2 拍目の裏と 4 拍目で動く。最後は次のルートへ滑り込む。</summary>
        static void LofiBass(Pattern p, int bar, string root, string fifth, string approach)
        {
            int o = bar * 16;
            Put(p, Tri, o + 0, root, bar == 0 ? Song.InstBass : -1);
            Put(p, Tri, o + 6, root, -1, -1, "S03");
            Put(p, Tri, o + 10, fifth, -1, -1, "S04");
            Put(p, Tri, o + 14, approach, -1, -1, "302");
        }

        /// <summary>ゆるいビート：キックは 0 と 10、リムは 4 と 12。裏の 8 分ハットは G03 で遅らせてスウィングさせる。</summary>
        static void LofiDrums(Pattern p, int bar, bool full)
        {
            int o = bar * 16;
            Put(p, Nz, o + 0, Kick, Song.InstKick, 11);
            Put(p, Nz, o + 10, Kick, Song.InstKick, 8);
            Put(p, Nz, o + 4, "8-#", LfRim, 9);
            Put(p, Nz, o + 12, "8-#", LfRim, 10);
            foreach (int r in new[] { 2, 6, 14 })
                Put(p, Nz, o + r, Hat, Song.InstHat, 5, "G03", "V01");
            Put(p, Nz, o + 8, Hat, Song.InstHat, 6, "V01");
            if (full) Put(p, Nz, o + 15, Kick, Song.InstKick, 5, null, null, 50);
        }

        /// <summary>レコードのチリチリ：高いノイズをごく小さく、確率で鳴らす（ドラムの無い場所だけ）。</summary>
        static void Crackle(Pattern p, int from, int to)
        {
            for (int r = from; r < to; r++)
                if (!p.tracks[Nz].cells[r].HasNote)
                    Put(p, Nz, r, r % 3 == 0 ? "F-#" : "E-#", LfCrackle, 1 + r % 2, null, null, 30);
        }

        static readonly string[][] LofiVerseChords =
        {
            new[] { "A#3", "D-4", "F-4", "A-4" }, // B♭maj7
            new[] { "A-3", "C-4", "E-4", "G-4" }, // Am7
            new[] { "G-3", "A#3", "D-4", "F-4" }, // Gm7
            new[] { "F-3", "A-3", "C-4", "E-4" }, // Fmaj7
        };

        static void LofiVerseBed(Pattern p)
        {
            for (int bar = 0; bar < 4; bar++) LofiKeys(p, bar, LofiVerseChords[bar], 10);
            LofiBass(p, 0, "A#2", "F-2", "A-2");
            LofiBass(p, 1, "A-2", "E-2", "G-2");
            LofiBass(p, 2, "G-2", "D-2", "F-2");
            LofiBass(p, 3, "F-2", "C-3", "A#2");
            for (int bar = 0; bar < 4; bar++) LofiDrums(p, bar, bar % 2 == 1);
        }

        static Pattern LofiIntro()
        {
            var p = new Pattern(64, "Intro");
            for (int bar = 0; bar < 4; bar++) LofiKeys(p, bar, LofiVerseChords[bar], 8);
            // 後半からベースが入る
            LofiBass(p, 2, "G-2", "D-2", "F-2");
            LofiBass(p, 3, "F-2", "C-3", "A#2");
            Put(p, Tri, 32, "G-2", Song.InstBass);
            Put(p, Nz, 48, Kick, Song.InstKick, 9);
            Put(p, Nz, 60, "8-#", LfRim, 7);
            Crackle(p, 0, 64);
            // ハイを削った状態から開いていく（EQ のオートメーション）
            AddFx(p, P2, 0, "H06");
            AddFx(p, P2, 48, "H0C");
            return p;
        }

        static Pattern LofiVerse(bool answer)
        {
            var p = new Pattern(64, answer ? "Verse B" : "Verse A");
            LofiVerseBed(p);
            // フルートのような柔らかいリード。伸ばした音には遅れてビブラート、G01 で少し後ろに置く
            Phrase(p, P1, LfFlute, -1,
                "0:A-5 4:C-6:G01 6:A-5 8:F-5 12:.:423 14:G-5:G02 " +
                "16:A-5 20:.:424 24:G-5 26:E-5 28:C-5 30:D-5:G02 " +
                "32:F-5 36:D-5 38:F-5:G01 40:A#5 42:A-5 44:G-5 46:F-5");
            if (!answer)
                Phrase(p, P1, -1, -1, "48:E-5 52:.:424 56:G-5:G02 58:A-5 60:.:A01 63:===");
            else
                Phrase(p, P1, -1, -1, "48:A-5 50:G-5 52:E-5 54:C-5 56:F-5:304 60:.:425 62:.:A01");
            return p;
        }

        static Pattern LofiBridge()
        {
            var p = new Pattern(64, "Bridge");
            string[][] chords =
            {
                new[] { "G-3", "A#3", "D-4", "F-4" }, // Gm7
                new[] { "C-4", "E-4", "G-4", "A#4" }, // C7
                new[] { "A-3", "C-4", "E-4", "G-4" }, // Am7
                new[] { "D-4", "F-4", "A-4", "C-5" }, // Dm7
            };
            for (int bar = 0; bar < 4; bar++) LofiKeys(p, bar, chords[bar], 11);
            LofiBass(p, 0, "G-2", "D-2", "B-2");
            LofiBass(p, 1, "C-3", "G-2", "G#2");
            LofiBass(p, 2, "A-2", "E-2", "C#3");
            LofiBass(p, 3, "D-3", "A-2", "A#2");
            for (int bar = 0; bar < 4; bar++) LofiDrums(p, bar, true);
            Put(p, Nz, 30, OpenHat, Song.InstHat, 6, "G03");
            // 高めの音域で少し動きを増やす。最後は 1xx でゆっくりせり上がる
            Phrase(p, P1, LfFlute, -1,
                "0:D-6 4:A#5 6:D-6:G01 8:F-6 12:E-6 14:D-6:G02 " +
                "16:E-6 20:.:424 24:C-6 28:A#5 30:C-6:G02 " +
                "32:C-6 34:E-6 36:G-6 40:.:425 44:E-6 46:C-6 " +
                "48:D-6 52:.:A01 56:A-5 58:.:101 63:===");
            // Pulse 1 のエコー役は付けず、DUCK を少し深くしてキックで揺らす
            AddFx(p, P2, 0, "D50");
            AddFx(p, P2, 48, "D33");
            return p;
        }

        static Pattern LofiOutro()
        {
            var p = new Pattern(64, "Outro");
            // B♭maj7 → Fmaj7 を伸ばしながらテンポを落とす
            LofiKeys(p, 0, LofiVerseChords[0], 9);
            LofiKeys(p, 1, LofiVerseChords[3], 8);
            Put(p, P2, 32, "F-3", -1, 7, "04B");
            Put(p, P2, 48, "F-3", -1, 4, "04B", "A01");
            Phrase(p, P1, LfFlute, -1, "0:A-5 4:G-5 8:F-5 16:E-5:424 28:C-5 32:F-5:425 44:.:A01 56:===");
            LofiBass(p, 0, "A#2", "F-2", "A-2");
            Phrase(p, Tri, -1, -1, "16:F-2 32:F-2 48:===");
            LofiDrums(p, 0, false);
            Put(p, Nz, 16, Kick, Song.InstKick, 9);
            Crackle(p, 17, 64);
            AddFx(p, Nz, 0, "F54");
            AddFx(p, Nz, 32, "F48");
            AddFx(p, Nz, 40, "F40");
            AddFx(p, Nz, 48, "F38");
            AddFx(p, P2, 32, "H06");
            return p;
        }

        public static Song BuildLofiSong()
        {
            var s = Song.CreateDefault(64);
            s.title = "Sunset Lo-Fi";
            s.name = "Sunset Lo-Fi";
            s.bpm = 84;
            s.rowsPerBeat = 4;
            s.speed = 6;
            s.keyRoot = 5;
            s.scaleMode = ScaleMode.Major;

            s.instruments.Add(new Instrument("Keys", "11 9 8 7 6 5 5 4 4 3 3 2 2 1 1 0", "", "2"));
            s.instruments.Add(new Instrument("Flute", "5 7 9 10 10 | 10 / 8 6 4 2 0", "", "2 2 2 1 | 2"));
            s.instruments.Add(new Instrument("Rim", "12 7 3 1 0", "", "1"));
            s.instruments.Add(new Instrument("Crackle", "6 2 0", "", "1"));

            s.patterns.Clear();
            s.patterns.Add(LofiIntro());       // 0
            s.patterns.Add(LofiVerse(false));  // 1
            s.patterns.Add(LofiVerse(true));   // 2
            s.patterns.Add(LofiBridge());      // 3
            s.patterns.Add(LofiOutro());       // 4
            s.order = new List<int> { 0, 1, 2, 3, 1, 2, 4 };
            s.orderTranspose = new List<int> { 0, 0, 0, 0, 0, 0, 0 };

            var m = s.mixer;
            m[P1].volumeDb = -3f; m[P1].pan = -0.12f; m[P1].delaySend = 0.25f; m[P1].reverbSend = 0.35f; m[P1].eqHighDb = -3f;
            m[P2].volumeDb = 2f; m[P2].pan = 0.35f; m[P2].reverbSend = 0.4f; m[P2].eqHighDb = -4f; m[P2].duck = 0.2f;
            m[Tri].volumeDb = -1f; m[Tri].eqLowDb = 2f;
            m[Nz].volumeDb = -4f; m[Nz].eqHighDb = -3f; m[Nz].reverbSend = 0.1f;
            s.effects.delayBeats = 0.75f;
            s.effects.delayFeedback = 0.35f;
            s.effects.delayPingPong = true;
            s.effects.reverbSize = 0.7f;
            s.effects.sidechainSource = Nz;
            s.effects.duckRelease = 0.45f;

            // マスター：ハイを丸めて少し広げ、軽いコンプでまとめる。目標は控えめな -16 LUFS
            var mc = s.master;
            mc.enabled = true;
            mc.eqLowDb = 1f;
            mc.eqHighDb = -2.5f;
            mc.width = 1.15f;
            mc.compThresholdDb = -22f;
            mc.compRatio = 2f;
            mc.limiter = true;
            mc.ceilingDb = -1f;
            mc.targetLufs = -16f;

            s.Validate();
            return s;
        }

        // ================= Iron Fortress（7/8） =================

        const int IfLead = 8, IfStab = 9, IfTriKick = 10, IfClank = 11;
        /// <summary>7/8 の 1 小節（16 分 × 14）。</summary>
        const int Bar78 = 14;

        /// <summary>2+2+3 のベース：グループの頭にルート、最後のグループで 5 度と半音の経過音。</summary>
        static void FortressBass(Pattern p, int bar, string root, string oct, string fifth, string pass)
        {
            int o = bar * Bar78;
            Put(p, Tri, o + 0, root, bar == 0 ? Song.InstBass : -1);
            Put(p, Tri, o + 2, root, -1, -1, "S03");
            Put(p, Tri, o + 4, root);
            Put(p, Tri, o + 6, oct, -1, -1, "S02");
            Put(p, Tri, o + 8, root);
            Put(p, Tri, o + 10, fifth, -1, -1, "S03");
            Put(p, Tri, o + 12, pass, -1, -1, "S03");
        }

        /// <summary>2+2+3 のドラム：キックはグループの頭（最後のグループは 2 発）、スネアは 4 と 12。裏は確率のハット。</summary>
        static void FortressDrums(Pattern p, int bar, bool crash)
        {
            int o = bar * Bar78;
            Put(p, Nz, o + 0, crash ? Crash : Kick, crash ? Song.InstCrash : Song.InstKick);
            Put(p, Nz, o + 8, Kick, Song.InstKick);
            Put(p, Nz, o + 10, Kick, Song.InstKick, 11);
            Put(p, Nz, o + 4, Snare, Song.InstSnare);
            Put(p, Nz, o + 12, Snare, Song.InstSnare, 13);
            foreach (int r in new[] { 2, 6 })
                Put(p, Nz, o + r, Hat, Song.InstHat, 8, "V01");
            foreach (int r in new[] { 1, 3, 5, 7, 9, 11, 13 })
                Put(p, Nz, o + r, Hat, Song.InstHat, 4, "V01", null, 55);
        }

        /// <summary>Pulse 2：グループの頭で 0xy の高速和音を短く刻む。</summary>
        static void FortressStabs(Pattern p, int bar, string root, string arp)
        {
            int o = bar * Bar78;
            foreach (int r in new[] { 0, 4, 8, 11 })
                Put(p, P2, o + r, root, bar == 0 && r == 0 ? IfStab : -1, r == 0 ? 12 : 9, arp, r == 11 ? "S03" : null);
        }

        // 小節ごとのコード（ルート / 0xy）
        static readonly (string root, string arp, string bass, string oct, string fifth, string pass)[] FortressA =
        {
            ("D-4", "037", "D-3", "D-4", "A-2", "C#3"),  // Dm
            ("A#3", "047", "A#2", "A#3", "F-2", "A-2"),  // B♭
            ("G-3", "037", "G-2", "G-3", "D-3", "G#2"),  // Gm
            ("A-3", "047", "A-2", "A-3", "E-3", "C#3"),  // A
        };

        static readonly (string root, string arp, string bass, string oct, string fifth, string pass)[] FortressB =
        {
            ("D#4", "047", "D#3", "D#4", "A#2", "D-3"),  // E♭（ナポリの II）
            ("D-4", "037", "D-3", "D-4", "A-2", "C#3"),  // Dm
            ("C#4", "036", "C#3", "C#4", "G-2", "A#2"),  // C#dim
            ("A-3", "047", "A-2", "A-3", "E-3", "C#3"),  // A
        };

        static void FortressBed(Pattern p, (string root, string arp, string bass, string oct, string fifth, string pass)[] chords)
        {
            for (int bar = 0; bar < 4; bar++)
            {
                var c = chords[bar];
                FortressStabs(p, bar, c.root, c.arp);
                FortressBass(p, bar, c.bass, c.oct, c.fifth, c.pass);
                FortressDrums(p, bar, bar == 0);
            }
        }

        static Pattern FortressIntro()
        {
            var p = new Pattern(Bar78 * 4, "Intro");
            for (int bar = 0; bar < 4; bar++)
            {
                FortressBass(p, bar, "D-3", "D-4", "A-2", bar == 3 ? "C#3" : "D#3");
                if (bar >= 2) FortressDrums(p, bar, bar == 2);
            }
            // 最初の 2 小節は金属音だけでグループの頭を示す
            foreach (int r in new[] { 0, 4, 8, 14, 18, 22 })
                Put(p, Nz, r, "6-#", IfClank, r % Bar78 == 0 ? 13 : 9);
            // Pulse 2：小さな音から 1xx でせり上がるライザー
            Put(p, P2, 28, "D-3", IfStab, 3, "102", "A20");
            AddFx(p, P2, 55, "S02");
            return p;
        }

        static Pattern FortressThemeA()
        {
            var p = new Pattern(Bar78 * 4, "Theme A");
            FortressBed(p, FortressA);
            Phrase(p, P1, IfLead, -1,
                "0:D-5 4:F-5 8:A-5 10:G-5 12:A-5 " +
                "14:A#5:445 20:.:A01 22:A-5 24:G-5 26:F-5 " +
                "28:G-5 32:A#5 36:D-6 38:C-6 40:A#5 " +
                "42:C#6:446 48:E-6 50:D-6 52:C#6 54:A-5:S04");
            return p;
        }

        static Pattern FortressThemeB()
        {
            var p = new Pattern(Bar78 * 4, "Theme B");
            FortressBed(p, FortressB);
            Phrase(p, P1, IfLead, -1,
                "0:G-5 2:A#5 4:D#6 8:D-6 10:C-6 12:A#5 " +
                "14:A-5:445 22:F-5 24:A-5 26:D-6 " +
                "28:E-6 32:G-6 36:E-6 38:C#6 40:A#5:Q31 " +
                "42:A-5:448 50:.:A02 54:===");
            // 最後の小節はタムを下降させて次のセクションへ
            for (int r = 42; r < 56; r++) p.tracks[Nz].cells[r] = Cell.Blank;
            Put(p, Nz, 42, Kick, Song.InstKick);
            foreach (var (r, n) in new[] { (46, "8-#"), (48, "7-#"), (50, "6-#"), (52, "5-#") })
                Put(p, Nz, r, n, IfClank, 12);
            for (int r = 53; r < 56; r++) Put(p, Nz, r, Snare, Song.InstSnare, 10 + (r - 53) * 2);
            return p;
        }

        static Pattern FortressBreak()
        {
            var p = new Pattern(Bar78 * 4, "Break");
            // Pulse 2：10 行で回るポリメーター。14 行の小節と少しずつずれていく
            p.tracks[P2].length = 10;
            Put(p, P2, 0, "D-5", IfStab, 8, "036");
            Put(p, P2, 3, "A-4", -1, 5);
            Put(p, P2, 6, "G#4", -1, 6, "036");
            Put(p, P2, 8, "F-4", -1, 4, "S02");
            // Pulse 1：三全音（D → G#）を伸ばして深いビブラート、ポルタメントで戻る
            Phrase(p, P1, IfLead, 10, "0:D-5:434 14:G#5:436 28:A-5:303 34:.:448 42:C#5 48:D-5:A02 55:===");
            // Triangle：長いルートとトライアングルキック
            Phrase(p, Tri, Song.InstBass, -1, "0:D-3 14:D-3 28:A#2 42:A-2");
            foreach (int r in new[] { 8, 22, 36, 50 }) Put(p, Tri, r, "C-4", IfTriKick);
            // Noise：ハーフタイム（スネアは 2 小節目の頭ではなく各小節 8 行目）
            for (int bar = 0; bar < 4; bar++)
            {
                int o = bar * Bar78;
                Put(p, Nz, o + 8, Snare, Song.InstSnare, 12);
                Put(p, Nz, o + 4, Hat, Song.InstHat, 5, "V01", null, 60);
                Put(p, Nz, o + 12, Hat, Song.InstHat, 5, "V01", null, 60);
            }
            AddFx(p, P2, 0, "D20"); // ダッキングを弱めて空気を残す
            AddFx(p, Tri, 0, "L10");
            AddFx(p, Tri, 42, "L0C");
            return p;
        }

        static Pattern FortressEnd()
        {
            var p = new Pattern(Bar78 * 2, "End");
            // 全員でルートを叩いて伸ばし、消えていく
            Put(p, P1, 0, "D-6", IfLead, 15, "446", "A02");
            Put(p, P2, 0, "D-4", IfStab, 12, "037");
            Put(p, Tri, 0, "D-3", Song.InstBass);
            Put(p, Nz, 0, Crash, Song.InstCrash);
            Put(p, Tri, 20, "===");
            Put(p, P2, 4, "D-4", -1, 6, "037", "A02");
            return p;
        }

        public static Song BuildFortressSong()
        {
            var s = Song.CreateDefault(Bar78 * 4);
            s.title = "Iron Fortress";
            s.name = "Iron Fortress";
            s.bpm = 168;
            s.rowsPerBeat = 4;
            s.speed = 6;
            s.keyRoot = 2;
            s.scaleMode = ScaleMode.HarmonicMinor;

            s.instruments.Add(new Instrument("Fortress Lead", "15 14 13 12 12 11 | 11 / 8 5 2 0", "", "1 1 0 0 1 1 2 2 | 1 1 2 2"));
            s.instruments.Add(new Instrument("Iron Stab", "15 11 8 6 4 2 0", "", "0"));
            s.instruments.Add(new Instrument("Tri Kick", "15 15 15 15 0", "12 6 0 -6 -12 -18", ""));
            s.instruments.Add(new Instrument("Clank", "15 11 8 6 4 3 2 1 0", "0 2 -2 1 -1", "1"));

            s.patterns.Clear();
            s.patterns.Add(FortressIntro());   // 0
            s.patterns.Add(FortressThemeA());  // 1
            s.patterns.Add(FortressThemeB());  // 2
            s.patterns.Add(FortressBreak());   // 3
            s.patterns.Add(FortressEnd());     // 4
            // 2 周目は半音上（E♭m）へ転調して追い込む（Noise は移調されない）
            s.order = new List<int> { 0, 1, 2, 1, 3, 1, 2, 4 };
            s.orderTranspose = new List<int> { 0, 0, 0, 0, 0, 1, 1, 1 };

            var m = s.mixer;
            m[P1].volumeDb = -1f; m[P1].pan = -0.25f; m[P1].delaySend = 0.15f; m[P1].reverbSend = 0.15f;
            m[P2].volumeDb = 3f; m[P2].pan = 0.45f; m[P2].duck = 0.35f; m[P2].eqLowDb = -4f; m[P2].reverbSend = 0.2f;
            m[Tri].volumeDb = -1f; m[Tri].eqLowDb = 3f; m[Tri].duck = 0.2f;
            m[Nz].volumeDb = -3f; m[Nz].eqHighDb = 1f; m[Nz].reverbSend = 0.05f;
            s.effects.delayBeats = 0.5f;
            s.effects.delayFeedback = 0.25f;
            s.effects.delayPingPong = true;
            s.effects.reverbSize = 0.4f;
            s.effects.sidechainSource = Nz;
            s.effects.duckRelease = 0.2f;

            // マスター：締まったローと少し明るいハイ、強めのコンプで密度を出す。目標は大きめの -11 LUFS
            var mc = s.master;
            mc.enabled = true;
            mc.eqLowDb = 1f;
            mc.eqMidDb = -1f;
            mc.eqHighDb = 1.5f;
            mc.width = 1.1f;
            mc.compThresholdDb = -16f;
            mc.compRatio = 3f;
            mc.limiter = true;
            mc.ceilingDb = -0.5f;
            mc.targetLufs = -11f;

            s.Validate();
            return s;
        }

        /// <summary>
        /// 開発用：追加のデモ曲をラウドネス合わせしてから Samples~/Demo に書き出す。
        /// -executeMethod UniDAW.DemoSongs.WriteMoreSamples
        /// </summary>
        public static void WriteMoreSamples()
        {
            const string tmp = "Assets/_DemoGen";
            const string dst = "Packages/com.morilab.unidaw/Samples~/Demo";
            int code = 0;
            try
            {
                foreach (var (song, file) in new[] { (BuildLofiSong(), "SunsetLoFi"), (BuildFortressSong(), "IronFortress") })
                {
                    var matched = MasterAnalysis.MatchLoudness(song, song.master, out var st);
                    if (matched != null) song.master.gainDb = matched.gainDb;
                    UnityEngine.Debug.Log($"[DemoGen] {file}: {LoudnessMeter.Format(st.integratedLufs)} LUFS, gain {song.master.gainDb:0.0}\n{MasterAnalysis.Analyze(song).Text()}");
                    string path = $"{tmp}/{file}.asset";
                    UniDAW.Editor.UniDAWAssets.SaveSong(song, path);
                    UnityEditor.AssetDatabase.SaveAssets();
                    System.IO.File.Copy(path, $"{dst}/{file}.asset", true);
                    System.IO.File.Copy(path + ".meta", $"{dst}/{file}.asset.meta", true);
                }
                UnityEditor.AssetDatabase.DeleteAsset(tmp);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogException(ex);
                code = 1;
            }
            UnityEditor.EditorApplication.Exit(code);
        }
    }
}
