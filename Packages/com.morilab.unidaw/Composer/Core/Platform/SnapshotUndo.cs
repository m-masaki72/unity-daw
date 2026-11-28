using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniDAW.Composer
{
    /// <summary>
    /// 曲全体の JSON スナップショットで Undo / Redo する履歴（エディタの Undo が無いホスト用）。
    /// 同じ名前の記録が <see cref="MergeSeconds"/> 以内に続いたら 1 段にまとめる（スライダーのドラッグ等）。
    /// 別の曲を記録したら履歴は捨てる。
    /// </summary>
    public sealed class SnapshotUndo
    {
        /// <summary>段数の上限。</summary>
        public const int MaxUndo = 100;
        /// <summary>同じ名前の記録をまとめる間隔（秒）。</summary>
        public const double MergeSeconds = 0.5;

        readonly Func<double> now;
        readonly List<string> undo = new List<string>(), redo = new List<string>();
        Song song;
        string lastName;
        double lastTime = double.NegativeInfinity;

        /// <param name="now">今の時刻（秒）。記録をまとめる判定に使う。</param>
        public SnapshotUndo(Func<double> now) => this.now = now;

        public bool CanUndo => song != null && undo.Count > 0;
        public bool CanRedo => song != null && redo.Count > 0;

        /// <summary>変更の直前に呼ぶ。</summary>
        public void Record(Song target, string name)
        {
            if (target == null) return;
            if (target != song)
            {
                undo.Clear();
                redo.Clear();
                song = target;
            }
            double t = now();
            bool merge = name == lastName && t - lastTime < MergeSeconds && undo.Count > 0;
            lastName = name;
            lastTime = t;
            if (merge) return;
            undo.Add(JsonUtility.ToJson(target));
            if (undo.Count > MaxUndo) undo.RemoveAt(0);
            redo.Clear();
        }

        /// <summary>1 段戻す。戻せたら true。</summary>
        public bool Undo() => Step(undo, redo);

        /// <summary>1 段やり直す。やり直せたら true。</summary>
        public bool Redo() => Step(redo, undo);

        bool Step(List<string> from, List<string> to)
        {
            if (song == null || from.Count == 0) return false;
            to.Add(JsonUtility.ToJson(song));
            string json = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            JsonUtility.FromJsonOverwrite(json, song);
            lastName = null;
            return true;
        }
    }
}
