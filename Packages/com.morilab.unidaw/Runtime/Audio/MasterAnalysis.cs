using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UniDAW
{
    /// <summary>マスタリング用の解析結果：曲全体の測定・チャンネルごとのラウドネス・気づいた点。</summary>
    public sealed class MasterReport
    {
        public LoudnessStats mix;
        /// <summary>チャンネルごとの統合ラウドネス（マスターチェーンなし）。測っていなければ null。</summary>
        public float[] channelLufs;
        public float targetLufs;
        public bool chainEnabled;
        public readonly List<string> hints = new List<string>();

        /// <summary>目標との差（dB、正なら大きすぎ）。</summary>
        public float TargetDelta => mix.integratedLufs - targetLufs;

        public string Summary()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("ラウドネス ").Append(LoudnessMeter.Format(mix.integratedLufs)).Append(" LUFS（目標 ")
              .Append(targetLufs.ToString("0.#", inv));
            if (!mix.Silent) sb.Append("、").Append(TargetDelta.ToString("+0.0;-0.0;0.0", inv)).Append(" dB");
            sb.Append("）\n");
            sb.Append("ピーク ").Append(LoudnessMeter.Format(mix.peakDb)).Append(" dBFS　PLR ")
              .Append(mix.Silent ? "—" : mix.Plr.ToString("0.0", inv)).Append(" dB　短時間最大 ")
              .Append(LoudnessMeter.Format(mix.shortTermMaxLufs)).Append(" LUFS\n");
            sb.Append("帯域 低 ").Append(Pct(mix.lowShare)).Append(" / 中 ").Append(Pct(mix.midShare)).Append(" / 高 ").Append(Pct(mix.highShare))
              .Append("　相関 ").Append(mix.correlation.ToString("0.00", inv));
            int sec = (int)Math.Round(mix.seconds);
            sb.Append("　").Append(sec / 60).Append(':').Append((sec % 60).ToString("00"));
            if (channelLufs != null)
            {
                sb.Append("\nチャンネル");
                for (int c = 0; c < channelLufs.Length; c++)
                    sb.Append(' ').Append(Song.ChannelNames[c]).Append(' ').Append(LoudnessMeter.Format(channelLufs[c]));
            }
            return sb.ToString();
        }

        public string Text()
        {
            var sb = new StringBuilder(Summary());
            foreach (var h in hints) sb.Append("\n・").Append(h);
            return sb.ToString();
        }

        static string Pct(float v) => Math.Round(v * 100) + "%";
    }

    /// <summary>オフラインレンダリングで曲を測り、マスタリングの目安を出す。ラウドネス合わせ（ゲインの自動調整）も行う。</summary>
    public static class MasterAnalysis
    {
        public const int SampleRate = 44100;

        /// <summary>曲を測る。<paramref name="master"/> を曲のマスターチェーンの代わりに使う（null なら曲の設定）。</summary>
        public static MasterReport Analyze(Song song, SongMaster master = null, bool channels = true)
        {
            master = master ?? song.master ?? new SongMaster();
            var report = new MasterReport { targetLufs = master.targetLufs, chainEnabled = master.enabled };
            report.mix = Measure(song, master);
            if (channels)
            {
                report.channelLufs = new float[ChipEngine.Channels];
                for (int c = 0; c < report.channelLufs.Length; c++)
                {
                    bool silent = song.Mix(c) != null && song.Mix(c).mute;
                    report.channelLufs[c] = silent ? float.NegativeInfinity
                        : LoudnessMeter.Measure(OfflineRenderer.RenderChannelStereo(song, SampleRate, c), SampleRate).integratedLufs;
                }
            }
            AddHints(report, master);
            return report;
        }

        /// <summary>指定のマスター設定で曲全体を測る。</summary>
        public static LoudnessStats Measure(Song song, SongMaster master) =>
            LoudnessMeter.Measure(OfflineRenderer.RenderSongStereo(song, SampleRate, e => e.MasterOverride = master), SampleRate);

        /// <summary>
        /// 目標ラウドネスに合うマスターのゲインを探す（リミッターを有効にしたうえで数回測り直す）。
        /// 返り値は調整後の設定のコピー。無音なら null。
        /// </summary>
        public static SongMaster MatchLoudness(Song song, SongMaster master, out LoudnessStats result)
        {
            var m = (master ?? new SongMaster()).Clone();
            m.enabled = true;
            m.limiter = true;
            var (min, max, _) = SongMaster.Range(MasterParam.Gain);
            result = default;
            for (int i = 0; i < 4; i++)
            {
                result = Measure(song, m);
                if (result.Silent) return null;
                float diff = m.targetLufs - result.integratedLufs;
                if (Math.Abs(diff) < 0.2f) break;
                float next = Math.Max(min, Math.Min(max, m.gainDb + diff));
                if (Math.Abs(next - m.gainDb) < 0.05f) break; // 上限・下限に張り付いた
                m.gainDb = (float)Math.Round(next, 1);
            }
            return m;
        }

        static void AddHints(MasterReport r, SongMaster m)
        {
            var s = r.mix;
            var h = r.hints;
            var inv = CultureInfo.InvariantCulture;
            if (s.Silent)
            {
                h.Add("音が出ていません。ミュート・ソロ・音量を確認してください");
                return;
            }
            if (s.peakDb > -0.1f)
                h.Add("ピークが 0 dBFS に達しています（音割れの恐れ）。マスターを ON にしてリミッターの上限を -1 dB に");
            float d = r.TargetDelta;
            if (d > 1f) h.Add($"目標より {d.ToString("0.0", inv)} dB 大きい。ゲインを下げるか「ラウドネスを合わせる」");
            else if (d < -1f) h.Add($"目標より {(-d).ToString("0.0", inv)} dB 小さい。「ラウドネスを合わせる」でゲインを上げられます");
            if (s.Plr < 6f) h.Add($"ダイナミクスが少なめ（PLR {s.Plr.ToString("0.0", inv)} dB）。コンプのレシオやゲインを控えると抜けが良くなります");
            else if (s.Plr > 18f) h.Add($"ピークに比べて平均が小さい（PLR {s.Plr.ToString("0.0", inv)} dB）。コンプで粒をそろえると音量を稼げます");
            if (s.lowShare > 0.55f) h.Add($"低域が多め（{Math.Round(s.lowShare * 100)}%）。EQ のローを下げるか Triangle の音量を控えめに");
            else if (s.lowShare < 0.08f) h.Add($"低域が少なく軽い（{Math.Round(s.lowShare * 100)}%）。Triangle のベースや EQ のローを足すと厚みが出ます");
            if (s.highShare > 0.3f) h.Add($"高域が強め（{Math.Round(s.highShare * 100)}%、耳に痛い恐れ）。EQ のハイを下げるか、細いデューティ・ノイズを控えめに");
            else if (s.highShare < 0.02f) h.Add($"高域が少なくこもり気味（{Math.Round(s.highShare * 100)}%）。EQ のハイを少し足すと明るくなります");
            if (s.correlation < 0.3f) h.Add($"左右の相関が低い（{s.correlation.ToString("0.00", inv)}）。モノラル再生で痩せる恐れ。幅を 100% 以下に");
            else if (s.correlation > 0.97f) h.Add("ほぼモノラルです。パンを振るか、ディレイのピンポン・幅で広がりを");
            if (r.channelLufs != null)
            {
                float loudest = float.NegativeInfinity;
                int top = -1;
                for (int c = 0; c < r.channelLufs.Length; c++)
                    if (r.channelLufs[c] > loudest) { loudest = r.channelLufs[c]; top = c; }
                for (int c = 0; c < r.channelLufs.Length; c++)
                {
                    float v = r.channelLufs[c];
                    if (c != top && !float.IsNegativeInfinity(v) && loudest - v > 12f)
                        h.Add($"{Song.ChannelNames[c]} が {Song.ChannelNames[top]} より {(loudest - v).ToString("0", inv)} dB 小さめです（伴奏・エコーとして控えめにしているなら問題ありません）");
                }
            }
            if (m.enabled && m.compRatio > 4f && m.compThresholdDb < -20f)
                h.Add("コンプが深めです。ポンピング（音量の揺れ）が気になるならスレッショルドを上げてください");
            if (h.Count == 0) h.Add("大きな問題は見つかりませんでした");
        }
    }
}
