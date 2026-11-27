using System;
using System.Text;
using UnityEngine.Networking;

namespace UniDAW.Composer
{
    /// <summary>Claude への接続方法。</summary>
    public enum ClaudeBackend { Auto = 0, ClaudeCode = 1, Api = 2 }

    /// <summary>
    /// Claude への送信。API キーで Messages API を直接呼ぶか、ローカルの Claude Code（<see cref="ClaudeCodeClient"/>）に頼む。
    /// 自動なら API キーがあれば API、なければ Claude Code。
    /// キーはこの PC の設定（エディタでは EditorPrefs）か環境変数 ANTHROPIC_API_KEY から読む（プロジェクトには書かない・ログに出さない）。
    /// </summary>
    public static class ClaudeClient
    {
        public const string Endpoint = "https://api.anthropic.com/v1/messages";
        public const string EnvVar = "ANTHROPIC_API_KEY";
        const string PrefKey = "UniDAW.Claude.ApiKey";
        const string BackendPref = "UniDAW.Claude.Backend";
        public static readonly string[] BackendNames = { "自動", "Claude Code", "API キー" };

        public static ClaudeBackend Backend
        {
            get => (ClaudeBackend)Math.Max(0, Math.Min(2, DawPlatform.Current.GetInt(BackendPref, 0)));
            set => DawPlatform.Current.SetInt(BackendPref, (int)value);
        }

        /// <summary>実際に使う接続（自動を解決したもの）。</summary>
        public static ClaudeBackend Resolve(ClaudeBackend chosen, bool hasKey, bool hasCli) =>
            chosen != ClaudeBackend.Auto ? chosen : hasKey ? ClaudeBackend.Api : hasCli ? ClaudeBackend.ClaudeCode : ClaudeBackend.Api;

        public static ClaudeBackend Effective => Resolve(Backend, ApiKey.Length > 0, ClaudeCodeClient.Available);

        /// <summary>接続の説明（表示用）。</summary>
        public static string Describe()
        {
            if (Effective == ClaudeBackend.ClaudeCode)
                return ClaudeCodeClient.Available
                    ? "接続: Claude Code（" + ClaudeCodeClient.ExecutablePath + "）のログインを使います。送るのは曲の音符・コード・頼みごとだけです"
                    : "接続: Claude Code が見つかりません（claude をインストールしてログイン、または API キー）";
            return KeySource != null
                ? $"接続: API キー（{KeySource}）。送るのは曲の音符・コード・頼みごとだけです"
                : $"API キーが未設定です（「API キー…」か環境変数 {EnvVar}）。Claude Code があれば接続を切り替えられます";
        }

        const int TimeoutSeconds = 600;
        const int MaxTokens = 32000;

        public static string StoredKey
        {
            get => DawPlatform.Current.GetString(PrefKey, "");
            set
            {
                if (string.IsNullOrWhiteSpace(value)) DawPlatform.Current.DeleteKey(PrefKey);
                else DawPlatform.Current.SetString(PrefKey, value.Trim());
            }
        }

        static string EnvKey => Environment.GetEnvironmentVariable(EnvVar) ?? "";

        public static string ApiKey => StoredKey.Length > 0 ? StoredKey : EnvKey.Trim();

        /// <summary>キーの出どころ（表示用）。なければ null。</summary>
        public static string KeySource => StoredKey.Length > 0 ? "この PC に保存" : EnvKey.Trim().Length > 0 ? "環境変数 " + EnvVar : null;

        /// <summary>
        /// 依頼文を送る（schema の JSON で返すよう頼む）。done(返答 JSON, 通信エラー) はメインスレッドで 1 回だけ呼ばれる
        /// （API の HTTP エラーも本文を返す）。戻り値を呼ぶと中止（done は呼ばれない）。
        /// </summary>
        public static Action Send(string model, string system, string schema, string userMessage, Action<string, string> done) =>
            Effective == ClaudeBackend.ClaudeCode
                ? ClaudeCodeClient.Send(model, system, schema, userMessage, done)
                : SendApi(BuildApiBody(model, system, schema, userMessage), done);

        /// <summary>Messages API へのリクエスト本文（構造化出力・effort high・サーバー側フォールバック）。</summary>
        public static string BuildApiBody(string model, string system, string schema, string userMessage) =>
            "{\"model\":" + JsonString(model) +
            ",\"max_tokens\":" + MaxTokens +
            ",\"system\":" + JsonString(system) +
            ",\"messages\":[{\"role\":\"user\",\"content\":" + JsonString(userMessage) + "}]" +
            ",\"output_config\":{\"effort\":\"high\",\"format\":{\"type\":\"json_schema\",\"schema\":" + schema + "}}" +
            ",\"fallbacks\":\"default\"}";

        /// <summary>JSON 文字列リテラル。</summary>
        public static string JsonString(string s)
        {
            s ??= "";
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        static Action SendApi(string body, Action<string, string> done)
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
                DawPlatform.Current.Update -= Poll;
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

            DawPlatform.Current.Update += Poll;
            return () =>
            {
                if (finished) return;
                req.Abort();
                Finish();
            };
        }
    }
}
