using System;
using UnityEditor;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>エディタのホスト：EditorApplication の時計、EditorPrefs、Unity の Undo、GenericMenu。</summary>
    [InitializeOnLoad]
    public sealed class EditorDawPlatform : IDawPlatform
    {
        static EditorDawPlatform() => DawPlatform.Current = new EditorDawPlatform();

        EditorDawPlatform() => Undo.undoRedoPerformed += () => UndoRedoPerformed?.Invoke();

        public double Now => EditorApplication.timeSinceStartup;
        public int GetInt(string key, int defaultValue) => EditorPrefs.GetInt(key, defaultValue);
        public void SetInt(string key, int value) => EditorPrefs.SetInt(key, value);
        public string GetString(string key, string defaultValue) => EditorPrefs.GetString(key, defaultValue);
        public void SetString(string key, string value) => EditorPrefs.SetString(key, value);

        public void RecordUndo(Song song, string name) => Undo.RecordObject(song, name);

        public void MarkDirty(Song song) => EditorUtility.SetDirty(song);

        public event Action UndoRedoPerformed;

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
    }
}
