using System.Collections.Generic;

namespace UniDAW.Editor
{
    /// <summary>
    /// MIDI 入力キュー（<see cref="UniDAWMidiBridge.Pending"/>）を取り出してパターンへ記録する。
    /// 停止中はステップ入力（カーソル位置に書いて進む）、再生中はリアルタイム録音（再生位置に書く）。
    /// </summary>
    public sealed class MidiRecorder
    {
        /// <summary>この音量以上は「既定音量」として記録しない（パラメータロックを増やさない）。</summary>
        const int DefaultVolumeThreshold = 13;

        readonly ComposerSession s;
        readonly Dictionary<int, LiveNote> latest = new Dictionary<int, LiveNote>();

        public MidiRecorder(ComposerSession session) => s = session;

        /// <summary>メインスレッドから毎フレーム呼ぶ。記録しないときもキューは空にする。</summary>
        public void Drain(ChipEngine e)
        {
            if (UniDAWMidiBridge.Pending.IsEmpty) return;
            latest.Clear();
            while (UniDAWMidiBridge.Pending.TryDequeue(out var n))
            {
                if (!n.on) continue;
                // 和音は最高音を残す（ドラムは最後の音）
                if (!latest.TryGetValue(n.channel, out var prev) || n.note > prev.note || n.channel == 3) latest[n.channel] = n;
            }
            if (!s.State.recording || s.Song == null || latest.Count == 0 || s.IsReadOnly) return;

            bool live = e != null && e.IsPlaying && e.PlayingPattern >= 0;
            int pat = live ? e.PlayingPattern : s.State.patternIndex;
            int row = live ? e.PlayingRow : s.State.cursorRow;
            int loop = live ? e.PlayingLoop : 0;
            foreach (var n in latest.Values)
            {
                int vol = n.volume >= DefaultVolumeThreshold ? -1 : n.volume;
                s.ModifyCellAt(pat, n.channel, row, live ? "Record MIDI" : "Step Input", (ref Cell c) =>
                {
                    c.note = n.note;
                    c.instrument = n.instrument;
                    c.volume = vol;
                }, loop);
            }
            if (!live) s.Advance();
        }
    }
}
