using System;
using System.Collections.Generic;
using UniDAW.Midi;

namespace UniDAW
{
    /// <summary>
    /// テスト用のデモ曲（オリジナル）とデモ MIDI の生成。Samples~/Demo のアセットはこれで作ったもの。
    /// デモ曲は機能の見本も兼ねる：楽器マクロ（PWM・トライアングルキック）、行エフェクト（アルペジオ・ビブラート・
    /// ポルタメント・ノートスライド・スライド・音量スライド・ノートカット・ノートディレイ・デューティ）、
    /// ミキサーのオートメーション（EQ・ダッキング）、テンポ / スピード（シャッフル・リタルダンド）、
    /// 確率・条件トリガー、トラック長の違うポリメーター、順序表の移調（転調）。
    /// </summary>
    public static partial class DemoSongs
    {
        const int P1 = 0, P2 = 1, Tri = 2, Nz = 3;

        // 既定の 8 楽器の後ろに追加する楽器
        const int InstPwm = 8, InstPluck = 9, InstEcho = 10, InstTriKick = 11, InstOpenHat = 12, InstTom = 13, InstStab = 14;

        // ノイズの音程（数字が大きいほど明るい）
        const string Kick = "3-#", Snare = "9-#", Hat = "E-#", OpenHat = "D-#", Crash = "C-#";

        static void Put(Pattern p, int ch, int row, string note, int inst = -1, int vol = -1, string fx1 = null, string fx2 = null, int prob = 100)
        {
            if (!NoteText.TryParse(note, Song.Kinds[ch], out int n)) throw new ArgumentException("note: " + note);
            var c = Cell.Blank;
            c.note = n;
            c.instrument = inst;
            c.volume = vol;
            c.fx1 = ParseFx(fx1);
            c.fx2 = ParseFx(fx2);
            c.probability = prob;
            p.tracks[ch].cells[row] = c;
        }

        static Fx ParseFx(string s)
        {
            if (s == null) return Fx.None;
            if (!FxText.TryParse(s, out var fx)) throw new ArgumentException("fx: " + s);
            return fx;
        }

        /// <summary>既存のセルの空いているエフェクト欄に足す（ノートの無い行なら発音中の音に効く）。</summary>
        static void AddFx(Pattern p, int ch, int row, string fx)
        {
            ref var c = ref p.tracks[ch].cells[row];
            var f = ParseFx(fx);
            if (c.fx1.IsEmpty) c.fx1 = f;
            else if (c.fx2.IsEmpty) c.fx2 = f;
            else throw new ArgumentException($"fx full: ch{ch} row{row}");
        }

        /// <summary>
        /// "行:ノート[:fx1[:fx2]]" をスペース区切りで並べたフレーズ。ノートが "." ならエフェクトだけ。
        /// 楽器は最初のノートにだけ書く（以降は引き継ぐ）。
        /// </summary>
        static void Phrase(Pattern p, int ch, int inst, int vol, string text)
        {
            bool first = true;
            foreach (var tok in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var a = tok.Split(':');
                int row = int.Parse(a[0]);
                string f1 = a.Length > 2 ? a[2] : null, f2 = a.Length > 3 ? a[3] : null;
                if (a[1] == ".")
                {
                    if (f1 != null) AddFx(p, ch, row, f1);
                    if (f2 != null) AddFx(p, ch, row, f2);
                    continue;
                }
                bool off = a[1] == "===";
                Put(p, ch, row, a[1], first && !off ? inst : -1, off ? -1 : vol, f1, f2);
                if (!off) first = false;
            }
        }

        /// <summary>メロディを <paramref name="delay"/> 行遅れで別チャンネルに写す（音色を変えた小さなエコー）。</summary>
        static void EchoOf(Pattern p, int from, int to, int delay, int inst, int vol)
        {
            var src = p.tracks[from].cells;
            var dst = p.tracks[to].cells;
            bool first = true;
            for (int r = 0; r + delay < dst.Length && r < src.Length; r++)
            {
                var c = src[r];
                if (!c.HasNote && !c.IsOff) continue;
                var e = Cell.Blank;
                e.note = c.note;
                if (c.HasNote)
                {
                    e.instrument = first ? inst : -1;
                    e.volume = vol;
                    first = false;
                }
                dst[r + delay] = e;
            }
        }

