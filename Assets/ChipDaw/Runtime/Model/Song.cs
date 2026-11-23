using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChipDaw
{
    public enum ChannelKind { Pulse, Triangle, Noise }

    /// <summary>
    /// 曲データ。トラッカー形式（行×チャンネル×列）で保持し、
    /// ステップ表示とトラッカー表示は同じデータを編集する。
    /// </summary>
    [CreateAssetMenu(menuName = "ChipDaw/Song", fileName = "NewSong")]
    public class Song : ScriptableObject
    {
        public const int ChannelCount = 4;
        public static readonly ChannelKind[] Kinds = { ChannelKind.Pulse, ChannelKind.Pulse, ChannelKind.Triangle, ChannelKind.Noise };
        public static readonly string[] ChannelNames = { "Pulse 1", "Pulse 2", "Triangle", "Noise" };

        // 既定インストゥルメントの添字（MIDI インポートやドラムマップが使う）
        public const int InstLead = 0, InstSoft = 1, InstBass = 2, InstKick = 3, InstSnare = 4, InstHat = 5, InstCrash = 6, InstArp = 7;

        public string title = "Untitled";
        [Range(20, 400)] public float bpm = 150f;
        [Range(1, 16)] public int rowsPerBeat = 4;
        [Range(1, 31)] public int speed = 6;
        public List<Instrument> instruments = new List<Instrument>();
        public List<Pattern> patterns = new List<Pattern>();
        public List<int> order = new List<int>();

        public static Song CreateDefault(int patternLength = 16)
        {
            var s = CreateInstance<Song>();
            s.InitDefault(patternLength);
            return s;
        }

        public void InitDefault(int patternLength = 16)
        {
            instruments = DefaultInstruments();
            patterns = new List<Pattern> { new Pattern(patternLength, "A") };
            order = new List<int> { 0 };
        }

        public static List<Instrument> DefaultInstruments() => new List<Instrument>
        {
            new Instrument("Lead", "15 14 13 12 | 11", "", "2"),
            new Instrument("Soft", "9 10 11 | 10 / 6 3 1 0", "", "1"),
            new Instrument("Bass", "15", "", ""),
            new Instrument("Kick", "15 13 10 7 4 2 0", "0 -2 -4 -6 -8", "0"),
            new Instrument("Snare", "15 13 11 9 8 6 5 4 3 2 1 0", "", "0"),
            new Instrument("Hat", "9 6 3 1 0", "", "0"),
            new Instrument("Crash", "13 12 12 11 11 10 10 9 9 8 8 7 7 6 6 5 5 4 4 3 3 2 2 1 1 0", "", "0"),
            new Instrument("Arp", "13 12 11 | 10 / 6 3 0", "| 0 0 4 4 7 7", "0"),
        };

        public Instrument GetInstrument(int index) =>
            index >= 0 && index < instruments.Count ? instruments[index] : null;

        public Pattern GetPattern(int index) =>
            index >= 0 && index < patterns.Count ? patterns[index] : null;

        public int PatternAtOrder(int orderIndex) =>
            orderIndex >= 0 && orderIndex < order.Count ? order[orderIndex] : -1;

        /// <summary>シリアライズ後やインポート後に形を整える。</summary>
        public void Validate()
        {
            if (instruments == null) instruments = new List<Instrument>();
            if (patterns == null) patterns = new List<Pattern>();
            if (order == null) order = new List<int>();
            if (patterns.Count == 0) patterns.Add(new Pattern(16, "A"));
            foreach (var p in patterns) p.EnsureShape();
            for (int i = 0; i < order.Count; i++)
                order[i] = Math.Max(0, Math.Min(patterns.Count - 1, order[i]));
            if (order.Count == 0) order.Add(0);
        }

        void OnValidate() => Validate();
    }
}
