using System;
using System.Collections.Generic;

namespace UniDAW
{
    /// <summary>
    /// 曲の中の旋律から「音程の動き」と「リズム（発音間隔）」の癖を数えるマルコフ連鎖。
    /// 学習データが少なくても動くよう、順次進行寄りの事前分布と混ぜて使う。
    /// 生成はキー（スケール外は使わない）とコード（強拍は構成音を優先）に従う。
    /// </summary>
    public sealed class MelodyModel
    {
        public const int MaxLeap = 12, MaxIoi = 32;

        public struct Note
        {
            public int start, length, pitch;
            public Note(int start, int length, int pitch)
            {
                this.start = start;
                this.length = length;
                this.pitch = pitch;
            }
        }

        public sealed class Options
        {
            public Scale key;
            /// <summary>行 → その行のコード（null = コードなし）。</summary>
            public Func<int, Chord?> chordAt;
            public int rowsPerBeat = 4;
            /// <summary>ばらつき（0.2〜2。1 で学習どおり、小さいほど無難）。</summary>
            public float temperature = 1f;
            public int seed;
            public int minPitch = NoteText.MinNote, maxPitch = NoteText.MaxNote;
            /// <summary>文脈がないときの出だしの高さ（この近くで動く）。</summary>
            public int center = 72;
        }

        readonly Dictionary<(int, int), Dictionary<int, int>> step2 = new Dictionary<(int, int), Dictionary<int, int>>();
        readonly Dictionary<int, Dictionary<int, int>> step1 = new Dictionary<int, Dictionary<int, int>>();
        readonly Dictionary<int, int> step0 = new Dictionary<int, int>();
        readonly Dictionary<int, Dictionary<int, int>> ioi1 = new Dictionary<int, Dictionary<int, int>>();
        readonly Dictionary<int, int> ioi0 = new Dictionary<int, int>();

        /// <summary>学習した音程の動きの数。</summary>
        public int TrainedSteps { get; private set; }

        static void Count<TK>(Dictionary<TK, Dictionary<int, int>> d, TK key, int v)
        {
            if (!d.TryGetValue(key, out var m)) d[key] = m = new Dictionary<int, int>();
            Count(m, v);
        }

        static void Count(Dictionary<int, int> m, int v) => m[v] = m.TryGetValue(v, out int n) ? n + 1 : 1;

        static int ClampStep(int s) => Math.Max(-MaxLeap, Math.Min(MaxLeap, s));
        static int ClampIoi(int i) => Math.Max(1, Math.Min(MaxIoi, i));

        /// <summary>1 フレーズ（開始行の昇順のノート列）を学習する。</summary>
        public void Train(IReadOnlyList<Note> phrase)
        {
            for (int i = 1; i < phrase.Count; i++)
            {
                int s = ClampStep(phrase[i].pitch - phrase[i - 1].pitch);
                Count(step0, s);
                if (i >= 2)
                {
                    int prev = ClampStep(phrase[i - 1].pitch - phrase[i - 2].pitch);
                    Count(step1, prev, s);
                    if (i >= 3) Count(step2, (ClampStep(phrase[i - 2].pitch - phrase[i - 3].pitch), prev), s);
                }
                int ioi = ClampIoi(phrase[i].start - phrase[i - 1].start);
                Count(ioi0, ioi);
                if (i >= 2) Count(ioi1, ClampIoi(phrase[i - 1].start - phrase[i - 2].start), ioi);
                TrainedSteps++;
            }
        }

        /// <summary>トラックのノート（トラック長の範囲、開始行の昇順）。</summary>
        public static List<Note> Extract(Pattern p, int ch)
        {
            var list = new List<Note>();
            var cells = p.tracks[ch].cells;
            int tl = p.TrackLength(ch), start = -1;
            for (int r = 0; r <= tl; r++)
            {
                bool end = r == tl || cells[r].HasNote || cells[r].IsOff;
                if (end && start >= 0)
                {
                    list.Add(new Note(start, r - start, cells[start].note));
                    start = -1;
                }
                if (r < tl && cells[r].HasNote) start = r;
            }
            return list;
        }

