using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace UniDAW.Composer
{
    /// <summary>
    /// ローカルの Claude Code（`claude -p`）で提案を頼む。API キーは要らず、Claude Code のログイン（サブスクリプション等）を使う。
    /// ツールなし・設定なし・セッションを残さない一回きりの呼び出しで、返答は `--output-format json` の結果オブジェクト。
    /// </summary>
    public static class ClaudeCodeClient
    {
        const int TimeoutSeconds = 600;

        static string cachedPath;
        static double cachedAt = -100;

        /// <summary>claude の実行ファイル（見つからなければ null）。PATH と既定のインストール先を探す。</summary>
        public static string ExecutablePath
        {
            get
            {
                double now = DawPlatform.Current.Now;
                if (now - cachedAt > 10)
                {
                    cachedPath = Find();
                    cachedAt = now;
                }
                return cachedPath;
            }
        }

        public static bool Available => ExecutablePath != null;

        static string Find()
        {
            bool win = Path.DirectorySeparatorChar == '\\';
            string[] names = win ? new[] { "claude.exe" } : new[] { "claude" };
            var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var extra = new[]
            {
                Path.Combine(home, ".local", "bin"), Path.Combine(home, ".claude", "local"),
                "/usr/local/bin", "/opt/homebrew/bin",
            };
            foreach (var group in new[] { dirs, extra })
                foreach (var d in group)
                {
                    if (string.IsNullOrWhiteSpace(d)) continue;
                    foreach (var n in names)
                    {
                        try
                        {
                            var p = Path.Combine(d.Trim().Trim('"'), n);
                            if (File.Exists(p)) return p;
                        }
                        catch (ArgumentException) { }
                    }
                }
            return null;
        }

        /// <summary>
        /// 送る。done(結果 JSON, エラー) はメインスレッドで 1 回だけ呼ばれる。戻り値を呼ぶと中止（done は呼ばれない）。
        /// </summary>
        public static Action Send(string model, string systemPrompt, string schema, string userMessage, Action<string, string> done)
        {
            string exe = ExecutablePath;
            if (exe == null)
            {
                done(null, "Claude Code（claude コマンド）が見つかりませんわ。インストールしてログインするか、API キーを使ってくださいまし");
                return () => { };
            }

            // 作業フォルダは空の一時フォルダ（プロジェクトの CLAUDE.md などを読ませない）
            string work = Path.Combine(Path.GetTempPath(), "UniDAW-claude");
            Directory.CreateDirectory(work);

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = string.Join(" ",
                    "-p", "--output-format", "json",
                    "--model", Quote(model),
                    "--effort", "high",
                    "--tools", Quote(""),
                    "--setting-sources", Quote(""),
                    "--strict-mcp-config", "--disable-slash-commands", "--no-session-persistence",
                    "--system-prompt", Quote(systemPrompt),
                    "--json-schema", Quote(schema)),
                WorkingDirectory = work,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            Process proc;
            try
            {
                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                done(null, "Claude Code を起動できませんでした: " + ex.Message);
                return () => { };
            }

            Task<string> stdout = proc.StandardOutput.ReadToEndAsync();
            Task<string> stderr = proc.StandardError.ReadToEndAsync();
            try
            {
                // 依頼文は標準入力で渡す（長さ・引用符の心配がない）
                var bytes = new UTF8Encoding(false).GetBytes(userMessage);
                proc.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                proc.StandardInput.Close();
            }
            catch (IOException) { }

            double deadline = DawPlatform.Current.Now + TimeoutSeconds;
            bool finished = false;

            void Finish()
            {
                if (finished) return;
                finished = true;
                DawPlatform.Current.Update -= Poll;
                DawPlatform.Current.Unloading -= OnReload;
            }

            // スクリプトの再読み込みで待ち手（update の購読）が消えるので、プロセスを残さず止める
            void OnReload()
            {
                Kill();
                Finish();
                proc.Dispose();
            }

            void Kill()
            {
                try
                {
                    if (!proc.HasExited) proc.Kill();
                }
                catch (Exception) { }
            }

            void Poll()
            {
                if (DawPlatform.Current.Now > deadline)
                {
                    Kill();
                    Finish();
                    proc.Dispose();
                    done(null, "Claude Code の返答が時間切れになりました");
                    return;
                }
                if (!proc.HasExited || !stdout.IsCompleted || !stderr.IsCompleted) return;
                string text = stdout.Result?.Trim();
                string err = stderr.Result?.Trim();
                int code = proc.ExitCode;
                Finish();
                proc.Dispose();
                if (!string.IsNullOrEmpty(text) && text.StartsWith("{")) done(text, null);
                else done(null, $"Claude Code がエラーで終わりました（{code}）: " + Short(string.IsNullOrEmpty(err) ? text : err));
            }

            DawPlatform.Current.Update += Poll;
            DawPlatform.Current.Unloading += OnReload;
            return () =>
            {
                if (!finished) OnReload();
            };
        }

        static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "出力なし";
            s = s.Replace('\r', ' ').Replace('\n', ' ');
            return s.Length > 200 ? s.Substring(0, 200) + "…" : s;
        }

        /// <summary>Windows の引数規則（CommandLineToArgvW）に沿って 1 引数を引用する。Mono / .NET の Unix 側も同じ規則で分割する。</summary>
        public static string Quote(string arg)
        {
            if (arg == null) arg = "";
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\')
                {
                    slashes++;
                    continue;
                }
                if (c == '"')
                {
                    sb.Append('\\', slashes * 2 + 1).Append('"');
                }
                else
                {
                    sb.Append('\\', slashes).Append(c);
                }
                slashes = 0;
            }
            sb.Append('\\', slashes * 2).Append('"');
            return sb.ToString();
        }
    }
}
