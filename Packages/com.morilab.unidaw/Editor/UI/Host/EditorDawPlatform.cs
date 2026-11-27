using System;
using System.IO;
using UniDAW.Composer;
using UniDAW.Midi;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>
    /// エディタのホスト：EditorApplication の時計と update、EditorPrefs、Unity の Undo、GenericMenu、
    /// 隠しプレビューホスト（<see cref="UniDAWPreview"/>）。
    /// </summary>
    [InitializeOnLoad]
    public sealed class EditorDawPlatform : IDawPlatform
    {
        static EditorDawPlatform()
        {
            DawPlatform.Current = new EditorDawPlatform();
            // MIDI 入力はウィンドウを開く前から受けられるようにし、再読み込み・終了で必ず閉じる
            UniDAWMidiBridge.Init();
            AssemblyReloadEvents.beforeAssemblyReload += MidiInput.Close;
            EditorApplication.quitting += MidiInput.Close;
        }

        EditorDawPlatform()
        {
            Undo.undoRedoPerformed += () => UndoRedoPerformed?.Invoke();
            EditorApplication.update += () => Update?.Invoke();
            AssemblyReloadEvents.beforeAssemblyReload += () => Unloading?.Invoke();
            EditorApplication.quitting += () => Unloading?.Invoke();
        }

        public double Now => EditorApplication.timeSinceStartup;
        public event Action Update;
        public event Action Unloading;

        public int GetInt(string key, int defaultValue) => EditorPrefs.GetInt(key, defaultValue);
        public void SetInt(string key, int value) => EditorPrefs.SetInt(key, value);
        public string GetString(string key, string defaultValue) => EditorPrefs.GetString(key, defaultValue);
        public void SetString(string key, string value) => EditorPrefs.SetString(key, value);
        public void DeleteKey(string key) => EditorPrefs.DeleteKey(key);
        public string UserDataFolder => Path.Combine(InternalEditorUtility.unityPreferencesFolder, "UniDAW");

        public void RecordUndo(Song song, string name) => Undo.RecordObject(song, name);
        public void MarkDirty(Song song) => EditorUtility.SetDirty(song);
        public event Action UndoRedoPerformed;
        public bool IsReadOnly(Song song) => UniDAWAssets.IsReadOnly(song);

        public void ShowMenu(DawMenu menu, Rect? worldRect)
        {
            var m = new GenericMenu();
            foreach (var e in menu.Entries)
            {
                if (e.separator) m.AddSeparator(e.path);
                else if (e.enabled)
                {
                    var action = e.action;
                    m.AddItem(new GUIContent(e.path), e.on, () => action());
                }
                else m.AddDisabledItem(new GUIContent(e.path), e.on);
            }
            if (worldRect.HasValue) m.DropDown(worldRect.Value);
            else m.ShowAsContext();
        }

        public IAudioHost AudioHost => UniDAWPreview.AudioHost;
    }
}
