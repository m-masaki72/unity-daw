using System;
using System.IO;
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
    }
}
