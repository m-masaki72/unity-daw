using System;
using System.Collections.Generic;

namespace UniDAW
{
    /// <summary>楽器のプリセット（分類つき）。</summary>
    [Serializable]
    public class InstrumentPreset
    {
        /// <summary>楽器の種類の推定にも使う分類名（内蔵プリセットと共通）。</summary>
        public const string Drum = "Drum", Bass = "Bass";

        public string category = "User";
        public Instrument instrument = new Instrument();
        /// <summary>ユーザーが保存したものか（内蔵は消せない）。</summary>
        [NonSerialized] public bool user;

        public InstrumentPreset() { }
        public InstrumentPreset(string category, Instrument instrument, bool user = false)
        {
            this.category = category;
            this.instrument = instrument;
            this.user = user;
        }

        public string Name => instrument?.name ?? "";
        public string Label => category + " / " + Name;
    }

    /// <summary>内蔵の楽器プリセット。</summary>
    public static class InstrumentLibrary
    {
        public static readonly IReadOnlyList<InstrumentPreset> BuiltIn = new List<InstrumentPreset>
        {
            P("Lead", "Square Lead", "15 14 13 12 | 11", "", "2"),
            P("Lead", "Thin Lead", "15 14 13 | 12", "", "0"),
            P("Lead", "Vibrato Lead", "15 14 13 12 | 11", "", "1", "0 0 0 0 0 0 0 0 | 1 1 -1 -1 -1 -1 1 1"),
            P("Lead", "Pluck", "15 12 9 7 5 4 3 2 1 0", "", "1"),
            P("Lead", "Soft", "9 10 11 | 10 / 6 3 1 0", "", "1"),
            P(InstrumentPreset.Bass, "Pulse Bass", "15 13 11 | 10 / 4 0", "", "2"),
            P(InstrumentPreset.Bass, "Slap Bass", "15 12 10 9 | 8 / 3 0", "12 0", "1"),
            P(InstrumentPreset.Bass, "Triangle Bass", "15", "", ""),
            P("Chord", "Major Arp", "13 12 11 | 10 / 6 3 0", "| 0 4 7", "0"),
            P("Chord", "Minor Arp", "13 12 11 | 10 / 6 3 0", "| 0 3 7", "0"),
            P("Chord", "Power Arp", "13 12 11 | 10 / 6 3 0", "| 0 7 12", "2"),
            P("Pad", "Swell", "2 4 6 8 10 11 12 | 12 / 9 7 5 3 1 0", "", "1"),
            P("Pad", "Echo", "13 6 11 5 9 4 7 3 5 2 3 1 1 0", "", "2"),
            P(InstrumentPreset.Drum, "Kick", "15 13 10 7 4 2 0", "0 -2 -4 -6 -8", "0"),
            P(InstrumentPreset.Drum, "Snare", "15 13 11 9 8 6 5 4 3 2 1 0", "", "0"),
            P(InstrumentPreset.Drum, "Closed Hat", "9 6 3 1 0", "", "0"),
            P(InstrumentPreset.Drum, "Open Hat", "10 9 8 7 6 5 4 3 2 1 0", "", "0"),
            P(InstrumentPreset.Drum, "Crash", "13 12 12 11 11 10 10 9 9 8 8 7 7 6 6 5 5 4 4 3 3 2 2 1 1 0", "", "0"),
            P(InstrumentPreset.Drum, "Tom", "15 13 11 9 7 5 3 1 0", "", "2", "-6 -6 -6 -6 -6 -6 -6 -6"),
            P("FX", "Coin", "15 15 15 15 14 13 12 11 10 8 6 4 2 0", "0 0 0 0 5", "1"),
            P("FX", "Jump", "15 14 13 12 11 10 9 8 6 4 2 0", "", "2", "-8 -6 -4 -2 0 2 4 6 8 10 12 14"),
            P("FX", "Laser", "15 14 13 12 11 10 9 8 7 6 5 4 3 2 1 0", "", "0", "| -16"),
        };

        static InstrumentPreset P(string category, string name, string vol, string arp, string duty, string pitch = "") =>
            new InstrumentPreset(category, new Instrument(name, vol, arp, duty, pitch));
    }
}