        // ---- ドラム ----

        /// <summary>
        /// 基本の 8 ビート。キック 0/10、スネア 4/12、クローズドハットはショートモード（V01）で金属的に、
        /// 裏の 16 分は確率で揺らし、7 行目はゴーストスネア、15 行目のスネアは G02 で後ろへ溜める。
        /// </summary>
        static void Groove(Pattern p, int bar, bool laidBack)
        {
            int o = bar * 16;
            Put(p, Nz, o + 0, Kick, Song.InstKick);
            Put(p, Nz, o + 10, Kick, Song.InstKick, 12);
            Put(p, Nz, o + 4, Snare, Song.InstSnare);
            Put(p, Nz, o + 12, Snare, Song.InstSnare);
            Put(p, Nz, o + 7, Snare, Song.InstSnare, 4, null, null, 40);
            foreach (int r in new[] { 2, 6, 8 })
                Put(p, Nz, o + r, Hat, Song.InstHat, r == 8 ? 9 : 7, "V01");
            foreach (int r in new[] { 3, 9, 13 })
                Put(p, Nz, o + r, Hat, Song.InstHat, 4, "V01", null, 35);
            Put(p, Nz, o + 14, OpenHat, InstOpenHat, 8, "S04"); // 開いたハットを 4 tick で切る
            if (laidBack) Put(p, Nz, o + 15, Snare, Song.InstSnare, 7, "G02");
        }

        /// <summary>小節末のフィル：タムを下降させ、最後はスネアの 16 分で音量を上げる。</summary>
        static void TomFill(Pattern p, int bar)
        {
            int o = bar * 16;
            Put(p, Nz, o + 8, "7-#", InstTom, 13);
            Put(p, Nz, o + 10, "6-#", InstTom, 13);
            Put(p, Nz, o + 11, "6-#", InstTom, 9);
            Put(p, Nz, o + 12, "5-#", InstTom, 14);
            for (int i = 0; i < 3; i++) Put(p, Nz, o + 13 + i, Snare, Song.InstSnare, 9 + i * 3);
        }

        // ---- ベース（Triangle） ----

        /// <summary>シンコペーションのオクターブベース。裏の高い音はノートカットで短く、小節末は次のコードへの経過音。</summary>
        static void VerseBass(Pattern p, int bar, string low, string high, string approach)
        {
            int o = bar * 16;
            Put(p, Tri, o + 0, low, bar == 0 ? Song.InstBass : -1);
            Put(p, Tri, o + 3, low, -1, -1, "S03");
            Put(p, Tri, o + 6, high, -1, -1, "S02");
            Put(p, Tri, o + 8, low);
            Put(p, Tri, o + 10, high, -1, -1, "S02");
            Put(p, Tri, o + 12, low);
            Put(p, Tri, o + 14, approach, -1, -1, "S04");
        }

        /// <summary>トライアングルキック（楽器の Arp マクロで音程を急降下）とベースを 1 チャンネルで交互に鳴らす。</summary>
        static void ChorusBass(Pattern p, int bar, string low, string high)
        {
            int o = bar * 16;
            foreach (int r in new[] { 0, 8 })
                Put(p, Tri, o + r, "C-4", InstTriKick);
            foreach (int r in new[] { 2, 4, 6, 10, 12, 14 })
            {
                bool hi = r % 4 == 2;
                Put(p, Tri, o + r, hi ? high : low, Song.InstBass, -1, hi ? "S03" : null);
            }
        }

        // ---- パターン ----

