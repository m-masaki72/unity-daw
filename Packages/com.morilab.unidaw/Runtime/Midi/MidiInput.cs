using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace UniDAW.Midi
{
    public struct MidiMessage
    {
        public byte status, data1, data2;
        public int Channel => status & 0x0F;
        public int Kind => status & 0xF0;
        public bool IsNoteOn => Kind == 0x90 && data2 > 0;
        public bool IsNoteOff => Kind == 0x80 || (Kind == 0x90 && data2 == 0);
    }

    /// <summary>
    /// ハードウェア MIDI 入力。現状は Windows (WinMM) 実装。
    /// <see cref="Received"/> はドライバのスレッドから呼ばれるので、
    /// ハンドラはスレッドセーフに（ChipEngine.NoteOn はスレッドセーフ）。
    /// </summary>
    public static class MidiInput
    {
        public static event Action<MidiMessage> Received;

        public static string OpenDeviceName { get; private set; }
        public static bool IsOpen => OpenDeviceName != null;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        const int MIM_DATA = 0x3C3;
        const int CALLBACK_FUNCTION = 0x30000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MIDIINCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwSupport;
        }

        delegate void MidiInProc(IntPtr hMidiIn, uint wMsg, IntPtr dwInstance, IntPtr dwParam1, IntPtr dwParam2);

        [DllImport("winmm.dll")] static extern uint midiInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "midiInGetDevCapsW")]
        static extern int midiInGetDevCaps(UIntPtr uDeviceID, ref MIDIINCAPS caps, uint cbMidiInCaps);
        [DllImport("winmm.dll")] static extern int midiInOpen(out IntPtr lphMidiIn, uint uDeviceID, MidiInProc dwCallback, IntPtr dwInstance, int dwFlags);
        [DllImport("winmm.dll")] static extern int midiInStart(IntPtr hMidiIn);
        [DllImport("winmm.dll")] static extern int midiInStop(IntPtr hMidiIn);
        [DllImport("winmm.dll")] static extern int midiInReset(IntPtr hMidiIn);
        [DllImport("winmm.dll")] static extern int midiInClose(IntPtr hMidiIn);

        static IntPtr handle;
        // GC に回収されないよう静的に保持
        static readonly MidiInProc Callback = OnMidi;

        public static string[] GetDeviceNames()
        {
            var list = new List<string>();
            uint n = midiInGetNumDevs();
            for (uint i = 0; i < n; i++)
            {
                var caps = new MIDIINCAPS();
                if (midiInGetDevCaps((UIntPtr)i, ref caps, (uint)Marshal.SizeOf<MIDIINCAPS>()) == 0) list.Add(caps.szPname);
                else list.Add($"MIDI In {i}");
            }
            return list.ToArray();
        }

        public static bool Open(int index)
        {
            Close();
            var names = GetDeviceNames();
            if (index < 0 || index >= names.Length) return false;
            if (midiInOpen(out handle, (uint)index, Callback, IntPtr.Zero, CALLBACK_FUNCTION) != 0)
            {
                handle = IntPtr.Zero;
                return false;
            }
            midiInStart(handle);
            OpenDeviceName = names[index];
            return true;
        }

        public static void Close()
        {
            if (handle != IntPtr.Zero)
            {
                midiInStop(handle);
                midiInReset(handle);
                midiInClose(handle);
                handle = IntPtr.Zero;
            }
            OpenDeviceName = null;
        }

        static void OnMidi(IntPtr h, uint msg, IntPtr inst, IntPtr p1, IntPtr p2)
        {
            if (msg != MIM_DATA) return;
            long v = p1.ToInt64();
            var m = new MidiMessage { status = (byte)(v & 0xFF), data1 = (byte)((v >> 8) & 0x7F), data2 = (byte)((v >> 16) & 0x7F) };
            if (m.status < 0x80 || m.status >= 0xF0) return; // リアルタイム系は無視
            try { Received?.Invoke(m); }
            catch { /* ドライバスレッドに例外を投げない */ }
        }
#else
        public static string[] GetDeviceNames() => Array.Empty<string>();
        public static bool Open(int index) => false;
        public static void Close() { OpenDeviceName = null; }
#endif

        /// <summary>テストやソフトウェア入力から擬似的にメッセージを流す。</summary>
        public static void Inject(MidiMessage m) => Received?.Invoke(m);
    }
}
