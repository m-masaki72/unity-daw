using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniDAW
{
    public enum ChannelKind { Pulse, Triangle, Noise }

    /// <summary>
    /// 曲データ。トラッカー形式（行×チャンネル×列）で保持し、
    /// ステップ表示とトラッカー表示は同じデータを編集する。
    /// </summary>
    [CreateAssetMenu(menuName = "UniDAW/Song", fileName = "NewSong")]
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
        /// <summary>順序表の各エントリのトランスポーズ（半音、order と同じ長さ。ノイズには効かない）。LSDj のチェイン移調に相当。</summary>
        public List<int> orderTranspose = new List<int>();
        /// <summary>チャンネルごとのミキサー設定（ChannelCount 個）。</summary>
        public List<ChannelMix> mixer = new List<ChannelMix>();
        /// <summary>マスター音量（dB）。</summary>
        public float masterDb;
        /// <summary>センドエフェクト（ディレイ・リバーブ）。</summary>
        public SongEffects effects = new SongEffects();
        /// <summary>マスターチェーン（EQ・コンプ・リミッターなど）。</summary>
        public SongMaster master = new SongMaster();
        /// <summary>キーの主音（0..11）とスケール。Chromatic = 指定なし（補正・色分けをしない）。</summary>
        public int keyRoot;
        public ScaleMode scaleMode;

        public Scale Key => new Scale(keyRoot, scaleMode);

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
            orderTranspose = new List<int> { 0 };
            mixer = DefaultMixer();
            masterDb = 0f;
            effects = new SongEffects();
            master = new SongMaster();
        }

        public static List<ChannelMix> DefaultMixer()
        {
            var list = new List<ChannelMix>();
            for (int c = 0; c < ChannelCount; c++) list.Add(new ChannelMix());
            return list;
        }

        /// <summary>チャンネルのミキサー設定（範囲外・未初期化なら null。オーディオスレッドからも読む）。</summary>
        public ChannelMix Mix(int ch)
        {
            var m = mixer;
            return m != null && ch >= 0 && ch < m.Count ? m[ch] : null;
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

        public const int MaxTranspose = 48;

        /// <summary>チャンネル ch のノートを移調（ノイズはピッチ番号なので移調しない）。再生と MIDI 書き出しで共用。</summary>
        public static int TransposeNote(int ch, int note, int transpose) =>
            transpose == 0 || Kinds[ch] == ChannelKind.Noise ? note : Math.Max(0, Math.Min(127, note + transpose));

        public int TransposeAtOrder(int orderIndex) =>
            orderTranspose != null && orderIndex >= 0 && orderIndex < orderTranspose.Count ? orderTranspose[orderIndex] : 0;

        /// <summary>順序表にエントリを挿入（トランスポーズも同期）。</summary>
        public void InsertOrder(int index, int pattern, int transpose = 0)
        {
            SyncOrderTranspose();
            index = Math.Max(0, Math.Min(order.Count, index));
            order.Insert(index, pattern);
            orderTranspose.Insert(index, transpose);
        }

        public void RemoveOrder(int index)
        {
            SyncOrderTranspose();
            if (index < 0 || index >= order.Count) return;
            order.RemoveAt(index);
            orderTranspose.RemoveAt(index);
        }

        public void SetOrderTranspose(int index, int transpose)
        {
            SyncOrderTranspose();
            if (index < 0 || index >= order.Count) return;
            orderTranspose[index] = Math.Max(-MaxTranspose, Math.Min(MaxTranspose, transpose));
        }

        public void SwapOrder(int a, int b)
        {
            SyncOrderTranspose();
            (order[a], order[b]) = (order[b], order[a]);
            (orderTranspose[a], orderTranspose[b]) = (orderTranspose[b], orderTranspose[a]);
        }

        /// <summary>トランスポーズ表を順序表と同じ長さに揃える（order を直接編集するインポート等に備える）。</summary>
        void SyncOrderTranspose()
        {
            if (orderTranspose == null) orderTranspose = new List<int>();
            while (orderTranspose.Count < order.Count) orderTranspose.Add(0);
            if (orderTranspose.Count > order.Count) orderTranspose.RemoveRange(order.Count, orderTranspose.Count - order.Count);
        }

        /// <summary>シリアライズ後やインポート後に形を整える。</summary>
        public void Validate()
        {
            if (instruments == null) instruments = new List<Instrument>();
            foreach (var inst in instruments) inst?.EnsureShape();
            if (mixer == null) mixer = new List<ChannelMix>();
            while (mixer.Count < ChannelCount) mixer.Add(new ChannelMix());
            if (mixer.Count > ChannelCount) mixer.RemoveRange(ChannelCount, mixer.Count - ChannelCount);
            for (int c = 0; c < ChannelCount; c++)
            {
                if (mixer[c] == null) mixer[c] = new ChannelMix();
                mixer[c].volumeDb = ChannelMix.ClampDb(mixer[c].volumeDb);
                mixer[c].pan = ChannelMix.ClampPan(mixer[c].pan);
                mixer[c].SetSend(SendKind.Delay, mixer[c].delaySend);
                mixer[c].SetSend(SendKind.Reverb, mixer[c].reverbSend);
                foreach (MixParam p in Enum.GetValues(typeof(MixParam))) mixer[c].Set(p, mixer[c].Get(p));
            }
            if (effects == null) effects = new SongEffects();
            effects.Validate();
            if (master == null) master = new SongMaster();
            master.Validate();
            masterDb = ChannelMix.ClampDb(masterDb);
            var key = Key;
            keyRoot = key.root;
            scaleMode = key.mode;
            if (patterns == null) patterns = new List<Pattern>();
            if (order == null) order = new List<int>();
            if (patterns.Count == 0) patterns.Add(new Pattern(16, "A"));
            foreach (var p in patterns) p.EnsureShape();
            for (int i = 0; i < order.Count; i++)
                order[i] = Math.Max(0, Math.Min(patterns.Count - 1, order[i]));
            if (order.Count == 0) order.Add(0);
            SyncOrderTranspose();
            for (int i = 0; i < orderTranspose.Count; i++)
                orderTranspose[i] = Math.Max(-MaxTranspose, Math.Min(MaxTranspose, orderTranspose[i]));
        }

        void OnValidate() => Validate();
    }
}