        static Pattern Intro()
        {
            var p = new Pattern(64, "Intro");
            // 曲頭でテンポとスピードを戻す（アウトロのリタルダンドから戻ってきたとき用）
            AddFx(p, P1, 0, "F8C");
            AddFx(p, P1, 0, "F06");

            // Pulse2：分散和音の 16 分。強弱を交互に付け、Hxx で高域を少しずつ開ける
            string[][] arps =
            {
                new[] { "C-5", "G-4", "D#5", "G-4" },
                new[] { "C-5", "G#4", "D#5", "G#4" },
                new[] { "A#4", "G-4", "D#5", "G-4" },
                new[] { "D-5", "A#4", "F-5", "A#4" },
            };
            string[] open = { "H00", "H04", "H08", "H0C" };
            for (int bar = 0; bar < 4; bar++)
                for (int i = 0; i < 16; i++)
                {
                    int r = bar * 16 + i;
                    Put(p, P2, r, arps[bar][i % 4], r == 0 ? InstPluck : -1, i % 4 == 0 ? 11 : (i % 2 == 0 ? 8 : 5), null, i == 0 ? open[bar] : null);
                }

            // Triangle：1 小節ずつのペダル、最後は半音で次へ
            Phrase(p, Tri, Song.InstBass, -1, "0:C-3 16:G#2 32:D#3 48:A#2 60:B-2");

            // Noise：前半は確率ハットだけ、後半から四つ打ち、最後にスネアロール
            for (int r = 0; r < 32; r += 2)
                Put(p, Nz, r, Hat, Song.InstHat, r % 4 == 0 ? 8 : 5, "V01", null, r % 4 == 0 ? 100 : 60);
            for (int r = 32; r < 60; r += 4)
            {
                Put(p, Nz, r, Kick, Song.InstKick, 13);
                Put(p, Nz, r + 2, Hat, Song.InstHat, 7, "V01");
            }
            for (int i = 0; i < 4; i++) Put(p, Nz, 60 + i, Snare, Song.InstSnare, 7 + i * 2);

            // Pulse1：最後の小節でリードのピックアップ（Qxy で最後の音を持ち上げる）
            Phrase(p, P1, InstPwm, 9, "52:G-4 54:A#4 56:C-5 58:D-5 60:D#5 62:F-5:Q42");
            return p;
        }

        /// <summary>Verse のリード。前半 2 小節は共通、後半は A / B で答えを変える。</summary>
        static void VerseLead(Pattern p, bool answer)
        {
            // Cm：A5 → A#5 は Qxy の装飾音
            Phrase(p, P1, InstPwm, -1, "0:G-5 6:D#5 8:F-5 10:G-5 12:A-5:Q41 14:G-5");
            // Ab：伸ばした音に後からビブラートを足す（ディレイドビブラート）
            Phrase(p, P1, -1, -1, "16:C-6 20:.:445 22:A#5 24:G#5 26:G-5 28:F-5 30:G-5:S03");
            if (!answer)
            {
                // Eb → Bb：3xx で F5 から D5 へ滑り込み、最後は音量スライドで消える
                Phrase(p, P1, -1, -1, "32:G-5 35:A#5 38:G-5 40:D#5 44:F-5 46:G-5 48:F-5 52:.:434 56:F-5 58:D-5:306 60:D#5:A02 63:===");
            }
            else
            {
                Phrase(p, P1, -1, -1, "32:A#5 34:G-5 36:A#5 38:B-5:Q21 40:C-6 42:.:446 44:A#5 46:G-5 48:F-5 50:G-5 52:A#5:V01 54:A-5:V02 56:F-5 60:G-5:A02 63:===");
            }
        }

