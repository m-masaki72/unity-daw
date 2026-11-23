using System;
using System.IO;
using ChipDaw.Midi;
using UnityEditor;
using UnityEngine;

namespace ChipDaw.Editor
{
    /// <summary>
    /// バッチモード用エントリ。
    ///   -executeMethod ChipDaw.Editor.ChipDawCli.CreateDemo
    ///   -executeMethod ChipDaw.Editor.ChipDawCli.RenderMidi -chipdawMidi in.mid -chipdawWav out.wav [-chipdawAsset Assets/x.asset] [-chipdawMode Auto|ByTrack|SplitByPitch]
    /// </summary>
    public static class ChipDawCli
    {
        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        public static void CreateDemo()
        {
            int code = 0;
            try
            {
                var demo = ChipDawAssets.CreateDemo(createScene: true);
                Debug.Log($"[ChipDaw CLI] returned demo: {Stats(ChipEngine.RenderSong(demo, 48000), 48000)}");
                foreach (var path in new[] { ChipDawAssets.DemoSongPath, ChipDawAssets.DemoFromMidiPath, ChipDawAssets.DemoMidiPath })
                {
                    var s = AssetDatabase.LoadAssetAtPath<Song>(path);
                    if (s == null) throw new Exception("生成されていません: " + path);
                    var mono = ChipEngine.RenderSong(s, 48000);
                    Debug.Log($"[ChipDaw CLI] {path}: patterns {s.patterns.Count}, order {s.order.Count}, {Stats(mono, 48000)}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        public static void RenderMidi()
        {
            int code = 0;
            try
            {
                var midi = Arg("-chipdawMidi");
                var wav = Arg("-chipdawWav");
                if (midi == null || wav == null) throw new ArgumentException("-chipdawMidi と -chipdawWav が必要です");
                var opt = new MidiImportOptions();
                var mode = Arg("-chipdawMode");
                if (mode != null) opt.mode = (VoiceAssignMode)Enum.Parse(typeof(VoiceAssignMode), mode, true);
                var song = ChipDawAssets.ImportMidiFile(midi, opt, out var report);
                Debug.Log($"[ChipDaw CLI] Import: {report}");
                var mono = ChipEngine.RenderSong(song, 48000);
                WavWriter.WriteStereo16(Path.GetFullPath(wav), mono, 48000);
                Debug.Log($"[ChipDaw CLI] Rendered {wav}: {Stats(mono, 48000)}");
                var asset = Arg("-chipdawAsset");
                if (asset != null) ChipDawAssets.SaveSong(song, asset);
                else UnityEngine.Object.DestroyImmediate(song);
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
