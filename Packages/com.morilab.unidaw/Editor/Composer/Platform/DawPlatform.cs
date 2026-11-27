using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>
    /// コンポーザーが使うホスト側の機能（時計・設定の保存・Undo・ポップアップメニュー）。
    /// エディタでは <see cref="EditorDawPlatform"/>、ゲーム内では別の実装に差し替える。
    /// </summary>
    public interface IDawPlatform
    {
        /// <summary>単調に進む秒（表示の時間切れ・プレビューの止め時に使う）。</summary>
        double Now { get; }

        int GetInt(string key, int defaultValue);
        void SetInt(string key, int value);
        string GetString(string key, string defaultValue);
        void SetString(string key, string value);

        /// <summary>曲を書き換える直前に呼ぶ（Undo の記録）。書き換えた後は <see cref="MarkDirty"/>。</summary>
        void RecordUndo(Song song, string name);
        /// <summary>曲を変更済みにする（保存の対象にする）。</summary>
        void MarkDirty(Song song);
        /// <summary>Undo / Redo で曲が戻ったとき。</summary>
        event Action UndoRedoPerformed;

        /// <summary>メニューを出す。worldRect が null ならマウス位置（右クリックメニュー）。</summary>
        void ShowMenu(DawMenu menu, Rect? worldRect);
    }

    /// <summary>いま使うホスト。エディタでは読み込み時に <see cref="EditorDawPlatform"/> が入る。</summary>
    public static class DawPlatform
    {
        static IDawPlatform current;

        public static IDawPlatform Current
        {
            get => current ??= new BasicDawPlatform();
            set => current = value ?? new BasicDawPlatform();
        }
    }

    /// <summary>ホストが無いときの素の実装：経過時間の時計、メモリ上の設定、Undo なし、メニューは出さない。</summary>
    public sealed class BasicDawPlatform : IDawPlatform
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Dictionary<string, int> ints = new Dictionary<string, int>();
        readonly Dictionary<string, string> strings = new Dictionary<string, string>();

        public double Now => clock.Elapsed.TotalSeconds;
        public int GetInt(string key, int defaultValue) => ints.TryGetValue(key, out var v) ? v : defaultValue;
        public void SetInt(string key, int value) => ints[key] = value;
        public string GetString(string key, string defaultValue) => strings.TryGetValue(key, out var v) ? v : defaultValue;
        public void SetString(string key, string value) => strings[key] = value;
        public void RecordUndo(Song song, string name) { }
        public void MarkDirty(Song song) { }
#pragma warning disable 67
        public event Action UndoRedoPerformed;
#pragma warning restore 67
        public void ShowMenu(DawMenu menu, Rect? worldRect) { }
    }
}
