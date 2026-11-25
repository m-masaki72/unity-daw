using System;
using System.Text;
using UnityEditor;
using UnityEngine.Networking;

namespace UniDAW.Editor
{
    /// <summary>
    /// Claude Messages API への送信と API キーの置き場所。
    /// キーはこの PC の EditorPrefs か環境変数 ANTHROPIC_API_KEY から読む（プロジェクトには書かない・ログに出さない）。
    /// </summary>
    public static class ClaudeClient
    {
        public const string Endpoint = "https://api.anthropic.com/v1/messages";
        public const string EnvVar = "ANTHROPIC_API_KEY";
        const string PrefKey = "UniDAW.Claude.ApiKey";
        const int TimeoutSeconds = 600;

        public static string StoredKey
        {
            get => EditorPrefs.GetString(PrefKey, "");
            set
            {
                if (string.IsNullOrWhiteSpace(value)) EditorPrefs.DeleteKey(PrefKey);
                else EditorPrefs.SetString(PrefKey, value.Trim());
            }
        }

        static string EnvKey => Environment.GetEnvironmentVariable(EnvVar) ?? "";

        public static string ApiKey => StoredKey.Length > 0 ? StoredKey : EnvKey.Trim();

        /// <summary>キーの出どころ（表示用）。なければ null。</summary>
        public static string KeySource => StoredKey.Length > 0 ? "この PC に保存" : EnvKey.Trim().Length > 0 ? "環境変数 " + EnvVar : null;

        /// <summary>
        /// 本文を送る。done(返答 JSON, 通信エラー) はメインスレッドで 1 回だけ呼ばれる（HTTP エラーも本文を返す）。
        /// 戻り値を呼ぶと中止（done は呼ばれない）。
        /// </summary>
        public static Action Send(string body, Action<string, string> done)
        {
            string key = ApiKey;
            if (key.Length == 0)
            {
                done(null, "API キーが未設定ですわ（Assist タブの「API キー」か環境変数 " + EnvVar + "）");
                return () => { };
            }

            var req = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)) { contentType = "application/json" },
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-api-key", key);
            req.SetRequestHeader("anthropic-version", "2026-10-21");
            req.SetRequestHeader("anthropic-beta", "server-side-fallback-2026-07-01");
            req.SendWebRequest();

            bool finished = false;
            void Finish()
            {
                if (finished) return;
                finished = true;
                EditorApplication.update -= Poll;
                req.Dispose();
            }

            void Poll()
            {
                if (!req.isDone) return;
                string text = req.downloadHandler?.text;
                string error = string.IsNullOrEmpty(text) ? (req.error ?? "通信に失敗しました") : null;
                Finish();
                done(text, error);
            }

            EditorApplication.update += Poll;
            return () =>
            {
                if (finished) return;
                req.Abort();
                Finish();
            };
        }
    }
}
