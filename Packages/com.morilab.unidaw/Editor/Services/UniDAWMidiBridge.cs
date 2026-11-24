using System.Collections.Concurrent;
using UniDAW.Midi;
using UnityEditor;

namespace UniDAW.Editor
{
    /// <summary>ライブ入力（MIDI/キーボード）で解決済みのノート。</summary>
    public struct LiveNote
    {
        public int channel;
        public int note;
        public int instrument;
        public int volume;
        public bool on;
    }

    /// <summary>
    /// MIDI 入力 → エンジン（ドライバスレッドから即発音）＋ メインスレッド用キュー（記録用）。
    /// </summary>
    [InitializeOnLoad]
    public static class UniDAWMidiBridge
    {
        public static volatile ChipEngine Engine;
        public static volatile int TargetChannel;
        public static readonly int[] ChannelInstrument = { Song.InstLead, Song.InstSoft, Song.InstBass, Song.InstSnare };
        public static readonly ConcurrentQueue<LiveNote> Pending = new ConcurrentQueue<LiveNote>();
        static readonly int[] lastRaw = { -1, -1, -1, -1 };
        /// <summary>押されている生ノート番号ごとの発音チャンネル（押下中にチャンネルを移っても正しく離すため）。</summary>
        static readonly int[] heldChannel = NewHeld();
        static int[] NewHeld() { var a = new int[128]; for (int i = 0; i < a.Length; i++) a[i] = -1; return a; }

        static UniDAWMidiBridge()
        {
            AssemblyReloadEvents.beforeAssemblyReload += MidiInput.Close;
            EditorApplication.quitting += MidiInput.Close;
            MidiInput.Received += OnMidi;
        }

        /// <summary>MIDI メッセージを処理する（デバイス入力のほか、仮想鍵盤やテストからも呼べる）。</summary>
        public static void OnMidi(MidiMessage m)
        {
            if (!m.IsNoteOn && !m.IsNoteOff) return;
            bool drum = m.Channel == 9;
            int ch = drum ? 3 : TargetChannel;
            int raw = m.data1 & 127;
            if (m.IsNoteOff && !drum && heldChannel[raw] >= 0) ch = heldChannel[raw];
            if (ch < 0 || ch >= Song.ChannelCount) return;
            int note = m.data1;
            int inst = ChannelInstrument[ch];
            if (ch == 3)
            {
                if (drum)
                {
                    var hit = DrumMap.Map(note);
                    note = hit.pitch;
                    inst = hit.instrument;
                }
                else note %= 16;
            }
            else
            {
                note = System.Math.Max(NoteText.MinNote, System.Math.Min(NoteText.MaxNote, note));
            }

            var e = Engine;
            if (m.IsNoteOn)
            {
                int vol = System.Math.Max(1, System.Math.Min(15, (int)System.Math.Round(m.data2 / 127.0 * 15)));
                lastRaw[ch] = m.data1;
                if (!drum) heldChannel[raw] = ch;
                e?.NoteOn(ch, note, vol, inst);
                Pending.Enqueue(new LiveNote { channel = ch, note = note, instrument = inst, volume = vol, on = true });
            }
            else
            {
                if (!drum) heldChannel[raw] = -1;
                if (!drum && lastRaw[ch] == m.data1)
                {
                    e?.NoteOff(ch);
                    lastRaw[ch] = -1;
                }
                Pending.Enqueue(new LiveNote { channel = ch, note = note, on = false });
            }
        }
    }
}
