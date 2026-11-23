using System;
using UnityEngine;
using UnityEngine.Events;

namespace ChipDaw
{
    /// <summary>
    /// Song を AudioSource 経由で鳴らすコンポーネント。エディタ（非 Play モード）でも動く。
    /// ノート発音は <see cref="NoteTriggered"/> / <see cref="onNote"/> で映像側へ通知する。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("ChipDaw/ChipDaw Player")]
    public class ChipDawPlayer : MonoBehaviour
    {
        [Serializable] public class NoteUnityEvent : UnityEvent<int, int, float> { }

        public Song song;
        public bool playOnStart = true;
        public bool loopPatternOnly;
        [Tooltip("true の場合 song フィールドではなく外部（エディタ等）が Engine.Song を制御する")]
        public bool externalControl;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Tooltip("(channel, note, volume01)")]
        public NoteUnityEvent onNote = new NoteUnityEvent();

        public event Action<NoteEvent> NoteTriggered;

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
                silence = AudioClip.Create("ChipDawSilence", rate, 1, rate, false);
                silence.hideFlags = HideFlags.DontSave;
            }
            if (source.clip != silence) source.clip = silence;
            source.loop = true;
            if (!source.isPlaying) source.Play();
        }

        void OnAudioConfigChanged(bool deviceWasChanged)
        {
            var oldSong = Engine?.Song;
            CreateEngine();
            Engine.Song = oldSong;
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
