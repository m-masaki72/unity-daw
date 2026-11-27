using System;
using UnityEngine;

namespace UniDAW.Composer
{
    /// <summary>
    /// 順序表（アレンジ）の編集。すべて Undo 付きで、終わったら操作したエントリを選択してそのパターンを表示する。
    /// キー操作は「選択中のエントリ」、アレンジ帯は「エントリ番号」を指定して同じ操作を使う。
    /// </summary>
    public sealed class OrderCommands
    {
        readonly ComposerSession session;

        public OrderCommands(ComposerSession session) => this.session = session;

        Song Song => session.Song;
        ComposerState State => session.State;
        int Selected => State.orderIndex;

        bool Valid(int index) => Song != null && index >= 0 && index < Song.order.Count;

        /// <summary>編集して、<paramref name="change"/> が返したエントリ番号を選択する。</summary>
        void Edit(string undoName, Func<Song, int> change) => session.ModifySong(undoName, s =>
        {
            int sel = Mathf.Clamp(change(s), 0, s.order.Count - 1);
            State.orderIndex = sel;
            State.patternIndex = s.order[sel];
        });

        // ---- 選択中のエントリ（キー操作・アレンジ帯のボタン） ------------------------------------------

        /// <summary>現在のパターンを選択中エントリの後ろに挿入（移調は引き継ぐ）。</summary>
        public void Insert()
        {
            if (Song == null) return;
            Edit("Add Order", s =>
            {
                s.InsertOrder(Selected + 1, State.patternIndex, s.TransposeAtOrder(Selected));
                return Selected + 1;
            });
        }

        /// <summary>パターンを順序表の <paramref name="slot"/>（0..Count）に並べる（パターン一覧からのドラッグ）。</summary>
        public void InsertPattern(int slot, int pattern)
        {
            if (Song == null || pattern < 0 || pattern >= Song.patterns.Count) return;
            Edit("Add Order", s =>
            {
                int at = Mathf.Clamp(slot, 0, s.order.Count);
                s.InsertOrder(at, pattern);
                return at;
            });
        }

        public void RemoveSelected() => Remove(Selected);
        public void MoveSelected(int delta) => Move(Selected, Selected + delta);
        public void ShiftSelectedPattern(int delta)
        {
            if (Valid(Selected)) SetPattern(Selected, Song.order[Selected] + delta);
        }
        public void TransposeSelected(int? delta) => Transpose(Selected, delta);

        // ---- 番号指定（アレンジ表示） ------------------------------------------------

        /// <summary>エントリを別の位置へ移す（<paramref name="to"/> は取り除いた後の位置）。移調も一緒に動く。</summary>
        public void Move(int from, int to)
        {
            if (!Valid(from)) return;
            to = Mathf.Clamp(to, 0, Song.order.Count - 1);
            if (to == from) return;
            Edit("Move Order", s =>
            {
                int pat = s.order[from], tr = s.TransposeAtOrder(from);
                s.RemoveOrder(from);
                s.InsertOrder(to, pat, tr);
                return to;
            });
        }

        /// <summary>エントリをすぐ後ろに複製する（同じパターンを共有）。</summary>
        public void Duplicate(int index)
        {
            if (!Valid(index)) return;
            Edit("Duplicate Order", s =>
            {
                s.InsertOrder(index + 1, s.order[index], s.TransposeAtOrder(index));
                return index + 1;
            });
        }

        /// <summary>エントリを外す（パターンは残る）。最後の 1 つは消さない。</summary>
        public void Remove(int index)
        {
            if (!Valid(index) || Song.order.Count <= 1) return;
            Edit("Remove Order", s =>
            {
                s.RemoveOrder(index);
                return Math.Min(index, s.order.Count - 1);
            });
        }

        /// <summary>エントリのパターンを複製して、このエントリだけの別パターンにする（他の出現は変わらない）。</summary>
        public void MakeUnique(int index)
        {
            if (!Valid(index)) return;
            Edit("Make Unique", s =>
            {
                var c = s.GetPattern(s.order[index]).Clone();
                c.name += "'";
                s.patterns.Add(c);
                s.order[index] = s.patterns.Count - 1;
                return index;
            });
        }

        public void SetPattern(int index, int pattern)
        {
            if (!Valid(index)) return;
            Edit("Order Pattern", s =>
            {
                s.order[index] = Mathf.Clamp(pattern, 0, s.patterns.Count - 1);
                return index;
            });
        }

        /// <summary>エントリ単位の移調（パターンは共有のまま）。null で移調なしに戻す。</summary>
        public void Transpose(int index, int? delta)
        {
            if (!Valid(index)) return;
            Edit("Order Transpose", s =>
            {
                s.SetOrderTranspose(index, delta.HasValue ? s.TransposeAtOrder(index) + delta.Value : 0);
                return index;
            });
        }
    }
}