        static Pattern Verse(bool b)
        {
            var p = new Pattern(64, b ? "Verse B" : "Verse A");
            VerseLead(p, b);

            if (!b)
            {
                // Pulse2：リードを 3 行遅れで写したエコー（25% デューティ・小音量）
                EchoOf(p, P1, P2, 3, InstEcho, 6);
            }
            else
            {
                // Pulse2：裏拍のコードスタブ。0xy で和音を 1 音で鳴らし、転回形で声部を近く保つ
                string[] root = { "C-5", "C-5", "A#4", "A#4" };
                string[] arp = { "037", "038", "059", "047" };
                for (int bar = 0; bar < 4; bar++)
                    foreach (int r in new[] { 2, 6, 10, 14 })
                        Put(p, P2, bar * 16 + r, root[bar], bar == 0 && r == 2 ? InstStab : -1, r == 6 || r == 14 ? 9 : 6, arp[bar], r == 10 ? "S02" : null);
            }

            VerseBass(p, 0, "C-3", "C-4", "G-2");
            VerseBass(p, 1, "G#2", "G#3", "D#3");
            VerseBass(p, 2, "D#3", "D#4", "A#2");
            VerseBass(p, 3, "A#2", "A#3", b ? "G-2" : "B-2");

            for (int bar = 0; bar < 4; bar++) Groove(p, bar, bar == 1);
            if (b)
            {
                // Verse B：頭はキックの代わりにクラッシュ、最後の小節はタムフィル
                Put(p, Nz, 0, Crash, Song.InstCrash);
                for (int r = 56; r < 64; r++) p.tracks[Nz].cells[r] = Cell.Blank;
                TomFill(p, 3);
            }
            else
            {
                // 2 周に 1 回だけ鳴るフィル（条件トリガー 2:2）
                var c = Cell.NoteCell(9, Song.InstSnare, 13);
                c.condA = 2;
                c.condB = 2;
                p.tracks[Nz].cells[63] = c;
            }
            return p;
        }

        static Pattern Build()
        {
            var p = new Pattern(32, "Build");
            // Pulse1：同じ音を伸ばし、ビブラートを段々深くする。シャッフル（F07/F05）もここで戻す
            Phrase(p, P1, InstPwm, 11, "0:C-6:441:F06 8:.:443 16:D-6:444 24:.:446 28:.:A20 31:===");
            // Pulse2：小音量から 1xx でせり上がるライザー、Axy で音量も上げる
            Put(p, P2, 0, "G#3", InstPwm, 3, "101", "A20");
            AddFx(p, P2, 31, "S03");
            // Triangle：最初は EQ でローを削って軽く、8 分 → 16 分と詰めて最後は 2xx で急降下
            for (int r = 0; r < 16; r += 2) Put(p, Tri, r, "G#2", r == 0 ? Song.InstBass : -1, -1, "S03", r == 0 ? "L06" : null);
            for (int r = 16; r < 28; r++) Put(p, Tri, r, "A#2", -1, -1, "S02");
            Put(p, Tri, 28, "A#3", -1, -1, "220");
            // Noise：4 分 → 8 分 → 16 分のスネアロール（頭の 2 拍はキック）。音量のパラメータロックで段々大きく
            for (int r = 0; r < 32; r++)
            {
                int step = r < 16 ? 4 : (r < 24 ? 2 : 1);
                if (r % step != 0) continue;
                if (r == 0 || r == 8) Put(p, Nz, r, Kick, Song.InstKick);
                else Put(p, Nz, r, Snare, Song.InstSnare, 5 + r * 10 / 31);
            }
            return p;
        }

