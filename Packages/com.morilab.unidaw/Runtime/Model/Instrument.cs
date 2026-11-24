using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UniDAW
{
    /// <summary>
    /// FamiTracker 風のマクロ列。ティック毎に1つ進む。
    /// テキスト表記: "15 14 | 12 11 / 8 4 0"  ("|" の次がループ開始, "/" の次がリリース開始)
    /// </summary>
    [Serializable]
    public class Sequence
    {
        public int[] values = Array.Empty<int>();
        public int loop = -1;
        public int release = -1;

        public bool IsEmpty => values == null || values.Length == 0;

        public static Sequence Parse(string text)
        {
            var seq = new Sequence();
            if (string.IsNullOrWhiteSpace(text)) return seq;
            var list = new List<int>();
            var tokens = text.Replace("|", " | ").Replace("/", " / ")
                .Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var tok in tokens)
            {
                if (tok == "|") { seq.loop = list.Count; continue; }
                if (tok == "/") { seq.release = list.Count; continue; }
                if (int.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) list.Add(v);
            }
            seq.values = list.ToArray();
            if (seq.loop >= seq.values.Length) seq.loop = -1;
            if (seq.release >= seq.values.Length) seq.release = -1;
            return seq;
        }

        public override string ToString()
        {
            if (IsEmpty) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i == loop) sb.Append("| ");
                if (i == release) sb.Append("/ ");
                sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
                if (i < values.Length - 1) sb.Append(' ');
            }
            return sb.ToString();
        }

        public Sequence Clone() => new Sequence { values = (int[])values.Clone(), loop = loop, release = release };

        /// <summary>現在値を返し pos を進める。released 前はリリース位置より手前でループ／ホールド。</summary>
        public int Step(ref int pos, bool released)
        {
            int len = values.Length;
            if (pos >= len) pos = len - 1;
            if (pos < 0) pos = 0;
            int v = values[pos];
            int next = pos + 1;
            if (!released)
            {
                int end = release > 0 ? release : len;
                if (next >= end)
                    next = loop >= 0 && loop < end ? loop : end - 1;
            }
            else if (next >= len)
            {
                // リリース後：ループ点がリリース部にある（またはリリース指定なし）ならループ、それ以外は最後の値で保持
                next = loop >= 0 && loop < len && (release <= 0 || loop >= release) ? loop : len - 1;
            }
            pos = next;
            return v;
        }
    }

    /// <summary>楽器のマクロの種類（int としてシリアライズされるので値は固定）。</summary>
    public enum MacroKind { Volume = 0, Arpeggio = 1, Pitch = 2, Duty = 3 }

    [Serializable]
    public class Instrument
    {
        public string name = "Instrument";
        /// <summary>0..15</summary>
        public Sequence volume = new Sequence();
        /// <summary>半音オフセット</summary>
        public Sequence arpeggio = new Sequence();
        /// <summary>0..3（ノイズは bit0 でショートモード）</summary>
        public Sequence duty = new Sequence();
        /// <summary>音程の微調整（1/16 半音、tick ごとに積み上がる相対値。FamiTracker の Pitch マクロ相当）。</summary>
        public Sequence pitch = new Sequence();

        public Instrument() { }

        public Instrument(string name, string vol, string arp = "", string duty = "", string pitch = "")
        {
            this.name = name;
            volume = Sequence.Parse(vol);
            arpeggio = Sequence.Parse(arp);
            this.duty = Sequence.Parse(duty);
            this.pitch = Sequence.Parse(pitch);
        }

        public Instrument Clone() => new Instrument
        {
            name = name, volume = volume.Clone(), arpeggio = arpeggio.Clone(), duty = duty.Clone(), pitch = pitch.Clone(),
        };

        public Sequence GetMacro(MacroKind kind)
        {
            switch (kind)
            {
                case MacroKind.Volume: return volume;
                case MacroKind.Arpeggio: return arpeggio;
                case MacroKind.Pitch: return pitch;
                default: return duty;
            }
        }

        public void SetMacro(MacroKind kind, Sequence seq)
        {
            seq = seq ?? new Sequence();
            switch (kind)
            {
                case MacroKind.Volume: volume = seq; break;
                case MacroKind.Arpeggio: arpeggio = seq; break;
                case MacroKind.Pitch: pitch = seq; break;
                default: duty = seq; break;
            }
        }

        /// <summary>マクロの値の範囲（編集 UI 用。アルペジオは半音、ピッチは 1/16 半音/tick）。</summary>
        public static void MacroRange(MacroKind kind, out int min, out int max)
        {
            switch (kind)
            {
                case MacroKind.Volume: min = 0; max = 15; break;
                case MacroKind.Arpeggio: min = -24; max = 24; break;
                case MacroKind.Pitch: min = -32; max = 32; break;
                default: min = 0; max = 3; break;
            }
        }

        /// <summary>古いデータ（列の追加前に保存されたもの）の欠けを埋める。</summary>
        public void EnsureShape()
        {
            if (volume == null) volume = new Sequence();
            if (arpeggio == null) arpeggio = new Sequence();
            if (duty == null) duty = new Sequence();
            if (pitch == null) pitch = new Sequence();
            if (volume.values == null) volume.values = Array.Empty<int>();
            if (arpeggio.values == null) arpeggio.values = Array.Empty<int>();
            if (duty.values == null) duty.values = Array.Empty<int>();
            if (pitch.values == null) pitch.values = Array.Empty<int>();
        }
    }
}
