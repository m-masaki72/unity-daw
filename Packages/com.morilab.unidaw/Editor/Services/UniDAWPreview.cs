using UnityEditor;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>
    /// 再生先の抽象。コンポーザーはこれ越しにエンジンへ触るので、
    /// 隠しプレビューホスト以外（シーン上のプレイヤー、テスト用スタブ等）にも差し替えられる。
    /// </summary>
    public interface IAudioHost
    {
        ChipEngine Engine { get; }
        /// <summary>オーディオ出力を動かす（止まっていれば再開）。</summary>
        void EnsureRunning();
        /// <summary>エンジンからのノートイベント等をメインスレッドで配る。</summary>
        void DispatchEvents();
    }

    /// <summary>エディタ用の隠し再生ホスト（Play モードに入らずに鳴らす）。</summary>
    public static class UniDAWPreview
    {
        const string HostName = "UniDAW Preview (hidden)";
        static UniDAWPlayer host;

        public static UniDAWPlayer Host
        {
            get
            {
                if (host == null) host = FindOrCreate();
                return host;
            }
        }

        public static ChipEngine Engine => Host.Engine;

        /// <summary>隠しホストを <see cref="IAudioHost"/> として使う既定実装。</summary>
        public static readonly IAudioHost AudioHost = new PreviewAudioHost();

        sealed class PreviewAudioHost : IAudioHost
        {
            public ChipEngine Engine => Host.Engine;
            public void EnsureRunning() => Host.EnsureSourceRunning();
            public void DispatchEvents() => Host.DispatchEvents();
        }

        static UniDAWPlayer FindOrCreate()
        {
            foreach (var p in Resources.FindObjectsOfTypeAll<UniDAWPlayer>())
            {
                if (p != null && p.gameObject.name == HostName)
                {
                    p.externalControl = true;
                    p.EnsureSourceRunning();
                    return p;
                }
            }
            var go = EditorUtility.CreateGameObjectWithHideFlags(HostName, HideFlags.HideAndDontSave, typeof(AudioSource), typeof(UniDAWPlayer));
            var pl = go.GetComponent<UniDAWPlayer>();
            pl.playOnStart = false;
            pl.externalControl = true;
            pl.EnsureSourceRunning();
            return pl;
        }
    }
}