        static Pattern Chorus()
        {
            var p = new Pattern(64, "Chorus");
            // Ab → Bb → G → Cm（G はナチュラルの B を使うドミナント）
            Phrase(p, P1, InstPwm, -1,
                "0:C-6 4:D#6 6:D-6 8:C-6 10:A#5 12:C-6 14:.:435 " +
                "16:D-6 20:F-6 22:D#6 24:D-6 26:A#5 28:D-6:S04 30:D#6 " +
                "32:D-6 36:B-5 38:C-6 40:D-6 44:F-6:Q42 " +
                "48:D#6:446 56:D-6 58:C-6:304 60:.:A01 62:===");

            // Pulse2：コードの高速アルペジオ。強いダッキング（DC0）でキックに合わせて揺らす
            string[] root = { "G#4", "A#4", "G-4", "G-4" };
            string[] arp = { "047", "047", "047", "058" };
            for (int bar = 0; bar < 4; bar++)
                for (int r = 0; r < 16; r += 4)
                {
                    int row = bar * 16 + r;
                    string a = arp[bar];
                    string n = root[bar];
                    if (row == 60) { n = "C-5"; a = "03A"; } // 最後だけ Cm7
                    Put(p, P2, row, n, row == 0 ? InstStab : -1, r == 0 ? 11 : 7, a, row == 0 ? "DC0" : null);
                }

            ChorusBass(p, 0, "G#2", "G#3");
            ChorusBass(p, 1, "A#2", "A#3");
            ChorusBass(p, 2, "G-2", "G-3");
            ChorusBass(p, 3, "C-3", "C-4");
            AddFx(p, Tri, 0, "L0C"); // Build で削ったローを戻す

            // Noise：キックはトライアングルキックと重ねる。16 分の裏ハットは確率、7 行目はゴースト
            for (int bar = 0; bar < 4; bar++)
            {
                int o = bar * 16;
                Put(p, Nz, o, bar == 0 ? Crash : Kick, bar == 0 ? Song.InstCrash : Song.InstKick);
                Put(p, Nz, o + 8, Kick, Song.InstKick, 13);
                Put(p, Nz, o + 4, Snare, Song.InstSnare);
                Put(p, Nz, o + 12, Snare, Song.InstSnare);
                Put(p, Nz, o + 7, Snare, Song.InstSnare, 4, null, null, 40);
                foreach (int r in new[] { 1, 3, 5, 9, 11, 13, 15 })
                    Put(p, Nz, o + r, Hat, Song.InstHat, r % 4 == 1 ? 7 : 4, "V01", null, r % 4 == 1 ? 100 : 70);
                Put(p, Nz, o + 2, OpenHat, InstOpenHat, 6, "S03");
                Put(p, Nz, o + 10, OpenHat, InstOpenHat, 6, "S03");
                Put(p, Nz, o + 14, OpenHat, InstOpenHat, 8);
            }
            for (int r = 56; r < 64; r++) Put(p, Nz, r, Snare, Song.InstSnare, 6 + (r - 56), null, null, r % 2 == 0 ? 100 : 80);
            return p;
        }

        static Pattern Breakdown()
        {
            var p = new Pattern(64, "Breakdown");
            // Pulse2：12 行で回るポリメーター（16 行の小節とずれていく）。偶数行 F07 / 奇数行 F05 でシャッフル
            p.tracks[P2].length = 12;
            for (int r = 0; r < 12; r++) Put(p, P2, r, "---", -1, -1, null, r % 2 == 0 ? "F07" : "F05");
            Put(p, P2, 0, "C-5", InstEcho, 9, "D20", "F07"); // ダッキングを弱めて空気を残す
            Put(p, P2, 3, "G-4", -1, 6, null, "F05");
            Put(p, P2, 5, "D#5", -1, 7, null, "F05");
            Put(p, P2, 8, "A#4", -1, 6, null, "F07");
            Put(p, P2, 10, "G-5", -1, 4, "V02", "F07");

            // Pulse1：まばらな長い音。ゆっくり深いビブラートと、ポルタメントでのつなぎ
            Phrase(p, P1, InstPwm, 10, "0:G-5:423 14:F-5 16:D#5 28:C-5:308 32:A#4:423 40:C-5 44:D#5 48:D-5:435 56:.:A01 60:===");

            // Triangle：全音符のルート。Lxx でローを持ち上げる
            Phrase(p, Tri, Song.InstBass, -1, "0:C-3:L12 16:G#2 32:D#3 48:A#2 56:F-2 60:G-2");

            // Noise：ハーフタイム（スネアは 8 行目）。ハットは確率で間引き、最後の小節はキックだけ
            for (int bar = 0; bar < 4; bar++)
            {
                int o = bar * 16;
                Put(p, Nz, o, Kick, Song.InstKick);
                if (bar < 3)
                {
                    Put(p, Nz, o + 8, Snare, Song.InstSnare, 12, "G01");
                    Put(p, Nz, o + 11, Kick, Song.InstKick, 9, null, null, 50);
                    foreach (int r in new[] { 4, 12, 14 })
                        Put(p, Nz, o + r, Hat, Song.InstHat, 5, "V01", null, 50);
                }
            }
            Put(p, Nz, 56, Kick, Song.InstKick, 11);
            Put(p, Nz, 60, Kick, Song.InstKick, 13);
            Put(p, Nz, 62, Kick, Song.InstKick, 15);
            return p;
        }

