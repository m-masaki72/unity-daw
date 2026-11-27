using UniDAW.Composer;
using UnityEditor;
using UnityEngine;

namespace UniDAW.Editor
{
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
