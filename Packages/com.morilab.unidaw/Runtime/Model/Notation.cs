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

    /// <summary>エフェクトの表示・解析（例: "037", "V02", "A04"）。</summary>
    public static class FxText
    {
        public static char TypeChar(FxType t)
        {
            switch (t)
            {
                case FxType.Arpeggio: return '0';
                case FxType.SlideUp: return '1';
                case FxType.SlideDown: return '2';
                case FxType.Portamento: return '3';
                case FxType.Vibrato: return '4';
                case FxType.VolumeSlide: return 'A';
                case FxType.Duty: return 'V';
                case FxType.Speed: return 'F';
                case FxType.NoteCut: return 'S';
                default: return '.';
            }
        }

        public static FxType TypeFromChar(char c)
        {
            switch (char.ToUpperInvariant(c))
            {
                case '0': return FxType.Arpeggio;
                case '1': return FxType.SlideUp;
                case '2': return FxType.SlideDown;
                case '3': return FxType.Portamento;
                case '4': return FxType.Vibrato;
                case 'A': return FxType.VolumeSlide;
                case 'V': return FxType.Duty;
                case 'F': return FxType.Speed;
                case 'S': return FxType.NoteCut;
                default: return FxType.None;
            }
        }

        public static string Describe(FxType t)
        {
            switch (t)
            {
                case FxType.Arpeggio: return "Arpeggio";
                case FxType.SlideUp: return "Slide Up";
                case FxType.SlideDown: return "Slide Down";
                case FxType.Portamento: return "Portamento";
                case FxType.Vibrato: return "Vibrato";
                case FxType.VolumeSlide: return "Volume Slide";
                case FxType.Duty: return "Duty / Noise Mode";
                case FxType.Speed: return "Speed / Tempo";
                case FxType.NoteCut: return "Note Cut";
                default: return "None";
            }
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
