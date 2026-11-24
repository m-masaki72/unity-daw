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
                next = len - 1;
            }
            pos = next;
            return v;
        }
    }

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

        public Instrument() { }

        public Instrument(string name, string vol, string arp = "", string duty = "")
        {
            this.name = name;
            volume = Sequence.Parse(vol);
            arpeggio = Sequence.Parse(arp);
            this.duty = Sequence.Parse(duty);
        }

        public Instrument Clone() => new Instrument
        {
            name = name, volume = volume.Clone(), arpeggio = arpeggio.Clone(), duty = duty.Clone(),
        };
    }
}
