using System;
using UnityEngine;
using UnityEngine.Events;

namespace UniDAW
{
    /// <summary>
    /// Song を AudioSource 経由で鳴らすコンポーネント。エディタ（非 Play モード）でも動く。
    /// ノート発音は <see cref="NoteTriggered"/> / <see cref="onNote"/> で映像側へ通知する。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("UniDAW/UniDAW Player")]
    public class UniDAWPlayer : MonoBehaviour
    {
        [Serializable] public class NoteUnityEvent : UnityEvent<int, int, float> { }
        [Serializable] public class BeatUnityEvent : UnityEvent<int, int> { }
        [Serializable] public class IntUnityEvent : UnityEvent<int> { }

        public Song song;
        public bool playOnStart = true;
        public bool loopPatternOnly;
        [Tooltip("true の場合 song フィールドではなく外部（エディタ等）が Engine.Song を制御する")]
        public bool externalControl;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("(channel, note, volume01)")]
        public NoteUnityEvent onNote = new NoteUnityEvent();
        [Tooltip("拍の頭ごと (bar, beatInBar)。4/4 として数える")]
        public BeatUnityEvent onBeat = new BeatUnityEvent();
        [Tooltip("小節の頭ごと (bar)")]
        public IntUnityEvent onBar = new IntUnityEvent();
        [Tooltip("パターン（区間）の頭ごと (順序表の位置)")]
        public IntUnityEvent onSection = new IntUnityEvent();

        public event Action<NoteEvent> NoteTriggered;
        public event Action<BeatEvent> BeatTriggered;

        public bool IsPlaying => Engine != null && Engine.IsPlaying;
        /// <summary>今の順序表の位置（曲再生時）。</summary>
        public int CurrentOrder => Engine != null ? Engine.PlayingOrder : -1;

        public ChipEngine Engine { get; private set; }

        AudioSource source;
        AudioClip silence;

        void OnEnable()
        {
            CreateEngine();
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigChanged;
            if (Application.isPlaying && playOnStart && song != null) Play();
        }

        void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigChanged;
            Engine?.Stop();
            if (source != null) source.Stop();
            if (silence != null)
            {
                if (Application.isPlaying) Destroy(silence);
                else DestroyImmediate(silence);
                silence = null;
            }
        }

        void CreateEngine()
        {
            int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            var e = new ChipEngine(rate) { Song = song, MasterVolume = volume };
            Engine = e;
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            EnsureSourceRunning();
        }

        /// <summary>OnAudioFilterRead を確実に回すため、無音ループクリップを鳴らしておく。</summary>
        public void EnsureSourceRunning()
        {
            if (source == null) source = GetComponent<AudioSource>();
            if (silence == null)
            {
                int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
                silence = AudioClip.Create("UniDAWSilence", rate, 1, rate, false);
                silence.hideFlags = HideFlags.DontSave;
            }
            if (source.clip != silence) source.clip = silence;
            source.loop = true;
            if (!source.isPlaying) source.Play();
        }

        void OnAudioConfigChanged(bool deviceWasChanged)
        {
            // デバイス変更でサンプルレートが変わりうるのでエンジンを作り直し、再生状態・ミュート等を引き継ぐ
            var old = Engine;
            CreateEngine();
            if (old == null) return;
            var e = Engine;
            e.Song = old.Song;
            e.RandomSeed = old.RandomSeed;
            for (int c = 0; c < ChipEngine.Channels; c++)
            {
                e.Mute[c] = old.Mute[c];
                e.Solo[c] = old.Solo[c];
            }
            if (old.IsPlaying)
            {
                if (old.IsPatternMode) e.PlayPattern(old.PlayingPattern, old.PlayingRow);
                else e.Play(old.PlayingOrder, old.PlayingRow);
            }
            old.Stop();
            EnsureSourceRunning();
        }

        public void Play()
        {
            if (Engine == null) return;
            Engine.Song = song;
            EnsureSourceRunning();
            if (loopPatternOnly && song != null) Engine.PlayPattern(song.PatternAtOrder(0));
            else Engine.Play();
        }

        public void Stop() => Engine?.Stop();

        /// <summary>
        /// 順序表の位置 <paramref name="order"/> へ移る予約（既定は次の小節の頭、false なら今のパターンの終わり）。
        /// ループ再生（loopPatternOnly）中はパターン番号として扱う。
        /// </summary>
        public void QueueSection(int order, bool atNextBar = true) => Engine?.Queue(order, atNextBar);

        /// <summary>名前のパターンが最初に出てくる順序表の位置へ移る予約。見つからなければ false。</summary>
        public bool QueueSection(string patternName, bool atNextBar = true)
        {
            if (song == null || Engine == null) return false;
            for (int i = 0; i < song.order.Count; i++)
            {
                var p = song.GetPattern(song.PatternAtOrder(i));
                if (p == null || p.name != patternName) continue;
                Engine.Queue(loopPatternOnly ? song.PatternAtOrder(i) : i, atNextBar);
                return true;
            }
            return false;
        }

        /// <summary>チャンネル（パート）を抜き差しする（曲のミキサー設定には触れない）。</summary>
        public void SetLayer(int channel, bool on)
        {
            if (Engine != null && channel >= 0 && channel < ChipEngine.Channels) Engine.Mute[channel] = !on;
        }

        void Update()
        {
            if (Engine == null) return;
            if (Application.isPlaying && !externalControl) Engine.Song = song;
            Engine.MasterVolume = volume;
            DispatchEvents();
        }

        /// <summary>オーディオスレッドで溜まったノートイベントをメインスレッドで配信。</summary>
        public void DispatchEvents()
        {
            if (Engine == null) return;
            while (Engine.NoteEvents.TryDequeue(out var e))
            {
                NoteTriggered?.Invoke(e);
                onNote?.Invoke(e.channel, e.note, e.volume);
            }
            while (Engine.BeatEvents.TryDequeue(out var b))
            {
                BeatTriggered?.Invoke(b);
                onBeat?.Invoke(b.bar, b.beatInBar);
                if (b.BarStart) onBar?.Invoke(b.bar);
                if (b.patternStart) onSection?.Invoke(b.order);
            }
        }

        /// <summary>チャンネル出力レベル（0..1）。映像のオーディオリアクティブ用。</summary>
        public float GetLevel(int channel) => Engine != null && channel >= 0 && channel < ChipEngine.Channels ? Engine.Levels[channel] : 0f;

        void OnAudioFilterRead(float[] data, int channels)
        {
            var e = Engine;
            if (e == null) return;
            e.Render(data, 0, data.Length / channels, channels);
        }
    }
}
