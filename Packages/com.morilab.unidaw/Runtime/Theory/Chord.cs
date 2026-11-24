using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace UniDAW
{
    public enum ChordQuality { Major, Minor, Diminished, Augmented, Sus2, Sus4, Dominant7, Major7, Minor7, HalfDiminished7, Power }

    /// <summary>コード（根音 + 種類 + 分数コードのベース）。"Am7" "F#dim" "C/E" や、キーに対するローマ数字 "vi" "V7" "bVII" を読める。</summary>
    public struct Chord
    {
        static readonly int[][] Shapes =
        {
            new[] { 0, 4, 7 }, new[] { 0, 3, 7 }, new[] { 0, 3, 6 }, new[] { 0, 4, 8 }, new[] { 0, 2, 7 }, new[] { 0, 5, 7 },
            new[] { 0, 4, 7, 10 }, new[] { 0, 4, 7, 11 }, new[] { 0, 3, 7, 10 }, new[] { 0, 3, 6, 10 }, new[] { 0, 7 },
        };
        static readonly string[] Suffix = { "", "m", "dim", "aug", "sus2", "sus4", "7", "maj7", "m7", "m7b5", "5" };

        /// <summary>根音（0..11）。</summary>
        public int root;
        public ChordQuality quality;
        /// <summary>分数コードのベース（0..11）。-1 = 根音。</summary>
        public int bass;

        public Chord(int root, ChordQuality quality, int bass = -1)
        {
            this.root = Scale.Pc(root);
            this.quality = quality;
            this.bass = bass < 0 ? -1 : Scale.Pc(bass);
        }

        /// <summary>根音からの半音（構成音）。</summary>
        public int[] Intervals => Shapes[(int)quality];

        public int BassPc => bass >= 0 ? bass : root;

        public bool ContainsPc(int note)
        {
            int rel = Scale.Pc(note - root);
            foreach (var i in Intervals) if (i == rel) return true;
            return Scale.Pc(note) == bass;
        }

        /// <summary>pc の音を [lo, lo+11] に置いた高さ。</summary>
        public static int Place(int pc, int lo) => lo + Scale.Pc(pc - lo);

        public string Name => Scale.RootNames[root] + Suffix[(int)quality] + (bass >= 0 && bass != root ? "/" + Scale.RootNames[bass] : "");
        public override string ToString() => Name;

        // ---- 解析 ------------------------------------------------------------------

        static readonly Regex LetterRx = new Regex(@"^([A-Ga-g])([#b]?)(.*?)(?:/([A-Ga-g])([#b]?))?$");
        static readonly Regex RomanRx = new Regex(@"^([b#]?)(VII|VI|IV|V|III|II|I|vii|vi|iv|v|iii|ii|i)(.*)$");
        static readonly int[] LetterPc = { 9, 11, 0, 2, 4, 5, 7 }; // A..G
        static readonly int[] MajorDegrees = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly string[] Romans = { "I", "II", "III", "IV", "V", "VI", "VII" };

        static int Letter(string l, string acc) =>
            LetterPc[char.ToUpperInvariant(l[0]) - 'A'] + (acc == "#" ? 1 : acc == "b" ? -1 : 0);

        /// <summary>1 つのコード記号を読む。ローマ数字はキーの度数（7 音スケール以外は長音階）で解釈する。</summary>
        public static bool TryParse(string token, Scale key, out Chord chord)
        {
            chord = default;
            if (string.IsNullOrWhiteSpace(token)) return false;
            token = token.Trim();

            var m = LetterRx.Match(token);
            if (m.Success && TryQuality(m.Groups[3].Value, false, out var q))
            {
                int bass = m.Groups[4].Success && m.Groups[4].Value.Length > 0 ? Letter(m.Groups[4].Value, m.Groups[5].Value) : -1;
                chord = new Chord(Letter(m.Groups[1].Value, m.Groups[2].Value), q, bass);
                return true;
            }

            m = RomanRx.Match(token);
            if (!m.Success) return false;
            string numeral = m.Groups[2].Value;
            bool lower = char.IsLower(numeral[0]);
            int degree = Array.IndexOf(Romans, numeral.ToUpperInvariant());
            var steps = key.Intervals.Length == 7 ? key.Intervals : MajorDegrees;
            int acc = m.Groups[1].Value == "b" ? -1 : m.Groups[1].Value == "#" ? 1 : 0;
            if (!TryQuality(m.Groups[3].Value, lower, out var rq)) return false;
            chord = new Chord(key.root + steps[degree] + acc, rq);
            return true;
        }

        /// <summary>根音の後ろの記号。minorBase はローマ数字の小文字（"7" が m7 になる）。</summary>
        static bool TryQuality(string s, bool minorBase, out ChordQuality q)
        {
            q = minorBase ? ChordQuality.Minor : ChordQuality.Major;
            switch (s)
            {
                case "": case "6": case "add9": case "M": case "maj": return true;
                case "m": case "min": case "-": case "m6": q = ChordQuality.Minor; return true;
                case "dim": case "°": case "o": case "dim7": case "°7": case "o7": q = ChordQuality.Diminished; return true;
                case "aug": case "+": q = ChordQuality.Augmented; return true;
                case "sus2": q = ChordQuality.Sus2; return true;
                case "sus4": case "sus": q = ChordQuality.Sus4; return true;
                case "7": case "9": case "13": q = minorBase ? ChordQuality.Minor7 : ChordQuality.Dominant7; return true;
                case "maj7": case "M7": case "Δ": case "Δ7": case "maj9": q = ChordQuality.Major7; return true;
                case "m7": case "min7": case "-7": case "m9": q = ChordQuality.Minor7; return true;
                case "m7b5": case "ø": case "ø7": q = ChordQuality.HalfDiminished7; return true;
                case "5": q = ChordQuality.Power; return true;
                default: return false;
            }
        }
    }

    /// <summary>コード進行（パターン全体に均等に割り付ける）。</summary>
    public static class ChordProgression
    {
        static readonly char[] Separators = { ' ', '\t', '\n', '\r', '|', ',' };

        /// <summary>"C G Am F" / "I V vi IV" を読む。"%" "." は直前のコードの繰り返し。読めない記号は error に入れて飛ばす。</summary>
        public static List<Chord> Parse(string text, Scale key, out string error)
        {
            error = null;
            var list = new List<Chord>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (var tok in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if ((tok == "%" || tok == ".") && list.Count > 0) list.Add(list[list.Count - 1]);
                else if (Chord.TryParse(tok, key, out var c)) list.Add(c);
                else error = error == null ? $"読めないコード: {tok}" : error + $", {tok}";
            }
            return list;
        }

        /// <summary>count 個のコードを length 行に均等に割り付けたとき、i 番目の開始行。</summary>
        public static int StartRow(int i, int count, int length) => count <= 0 ? 0 : (i * length + count - 1) / count;

        /// <summary>row にあるコードの番号（コードがなければ -1）。</summary>
        public static int IndexAt(int row, int count, int length) =>
            count <= 0 || length <= 0 ? -1 : Math.Max(0, Math.Min(count - 1, row * count / length));

        public static Chord? At(IReadOnlyList<Chord> chords, int row, int length)
        {
            int i = IndexAt(row, chords?.Count ?? 0, length);
            return i >= 0 ? chords[i] : (Chord?)null;
        }
    }
}