        static Pattern Outro()
        {
            var p = new Pattern(32, "Outro");
            Phrase(p, P1, InstPwm, 12, "0:G-5:446 16:.:A01 31:===");
            Put(p, P2, 0, "C-5", InstStab, 10, "037", "A01");
            // Triangle：伸ばしたルートの上でテンポを段々落とす（リタルダンド）
            Phrase(p, Tri, Song.InstBass, -1, "0:C-3 8:.:F82 16:.:F78 20:.:F6E 24:.:F64 28:===");
            Put(p, Nz, 0, Crash, Song.InstCrash);
            Put(p, Nz, 24, Kick, Song.InstKick, 9);
            Put(p, Nz, 28, "5-#", InstTom, 8);
            return p;
        }

        public static Song BuildDemoSong()
        {
            var s = Song.CreateDefault(64);
            s.title = "UniDAW Demo";
            s.name = "UniDAW Demo";
            s.bpm = 140;
            s.rowsPerBeat = 4;
            s.speed = 6;

            s.instruments.Add(new Instrument("PWM Lead", "15 14 13 12 12 11 11 10 | 10 / 7 5 3 1 0", "", "1 1 2 2 3 3 2 2 | 1 1 1 2 2 2 3 3 3 2 2 2"));
            s.instruments.Add(new Instrument("Pluck", "14 11 9 7 5 4 3 2 1 0", "", "2 2 1"));
            s.instruments.Add(new Instrument("Echo", "6 6 5 5 4 | 4 / 3 2 1 0", "", "1"));
            s.instruments.Add(new Instrument("Tri Kick", "15 15 15 15 0", "12 6 0 -6 -12 -18", ""));
            s.instruments.Add(new Instrument("Open Hat", "11 10 9 8 7 6 5 4 3 2 1 0", "", "0"));
            s.instruments.Add(new Instrument("Tom", "15 13 11 9 7 5 3 1 0", "0 -1 -2 -3 -4 -5", "0"));
            s.instruments.Add(new Instrument("Stab", "13 11 9 | 7 / 4 2 0", "", "2"));

            s.patterns.Clear();
            s.patterns.Add(Intro());       // 0
            s.patterns.Add(Verse(false));  // 1
            s.patterns.Add(Verse(true));   // 2
            s.patterns.Add(Build());       // 3
            s.patterns.Add(Chorus());      // 4
            s.patterns.Add(Breakdown());   // 5
            s.patterns.Add(Outro());       // 6

            // 2 回目の Build から全音上に転調（Noise は移調されない）
            s.order = new List<int> { 0, 1, 2, 3, 4, 5, 3, 4, 6 };
            s.orderTranspose = new List<int> { 0, 0, 0, 0, 0, 0, 2, 2, 2 };

            var m = s.mixer;
            m[P1].volumeDb = -1f; m[P1].pan = -0.15f; m[P1].delaySend = 0.22f; m[P1].reverbSend = 0.18f;
            m[P2].volumeDb = -5f; m[P2].pan = 0.35f; m[P2].delaySend = 0.35f; m[P2].reverbSend = 0.3f; m[P2].duck = 0.35f; m[P2].eqLowDb = -3f;
            m[Tri].volumeDb = -2f; m[Tri].duck = 0.25f; m[Tri].eqLowDb = 2f;
            m[Nz].volumeDb = -4f; m[Nz].reverbSend = 0.08f; m[Nz].eqHighDb = -2f;
            s.effects.delayBeats = 0.75f;
            s.effects.delayFeedback = 0.3f;
            s.effects.delayPingPong = true;
            s.effects.reverbSize = 0.55f;
            s.effects.sidechainSource = Nz;
            s.effects.duckRelease = 0.3f;

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
