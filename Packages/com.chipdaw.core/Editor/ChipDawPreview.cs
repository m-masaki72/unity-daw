using System.Collections.Concurrent;
using ChipDaw.Midi;
using UnityEditor;
using UnityEngine;

namespace ChipDaw.Editor
{
    /// <summary>エディタ用の隠し再生ホスト（Play モードに入らずに鳴らす）。</summary>
    public static class ChipDawPreview
    {
        const string HostName = "ChipDaw Preview (hidden)";
        static ChipDawPlayer host;

        public static ChipDawPlayer Host
        {
            get
            {
                if (host == null) host = FindOrCreate();
                return host;
            }
        }

        public static ChipEngine Engine => Host.Engine;

        static ChipDawPlayer FindOrCreate()
        {
            foreach (var p in Resources.FindObjectsOfTypeAll<ChipDawPlayer>())
            {
                if (p != null && p.gameObject.name == HostName)
                {
                    p.externalControl = true;
                    p.EnsureSourceRunning();
                    return p;
                }
            }
            var go = EditorUtility.CreateGameObjectWithHideFlags(HostName, HideFlags.HideAndDontSave, typeof(AudioSource), typeof(ChipDawPlayer));
            var pl = go.GetComponent<ChipDawPlayer>();
            pl.playOnStart = false;
            pl.externalControl = true;
            pl.EnsureSourceRunning();
            return pl;
        }
    }

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
    public static class ChipDawMidiBridge
    {
        public static volatile ChipEngine Engine;
        public static volatile int TargetChannel;
        public static readonly int[] ChannelInstrument = { Song.InstLead, Song.InstSoft, Song.InstBass, Song.InstSnare };
        public static readonly ConcurrentQueue<LiveNote> Pending = new ConcurrentQueue<LiveNote>();
        static readonly int[] lastRaw = { -1, -1, -1, -1 };

        static ChipDawMidiBridge()
        {
            AssemblyReloadEvents.beforeAssemblyReload += MidiInput.Close;
            EditorApplication.quitting += MidiInput.Close;
            MidiInput.Received += OnMidi;
        }

        static void OnMidi(MidiMessage m)
        {
            if (!m.IsNoteOn && !m.IsNoteOff) return;
            bool drum = m.Channel == 9;
            int ch = drum ? 3 : TargetChannel;
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
                e?.NoteOn(ch, note, vol, inst);
                Pending.Enqueue(new LiveNote { channel = ch, note = note, instrument = inst, volume = vol, on = true });
            }
            else
            {
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
