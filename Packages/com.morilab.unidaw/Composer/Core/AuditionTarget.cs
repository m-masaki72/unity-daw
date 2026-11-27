namespace UniDAW.Composer
{
    /// <summary>
    /// 楽器の試聴に使うチャンネルを決める。カーソルが Noise にあってもリードは矩形波で鳴らすため。
    /// 曲の中で使っているチャンネルがあればそこ（カーソルのチャンネルを優先）、無ければプリセットの分類と名前から推定する。
    /// </summary>
    public static class AuditionTarget
    {
        static readonly string[] DrumWords = { "kick", "snare", "hat", "crash", "cymbal", "tom", "clap", "drum", "perc", "noise" };

        /// <summary>楽器の分類と名前から、鳴らすべき音源の種類を推定する。</summary>
        public static ChannelKind Guess(Instrument inst, string category = null)
        {
            if (category == InstrumentPreset.Drum) return ChannelKind.Noise;
            // 単語単位で照合する（"Phat Bass" の hat や "Custom" の tom を拾わない）
            var words = new System.Collections.Generic.HashSet<string>(
                System.Text.RegularExpressions.Regex.Split((inst?.name ?? "").ToLowerInvariant(), "[^a-z]+"));
            foreach (var w in DrumWords)
                if (words.Contains(w) || words.Contains(w + "s")) return ChannelKind.Noise;
            bool noDuty = inst?.duty == null || inst.duty.IsEmpty;
            if (words.Contains("triangle") || words.Contains("tri") || (category == InstrumentPreset.Bass && noDuty)) return ChannelKind.Triangle;
            return ChannelKind.Pulse;
        }

        /// <summary>試聴するチャンネル。<paramref name="instIndex"/> が曲の楽器番号なら使用箇所を優先する（プリセットは -1）。</summary>
        public static int Channel(Song song, int instIndex, Instrument inst, int cursorCh, string category = null)
        {
            int n = Song.Kinds.Length;
            if (song != null && instIndex >= 0)
            {
                var uses = new int[n];
                foreach (var p in song.patterns)
                    for (int ch = 0; ch < n && ch < p.tracks.Length; ch++)
                    {
                        var t = p.tracks[ch];
                        int cur = -1; // 楽器番号の無いノートは直前の楽器を引き継ぐ（ChipEngine と同じ）
                        for (int r = 0, len = System.Math.Min(p.TrackLength(ch), t.cells.Length); r < len; r++)
                        {
                            if (t.cells[r].instrument >= 0) cur = t.cells[r].instrument;
                            if (t.cells[r].HasNote && cur == instIndex) uses[ch]++;
                        }
                    }
                if (uses[cursorCh] > 0) return cursorCh;
                int best = -1;
                for (int ch = 0; ch < n; ch++)
                    if (uses[ch] > 0 && (best < 0 || uses[ch] > uses[best])) best = ch;
                if (best >= 0) return best;
            }
            var kind = Guess(inst, category);
            if (Song.Kinds[cursorCh] == kind) return cursorCh;
            for (int ch = 0; ch < n; ch++)
                if (Song.Kinds[ch] == kind) return ch;
            return cursorCh;
        }
    }
}
