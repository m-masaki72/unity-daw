using System.IO;
using UniDAW.Midi;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>.mid / .midi を Assets に置くと UniDAW の Song としてインポートする（読み取り専用）。</summary>
    [ScriptedImporter(1, new[] { "mid", "midi" })]
    public class UniDAWMidiImporter : ScriptedImporter
    {
        public VoiceAssignMode mode = VoiceAssignMode.Auto;
        [Range(1, 16)] public int rowsPerBeat = 4;
        [Range(8, 256)] public int rowsPerPattern = 64;
        [Range(1, 31)] public int speed = 6;
        public bool includeDrums = true;
        public bool velocityToVolume = true;
        [Range(-24, 24)] public int transpose;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var opt = new MidiImportOptions
            {
                mode = mode,
                rowsPerBeat = rowsPerBeat,
                rowsPerPattern = rowsPerPattern,
                speed = speed,
                includeDrums = includeDrums,
                velocityToVolume = velocityToVolume,
                transpose = transpose,
            };
            var title = Path.GetFileNameWithoutExtension(ctx.assetPath);
            Song song;
            try
            {
                song = MidiToSong.Import(File.ReadAllBytes(ctx.assetPath), opt, title, out var report);
                Debug.Log($"[UniDAW] Imported {ctx.assetPath}: {report}");
            }
            catch (System.Exception ex)
            {
                ctx.LogImportError($"UniDAW: MIDI の読み込みに失敗しました: {ex.Message}");
                return;
            }
            song.name = title;
            ctx.AddObjectToAsset("song", song);
            ctx.SetMainObject(song);
        }
    }
}
