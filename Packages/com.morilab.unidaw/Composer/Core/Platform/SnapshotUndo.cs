using System;
using System.Collections.Generic;
using UnityEngine;

namespace UniDAW.Composer
{
    /// <summary>
    /// 曲全体の JSON スナップショットで Undo / Redo する履歴（エディタの Undo が無いホスト用）。
    /// 同じ名前の記録が <see cref="MergeSeconds"/> 以内に続いたら 1 段にまとめる（スライダーのドラッグ等）。
    /// 別の曲を記録したら履歴は捨てる。
    /// 一番新しい段だけを全文で持ち、それより古い段は 1 つ新しい段との差分で持つ（1 回の編集は曲のごく一部なので小さく済む）。
    /// </summary>
    public sealed class SnapshotUndo
    {
        /// <summary>段数の上限。</summary>
        public const int MaxUndo = 100;
        /// <summary>同じ名前の記録をまとめる間隔（秒）。</summary>
        public const double MergeSeconds = 0.5;

        readonly Func<double> now;
        readonly DeltaStack undo = new DeltaStack(), redo = new DeltaStack();
        Song song;
        string lastName;
        double lastTime = double.NegativeInfinity;

        /// <param name="now">今の時刻（秒）。記録をまとめる判定に使う。</param>
        public SnapshotUndo(Func<double> now) => this.now = now;

        public bool CanUndo => song != null && undo.Count > 0;
        public bool CanRedo => song != null && redo.Count > 0;

        /// <summary>履歴が持っている文字数（Undo と Redo の合計。メモリの目安）。</summary>
        public long StoredChars => undo.StoredChars + redo.StoredChars;

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
            undo.Push(JsonUtility.ToJson(target));
            if (undo.Count > MaxUndo) undo.RemoveOldest();
            redo.Clear();
        }

        /// <summary>1 段戻す。戻せたら true。</summary>
        public bool Undo() => Step(undo, redo);

        /// <summary>1 段やり直す。やり直せたら true。</summary>
        public bool Redo() => Step(redo, undo);

        bool Step(DeltaStack from, DeltaStack to)
        {
            if (song == null || from.Count == 0) return false;
            to.Push(JsonUtility.ToJson(song));
            JsonUtility.FromJsonOverwrite(from.Pop(), song);
            lastName = null;
            return true;
        }

        /// <summary>一番上だけ全文、ほかは 1 つ上の段との差分（前後の共通部分を除いた中身）で持つ積み重ね。</summary>
        sealed class DeltaStack
        {
            sealed class Entry
            {
                public string full;   // 一番上のときだけ
                public int prefix, suffix;
                public string middle;
                public int Size => full?.Length ?? middle.Length;
            }

            readonly List<Entry> items = new List<Entry>();

            public int Count => items.Count;

            public long StoredChars
            {
                get
                {
                    long n = 0;
                    foreach (var e in items) n += e.Size;
                    return n;
                }
            }

            public void Clear() => items.Clear();

            /// <summary>一番古い段を捨てる（その 1 つ上の段は自分より上を基準にしているので影響しない）。</summary>
            public void RemoveOldest() => items.RemoveAt(0);

            public void Push(string json)
            {
                if (items.Count > 0) ToDelta(items[items.Count - 1], json);
                items.Add(new Entry { full = json });
            }

            public string Pop()
            {
                var top = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (items.Count > 0)
                {
                    var next = items[items.Count - 1];
                    next.full = top.full.Substring(0, next.prefix) + next.middle + top.full.Substring(top.full.Length - next.suffix);
                    next.middle = null;
                }
                return top.full;
            }

            /// <summary><paramref name="e"/> の全文を、上に積む <paramref name="above"/> との差分に置き換える。</summary>
            static void ToDelta(Entry e, string above)
            {
                string s = e.full;
                int max = Math.Min(s.Length, above.Length);
                int p = 0;
                while (p < max && s[p] == above[p]) p++;
                int q = 0;
                while (q < max - p && s[s.Length - 1 - q] == above[above.Length - 1 - q]) q++;
                e.prefix = p;
                e.suffix = q;
                e.middle = s.Substring(p, s.Length - p - q);
                e.full = null;
            }
        }
    }
}