        /// <summary>
        /// 曲の音程のあるチャンネル（ノイズ以外）を全部学習する。
        /// exclude に当たる範囲（作り直す区間）は学習しない。
        /// </summary>
        public static MelodyModel FromSong(Song song, Func<int, int, Note, bool> exclude = null)
        {
            var m = new MelodyModel();
            if (song == null) return m;
            for (int pi = 0; pi < song.patterns.Count; pi++)
            {
                var p = song.patterns[pi];
                for (int ch = 0; ch < Song.ChannelCount; ch++)
                {
                    if (Song.Kinds[ch] == ChannelKind.Noise) continue;
                    var phrase = new List<Note>();
                    foreach (var n in Extract(p, ch))
                    {
                        if (exclude != null && exclude(pi, ch, n))
                        {
                            m.Train(phrase);
                            phrase.Clear();
                        }
                        else phrase.Add(n);
                    }
                    m.Train(phrase);
                }
            }
            return m;
        }

        // ---- 確率 ------------------------------------------------------------------

        static double Prob(Dictionary<int, int> m, int v)
        {
            if (m == null || m.Count == 0) return 0;
            int total = 0;
            foreach (var kv in m) total += kv.Value;
            return m.TryGetValue(v, out int n) ? (double)n / total : 0;
        }

        /// <summary>順次進行寄り（同音は少し控えめ）の事前分布。</summary>
        static double PriorStep(int s) => Math.Exp(-Math.Abs(s) / 2.2) * (s == 0 ? 0.5 : 1);

        double StepWeight(int? p2, int? p1, int s)
        {
            double w = 0, sum = 0;
            void Mix(Dictionary<int, int> m, double k)
            {
                if (m == null || m.Count == 0) return;
                w += k * Prob(m, s);
                sum += k;
            }
            if (p2.HasValue && p1.HasValue && step2.TryGetValue((p2.Value, p1.Value), out var m2)) Mix(m2, 0.45);
            if (p1.HasValue && step1.TryGetValue(p1.Value, out var m1)) Mix(m1, 0.3);
            Mix(step0, 0.15);
            double prior = PriorStep(s) / 3.9; // おおよそ正規化
            w += 0.1 * prior;
            sum += 0.1;
            return w / sum;
        }

        double IoiWeight(int? prev, int ioi, int rpb)
        {
            double w = 0, sum = 0;
            if (prev.HasValue && ioi1.TryGetValue(prev.Value, out var m1))
            {
                w += 0.5 * Prob(m1, ioi);
                sum += 0.5;
            }
            if (ioi0.Count > 0)
            {
                w += 0.35 * Prob(ioi0, ioi);
                sum += 0.35;
            }
            int half = Math.Max(1, rpb / 2);
            double prior = ioi == half ? 0.4 : ioi == rpb ? 0.3 : ioi == 1 ? 0.1 : ioi == rpb * 2 ? 0.15 : ioi == rpb * 4 ? 0.05 : 0;
            w += 0.15 * prior;
            sum += 0.15;
            return w / sum;
        }

        static int Sample(Random rng, List<(int value, double weight)> items, float temperature)
        {
            double t = Math.Max(0.15, temperature), total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                double w = items[i].weight <= 0 ? 0 : Math.Pow(items[i].weight, 1.0 / t);
                items[i] = (items[i].value, w);
                total += w;
            }
            if (total <= 0) return items.Count > 0 ? items[0].value : 0;
            double x = rng.NextDouble() * total;
            foreach (var it in items)
            {
                x -= it.weight;
                if (x <= 0) return it.value;
            }
            return items[items.Count - 1].value;
        }

        // ---- 生成 ------------------------------------------------------------------

