using System;
using System.IO;
using UniDAW.Composer;
using UniDAW.Midi;
using UnityEditor;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>
    /// バッチモード用エントリ。
    ///   -executeMethod UniDAW.Editor.UniDAWCli.RenderMidi -unidawMidi in.mid -unidawWav out.wav [-unidawAsset Assets/x.asset] [-unidawMode Auto|ByTrack|SplitByPitch]
    /// </summary>
    public static class UniDAWCli
    {
        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        public static void RenderMidi()
        {
            int code = 0;
            try
            {
                var midi = Arg("-unidawMidi");
                var wav = Arg("-unidawWav");
                if (midi == null || wav == null) throw new ArgumentException("-unidawMidi と -unidawWav が必要です");
                var opt = new MidiImportOptions();
                var mode = Arg("-unidawMode");
                if (mode != null) opt.mode = (VoiceAssignMode)Enum.Parse(typeof(VoiceAssignMode), mode, true);
                var song = UniDAWAssets.ImportMidiFile(midi, opt, out var report);
                Debug.Log($"[UniDAW CLI] Import: {report}");
                var stereo = ChipEngine.RenderSongStereo(song, 48000);
                WavWriter.Write16(Path.GetFullPath(wav), stereo, 2, 48000);
                Debug.Log($"[UniDAW CLI] Rendered {wav}: {Stats(stereo, 48000 * 2)}");
                var asset = Arg("-unidawAsset");
                if (asset != null) UniDAWAssets.SaveSong(song, asset);
                else UnityEngine.Object.DestroyImmediate(song);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        /// <summary>曲アセットを解析して結果をログに出す：-executeMethod UniDAW.Editor.UniDAWCli.AnalyzeSong -unidawAsset Assets/x.asset [-unidawMatch]</summary>
        public static void AnalyzeSong()
        {
            int code = 0;
            try
            {
                var path = Arg("-unidawAsset") ?? throw new ArgumentException("-unidawAsset が必要です");
                var song = AssetDatabase.LoadAssetAtPath<Song>(path) ?? throw new ArgumentException("曲が見つかりません: " + path);
                song.Validate();
                var master = song.master.Clone();
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-unidawMatch") >= 0)
                    master = MasterAnalysis.MatchLoudness(song, master, out _) ?? master;
                Debug.Log($"[UniDAW CLI] Analyze {path} (master {(master.enabled ? "on" : "off")}, gain {master.gainDb:0.0} dB)\n{MasterAnalysis.Analyze(song, master).Text()}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                code = 1;
            }
            EditorApplication.Exit(code);
        }


        /// <summary>
        /// 本物の Claude Code（claude -p）で AI 支援を一通り試す：下書き（おまかせ）→ 固定付きのメロディ→直す→音色→講評→次の一手。
        ///   -executeMethod UniDAW.Editor.UniDAWCli.AiSmoke [-unidawBrief 概要] [-unidawAsset Assets/UniDAW/AiSmoke.asset] [-unidawModel 0|1]
        /// </summary>
        public static void AiSmoke()
        {
            int code = 0;
            var song = Song.CreateDefault(32);
            var w = ScriptableObject.CreateInstance<UniDAWWindow>();
            try
            {
                w.SetSong(song);
                ClaudeAssist c = null;
                int calls = 0;
                c = new ClaudeAssist(w.Session, w.Roll, (model, msg, done) =>
                {
                    var target = c.SendingTarget;
                    var (system, schema) = ClaudeAssist.PromptFor(target);
                    int n = ++calls;
                    Debug.Log($"[AiSmoke] #{n} {target} {model} 送信 {msg.Length} 文字\n{msg}");
                    var t0 = DateTime.Now;
                    string text = null, err = null;
                    bool got = false;
                    ClaudeCodeClient.Send(model, system, schema, msg, (x, e) => { text = x; err = e; got = true; });
                    // バッチモードでは update が回らないので自分で回す
                    while (!got)
                    {
                        System.Threading.Thread.Sleep(200);
                        EditorApplication.update?.Invoke();
                    }
                    Debug.Log($"[AiSmoke] #{n} 返答 {(DateTime.Now - t0).TotalSeconds:0}s {(err ?? "")}\n{text}");
                    done(text, err);
                    return () => { };
                });
                c.RunBackground = a => { a(); return System.Threading.Tasks.Task.CompletedTask; };
                var st = w.State;
                if (int.TryParse(Arg("-unidawModel"), out int m)) c.SetModel(m);

                void Step(string name, bool ok)
                {
                    Debug.Log($"[AiSmoke] {name}: {(ok ? "OK" : "NG")} / {c.Status}{(c.StatusIsError ? "（エラー）" : "")}\n{Dump(song)}");
                    if (!ok || c.StatusIsError) code = 1;
                }

                // 1. 下書き（おまかせ）
                st.aiTarget = AiTarget.Draft;
                st.aiDraftAuto = true;
                st.aiPrompt = Arg("-unidawBrief") ?? "明るいフィールド曲。イントロ・Aメロ・サビの短い構成で、サビは盛り上げて";
                Step("下書き", c.Request() && !c.Drafting);

                // 2. 固定小節つきでリードを書き直し → 直す
                int lead = song.order.Count > 1 ? song.order[1] : song.order[0];
                w.Session.SelectPattern(lead);
                st.cursorCh = 0;
                st.cursorRow = 0;
                st.assistRows = song.patterns[lead].length;
                st.aiTarget = AiTarget.Melody;
                c.SetLockBars("1");
                st.aiPrompt = "2 小節目以降をもっと跳ねるリズムに";
                Step("固定付きメロディ", c.Request());
                st.aiPrompt = "最後の小節は高い音で締めて";
                Step("直す", c.Refine());
                c.SetLockBars("");

                // 3. 音色
                st.aiTarget = AiTarget.Instrument;
                st.aiPrompt = "柔らかいフルートっぽいリード";
                Step("音色", c.Request());
                var inst = song.instruments[w.Session.ChannelInstrument(0)];
                Debug.Log($"[AiSmoke] 音色 {inst.name}: vol {inst.volume} / arp {inst.arpeggio} / duty {inst.duty} / pitch {inst.pitch}");

                // 4. 講評 → 次の一手
                st.aiTarget = AiTarget.Critique;
                st.aiPrompt = "";
                Step("講評", c.Request());
                if (c.Critique != null)
                {
                    Debug.Log("[AiSmoke] 講評: " + c.Critique.comment + "\n- " + string.Join("\n- ", c.Critique.points ?? new string[0]) +
                              "\n次: " + string.Join(" / ", Array.ConvertAll(c.Critique.next ?? new ClaudeReply.NextAction[0], x => $"{x.label}（{x.target} ch{x.channel} p{x.pattern}）{x.request}")));
                    if (c.Critique.next != null && c.Critique.next.Length > 0) Step("次の一手", c.RunNext(0));
                }

                UniDAWAssets.SaveSong(song, Arg("-unidawAsset") ?? "Assets/UniDAW/AiSmoke.asset");
                Debug.Log($"[AiSmoke] 完了（{calls} 回送信）");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                code = 1;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(w);
            }
            EditorApplication.Exit(code);
        }

        static string Dump(Song song)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("order ").Append(string.Join(",", song.order)).Append('\n');
            for (int pi = 0; pi < song.patterns.Count; pi++)
            {
                var p = song.patterns[pi];
                sb.Append($"P{pi} \"{p.name}\" len {p.length} chords [{p.chords}]\n");
                for (int ch = 0; ch < Song.ChannelCount; ch++)
                {
                    sb.Append("  ch").Append(ch).Append(':');
                    var cells = p.tracks[ch].cells;
                    for (int r = 0; r < cells.Length; r++)
                        if (cells[r].HasNote) sb.Append(' ').Append(r).Append('=').Append(NoteText.Format(cells[r].note, Song.Kinds[ch]));
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        public static string Stats(float[] mono, int rate)
        {
            double sum = 0;
            float peak = 0;
            foreach (var s in mono)
            {
                sum += s * s;
                peak = Math.Max(peak, Math.Abs(s));
            }
            double rms = mono.Length > 0 ? Math.Sqrt(sum / mono.Length) : 0;
            return $"duration {mono.Length / (double)rate:F2}s, peak {peak:F3}, rms {rms:F4}";
        }

        /// <summary>
        /// プレイヤー用（UNITY_EDITOR なし）にスクリプトだけコンパイルし、UniDAW.Composer が通るか確かめる。
        ///   -executeMethod UniDAW.Editor.UniDAWCli.CompilePlayer [-unidawOut Temp/UniDAWPlayerCompile]
        /// </summary>
        public static void CompilePlayer()
        {
            var dir = Arg("-unidawOut") ?? "Temp/UniDAWPlayerCompile";
            var settings = new UnityEditor.Build.Player.ScriptCompilationSettings
            {
                group = BuildTargetGroup.Standalone,
                target = BuildTarget.StandaloneWindows64,
            };
            var result = UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(settings, dir);
            bool ok = result.assemblies != null && result.assemblies.Contains("UniDAW.Composer.dll");
            Debug.Log("[UniDAW] CompilePlayer: " + (ok ? "OK " : "FAILED ") + (result.assemblies == null ? "" : string.Join(", ", result.assemblies)));
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
