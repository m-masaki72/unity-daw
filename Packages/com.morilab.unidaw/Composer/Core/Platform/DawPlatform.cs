using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace UniDAW.Composer
{
    /// <summary>
    /// コンポーザーが使うホスト側の機能（時計・毎フレームの呼び出し・設定の保存・Undo・メニュー・音の出口）。
    /// エディタでは <c>EditorDawPlatform</c>、ゲーム内では別の実装に差し替える。
    /// </summary>
    public interface IDawPlatform
    {
        /// <summary>単調に進む秒（表示の時間切れ・プレビューの止め時に使う）。</summary>
        double Now { get; }
        /// <summary>メインスレッドで毎フレーム呼ばれる（通信の完了待ちなど）。</summary>
        event Action Update;
        /// <summary>スクリプトの再読み込みや終了の直前（外部プロセス・デバイスを閉じる）。</summary>
        event Action Unloading;

        int GetInt(string key, int defaultValue);
        void SetInt(string key, int value);
        string GetString(string key, string defaultValue);
        void SetString(string key, string value);
        void DeleteKey(string key);
        /// <summary>プロジェクトをまたいで使うユーザーのデータ（楽器プリセットなど）の置き場所。</summary>
        string UserDataFolder { get; }

        /// <summary>曲を書き換える直前に呼ぶ（Undo の記録）。書き換えた後は <see cref="MarkDirty"/>。</summary>
        void RecordUndo(Song song, string name);
        /// <summary>曲を変更済みにする（保存の対象にする）。</summary>
        void MarkDirty(Song song);
        /// <summary>Undo / Redo で曲が戻ったとき。</summary>
        event Action UndoRedoPerformed;
        /// <summary>曲が書き換えられないもの（MIDI ファイルから読み込んだままの曲など）か。</summary>
        bool IsReadOnly(Song song);

        /// <summary>メニューを出す。worldRect が null ならマウス位置（右クリックメニュー）。</summary>
        void ShowMenu(DawMenu menu, Rect? worldRect);

        /// <summary>再生・試聴に使う音の出口（無ければ null）。</summary>
        IAudioHost AudioHost { get; }
    }

    /// <summary>いま使うホスト。エディタでは読み込み時に <c>EditorDawPlatform</c> が入る。</summary>
    public static class DawPlatform
    {
        static IDawPlatform current;

        public static IDawPlatform Current
        {
            get => current ??= new BasicDawPlatform();
            set => current = value ?? new BasicDawPlatform();
        }
    }

    /// <summary>
    /// ホストが無いときの素の実装：経過時間の時計、メモリ上の設定、Undo なし、メニューと音は出さない。
    /// 毎フレームの呼び出しは <see cref="Tick"/> を呼んだときだけ。
    /// </summary>
    public class BasicDawPlatform : IDawPlatform
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Dictionary<string, int> ints = new Dictionary<string, int>();
        readonly Dictionary<string, string> strings = new Dictionary<string, string>();

        public virtual double Now => clock.Elapsed.TotalSeconds;
        public event Action Update;
        public event Action Unloading;
        public void Tick() => Update?.Invoke();
        public void Unload() => Unloading?.Invoke();

        public virtual int GetInt(string key, int defaultValue) => ints.TryGetValue(key, out var v) ? v : defaultValue;
        public virtual void SetInt(string key, int value) => ints[key] = value;
        public virtual string GetString(string key, string defaultValue) => strings.TryGetValue(key, out var v) ? v : defaultValue;
        public virtual void SetString(string key, string value) => strings[key] = value;
        public virtual void DeleteKey(string key)
        {
            ints.Remove(key);
            strings.Remove(key);
        }
        public virtual string UserDataFolder => System.IO.Path.GetTempPath();

        public virtual void RecordUndo(Song song, string name) { }
        public virtual void MarkDirty(Song song) { }
        public event Action UndoRedoPerformed;
        protected void RaiseUndoRedo() => UndoRedoPerformed?.Invoke();
        public virtual bool IsReadOnly(Song song) => false;

        public virtual void ShowMenu(DawMenu menu, Rect? worldRect) { }
        public virtual IAudioHost AudioHost => null;
    }
}
