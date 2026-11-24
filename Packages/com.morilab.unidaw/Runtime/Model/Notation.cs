using System;
using System.Globalization;

namespace UniDAW
{
    /// <summary>ノート名の表示・解析（C-4 = MIDI 60）。</summary>
    public static class NoteText
    {
        static readonly string[] Names = { "C-", "C#", "D-", "D#", "E-", "F-", "F#", "G-", "G#", "A-", "A#", "B-" };
        public const int MinNote = 12;   // C-0
        public const int MaxNote = 119;  // B-8

        /// <summary>黒鍵か（ピアノロールの段の色分け用）。</summary>
        public static bool IsBlackKey(int note) => Names[((note % 12) + 12) % 12][1] == '#';

        public static string Format(int note, ChannelKind kind)
        {
            if (note == Cell.Empty) return "---";
            if (note == Cell.Off) return "===";
            if (note < 0) return "???";
            if (kind == ChannelKind.Noise) return (note & 15).ToString("X") + "-#";
            int n = Math.Max(MinNote, Math.Min(MaxNote, note));
            return Names[n % 12] + (n / 12 - 1).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>"C-4" "c#5" "F-#"(ノイズ) "===" "---" を解析。失敗時 false。</summary>
        public static bool TryParse(string s, ChannelKind kind, out int note)
        {
            note = Cell.Empty;
            if (string.IsNullOrWhiteSpace(s)) return true;
            s = s.Trim().ToUpperInvariant();
            if (s == "---" || s == "." || s == "-") return true;
            if (s == "===" || s == "OFF") { note = Cell.Off; return true; }

            if (kind == ChannelKind.Noise)
            {
                string h = s.EndsWith("-#") ? s.Substring(0, s.Length - 2) : s;
                if (int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int p) && p >= 0 && p <= 15)
                {
                    note = p;
                    return true;
                }
                return false;
            }

            if (s.Length < 2) return false;
            int baseIdx = "C D EF G A B".IndexOf(s[0]);
            if (baseIdx < 0 || s[0] == ' ') return false;
            int i = 1;
            int acc = 0;
            if (s[i] == '#') { acc = 1; i++; }
            else if (s[i] == '-') { i++; }
            else if (s[i] == 'B' && s.Length > 2) { acc = -1; i++; }
            if (!int.TryParse(s.Substring(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int oct)) return false;
            int n = (oct + 1) * 12 + baseIdx + acc;
            if (n < MinNote || n > MaxNote) return false;
            note = n;
            return true;
        }

        public static double MidiToHz(double note) => 440.0 * Math.Pow(2.0, (note - 69.0) / 12.0);
    }

    /// <summary>エフェクトの表示・解析（例: "037", "V02", "A04"）。種類ごとの情報は <see cref="Table"/> の 1 か所で定義する。</summary>
    public static class FxText
    {
        struct Info
        {
            public FxType type;
            public char ch;
            public string name, help;
            public Info(FxType type, char ch, string name, string help) { this.type = type; this.ch = ch; this.name = name; this.help = help; }
        }

        static readonly Info[] Table =
        {
            new Info(FxType.Arpeggio, '0', "Arpeggio", "0xy アルペジオ: 0 / +x / +y 半音を tick 毎に巡回（例 037 = マイナー）"),
            new Info(FxType.SlideUp, '1', "Slide Up", "1xx スライドアップ: tick 毎に xx/16 半音上昇"),
            new Info(FxType.SlideDown, '2', "Slide Down", "2xx スライドダウン: tick 毎に xx/16 半音下降"),
            new Info(FxType.Portamento, '3', "Portamento", "3xx ポルタメント: 次ノートへ xx/16 半音/tick で滑る（再発音しない）"),
            new Info(FxType.Vibrato, '4', "Vibrato", "4xy ビブラート: 速度 x, 深さ y"),
            new Info(FxType.VolumeSlide, 'A', "Volume Slide", "Axy ボリュームスライド: x 上昇 / y 下降（1/8 段/tick）"),
            new Info(FxType.Duty, 'V', "Duty / Noise Mode", "Vxx デューティ: 0=12.5% 1=25% 2=50% 3=75%（ノイズは 1 でショートモード）"),
            new Info(FxType.Speed, 'F', "Speed / Tempo", "Fxx スピード/テンポ: 00-1F は tick/行, 20 以上は BPM"),
            new Info(FxType.NoteCut, 'S', "Note Cut", "Sxx ノートカット: 行の xx tick 目で音を止める"),
            new Info(FxType.NoteDelay, 'G', "Note Delay", "Gxx ノートディレイ: 行の xx tick 目まで発音を遅らせる（スウィング・フラム）"),
            new Info(FxType.NoteSlideUp, 'Q', "Note Slide Up", "Qxy ノートスライドアップ: 速度 x で y 半音上へ滑る"),
            new Info(FxType.NoteSlideDown, 'R', "Note Slide Down", "Rxy ノートスライドダウン: 速度 x で y 半音下へ滑る"),
        };

        /// <summary>入力できるエフェクト文字の一覧（例 "0 1 2 3 4 A V F S G Q R"）。</summary>
        public static readonly string AllChars = string.Join(" ", Array.ConvertAll(Table, i => i.ch.ToString()));

        static int IndexOf(FxType t)
        {
            for (int i = 0; i < Table.Length; i++) if (Table[i].type == t) return i;
            return -1;
        }

        public static char TypeChar(FxType t)
        {
            int i = IndexOf(t);
            return i >= 0 ? Table[i].ch : '.';
        }

        public static FxType TypeFromChar(char c)
        {
            c = char.ToUpperInvariant(c);
            foreach (var i in Table) if (i.ch == c) return i.type;
            return FxType.None;
        }

        public static string Describe(FxType t)
        {
            int i = IndexOf(t);
            return i >= 0 ? Table[i].name : "None";
        }

        /// <summary>エフェクトの使い方（ステータスバーのヘルプ用）。</summary>
        public static string Help(FxType t)
        {
            int i = IndexOf(t);
            return i >= 0 ? Table[i].help : "エフェクト: " + AllChars + " + 2 桁 16 進";
        }

        public static string Format(Fx fx) =>
            fx.IsEmpty ? "..." : TypeChar(fx.type) + fx.param.ToString("X2", CultureInfo.InvariantCulture);

        public static bool TryParse(string s, out Fx fx)
        {
            fx = Fx.None;
            if (string.IsNullOrWhiteSpace(s)) return true;
            s = s.Trim();
            if (s == "..." || s == ".") return true;
            if (s.Length != 3) return false;
            var t = TypeFromChar(s[0]);
            if (t == FxType.None) return false;
            if (!int.TryParse(s.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int p)) return false;
            fx = new Fx(t, p);
            return true;
        }
    }
}