        /// <summary>
        /// context（直前のノート、開始行の昇順）の続きを from 行から rows 行ぶん作る。
        /// ノートの長さは次の発音まで（最後は区間の終わりまで）。
        /// </summary>
        public List<Note> Continue(IReadOnlyList<Note> context, int from, int rows, Options o)
        {
            var result = new List<Note>();
            if (rows <= 0) return result;
            var rng = new Random(o.seed);
            int rpb = Math.Max(1, o.rowsPerBeat);
            int end = from + rows;

            var pitches = new List<int>();
            var starts = new List<int>();
            foreach (var n in context)
            {
                pitches.Add(n.pitch);
                starts.Add(n.start);
            }
            int center = o.center;
            if (pitches.Count > 0)
            {
                int sum = 0, k = 0;
                for (int i = Math.Max(0, pitches.Count - 8); i < pitches.Count; i++, k++) sum += pitches[i];
                center = sum / k;
            }
            center = Math.Max(o.minPitch + 6, Math.Min(o.maxPitch - 6, center));

            int row = from;
            int? lastIoi = starts.Count >= 2 ? starts[starts.Count - 1] - starts[starts.Count - 2] : (int?)null;
            var cand = new List<(int, double)>();
            while (row < end)
            {
                // ---- 高さ
                var chord = o.chordAt?.Invoke(row);
                bool strong = row % rpb == 0;
                int pitch;
                if (pitches.Count == 0)
                {
                    // 出だし：コードの構成音（なければ主音）の中から中心に近い音
                    cand.Clear();
                    for (int p = center - 7; p <= center + 7; p++)
                    {
                        if (p < o.minPitch || p > o.maxPitch || !o.key.Contains(p)) continue;
                        bool good = chord.HasValue ? chord.Value.ContainsPc(p) : Scale.Pc(p - o.key.root) == 0 || o.key.IsChromatic;
                        cand.Add((p, (good ? 1.0 : 0.05) * Math.Exp(-Math.Abs(p - center) / 4.0)));
                    }
                    pitch = cand.Count > 0 ? Sample(rng, cand, o.temperature) : center;
                }
                else
                {
                    int prev = pitches[pitches.Count - 1];
                    int? p1 = pitches.Count >= 2 ? ClampStep(prev - pitches[pitches.Count - 2]) : (int?)null;
                    int? p2 = pitches.Count >= 3 ? ClampStep(pitches[pitches.Count - 2] - pitches[pitches.Count - 3]) : (int?)null;
                    cand.Clear();
                    for (int s = -MaxLeap; s <= MaxLeap; s++)
                    {
                        int p = prev + s;
                        if (p < o.minPitch || p > o.maxPitch || !o.key.Contains(p)) continue;
                        double w = StepWeight(p2, p1, s);
                        // スケールで飛ばした音の分を詰める（7 音スケールで 2 半音の動きが消えないように）
                        w += 0.02 * PriorStep(s);
                        int d = Math.Abs(p - center);
                        if (d > 9) w *= Math.Exp(-(d - 9) / 2.5);
                        if (chord.HasValue) w *= chord.Value.ContainsPc(p) ? (strong ? 3.0 : 1.3) : (strong ? 0.35 : 1.0);
                        cand.Add((p, w));
                    }
                    pitch = cand.Count > 0 ? Sample(rng, cand, o.temperature) : prev;
                }

                // ---- 次の発音までの間隔
                int remain = end - row;
                cand.Clear();
                for (int i = 1; i <= Math.Min(MaxIoi, remain); i++)
                {
                    double w = IoiWeight(lastIoi, i, rpb);
                    // 拍の頭に戻りやすく
                    if ((row + i) % rpb == 0) w *= 1.5;
                    cand.Add((i, w));
                }
                int ioi = remain;
                if (cand.Count > 0)
                {
                    bool any = false;
                    foreach (var c in cand) if (c.Item2 > 0) any = true;
                    ioi = any ? Sample(rng, cand, Math.Min(1f, o.temperature)) : Math.Min(remain, Math.Max(1, rpb / 2));
                }

                // 区間の最後の音は主音・コードの根音に落ち着きやすく
                if (row + ioi >= end && pitches.Count > 0)
                    pitch = Resolve(pitch, chord, o);

                result.Add(new Note(row, ioi, pitch));
                pitches.Add(pitch);
                starts.Add(row);
                lastIoi = ioi;
                row += ioi;
            }
            return result;
        }

        /// <summary>近くに主音（なければコードの根音）があればそこへ寄せる。</summary>
        static int Resolve(int pitch, Chord? chord, Options o)
        {
            int target = o.key.IsChromatic ? (chord.HasValue ? chord.Value.root : -1) : o.key.root;
            if (target < 0) return pitch;
            for (int d = 0; d <= 2; d++)
                foreach (var p in new[] { pitch - d, pitch + d })
                    if (Scale.Pc(p - target) == 0 && p >= o.minPitch && p <= o.maxPitch && o.key.Contains(p)) return p;
            return pitch;
        }
    }
}
