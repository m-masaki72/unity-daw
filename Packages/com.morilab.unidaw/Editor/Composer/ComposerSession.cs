using System;
using UnityEditor;
using UnityEngine;

namespace UniDAW.Editor
{
    /// <summary>
    /// コンポーザーの編集セッション：曲・カーソル・パターン/順序表の選択と、Undo 付きの編集操作。
    /// UI には依存せず、変化は <see cref="Changed"/>、利用者への通知は <see cref="Notified"/> で伝える。
    /// </summary>
    public sealed class ComposerSession
    {
        public delegate void CellEdit(ref Cell c);

        public ComposerState State { get; }
        public Transport Transport { get; }

        /// <summary>表示を更新すべき変化があった。</summary>
        public event Action Changed;
        /// <summary>利用者に知らせたいメッセージ（読み取り専用の警告など）。</summary>
        public event Action<string> Notified;

        public ComposerSession(ComposerState state, Transport transport)
        {
            State = state;
            Transport = transport;
        }

        public Song Song => State.song;
        public Pattern CurrentPattern => State.CurrentPattern;
        public int CursorRow => State.cursorRow;
        public int CursorChannel => State.cursorCh;
        public int CursorCol => State.cursorCol;
        public bool IsReadOnly => UniDAWAssets.IsReadOnly(State.song);

        public void NotifyChanged() => Changed?.Invoke();
        public void Notify(string message) => Notified?.Invoke(message);

        // ---- 曲・選択 --------------------------------------------------------------

        public void SetSong(Song s)
        {
            State.song = s;
            if (s != null) s.Validate();
            State.Clamp();
            Transport.Attach(s);
            Changed?.Invoke();
        }

        /// <summary>Undo/Redo 後：データの整合を取り直して再表示。</summary>
        public void OnUndoRedo()
        {
            if (Song == null) return;
            Song.Validate();
            State.Clamp();
            Changed?.Invoke();
        }

        public void SetCursor(int row, int ch, int col)
        {
            if (Song == null) return;
            State.cursorRow = row;
            State.cursorCh = ch;
            State.cursorCol = col;
            State.Clamp();
            UniDAWMidiBridge.TargetChannel = State.cursorCh;
            Changed?.Invoke();
        }

        /// <summary>ノート入力後などにカーソルを編集ステップ分進める。</summary>
        public void Advance()
        {
            var p = CurrentPattern;
            if (p == null || State.editStep <= 0) return;
            State.cursorRow = (State.cursorRow + State.editStep) % p.length;
            Changed?.Invoke();
        }

        public void SelectPattern(int idx)
        {
            if (Song == null) return;
            State.patternIndex = Mathf.Clamp(idx, 0, Song.patterns.Count - 1);
            State.Clamp();
            Changed?.Invoke();
        }

        public void SelectOrder(int idx)
        {
            if (Song == null) return;
            State.orderIndex = Mathf.Clamp(idx, 0, Song.order.Count - 1);
            State.patternIndex = Song.order[State.orderIndex];
            State.Clamp();
            Changed?.Invoke();
        }

        /// <summary>頭出し（順序表の先頭・行 0）。再生中なら先頭から鳴らし直す。</summary>
        public void GoToStart()
        {
            if (Song == null) return;
            State.orderIndex = 0;
            State.patternIndex = Song.order[0];
            State.cursorRow = 0;
            State.Clamp();
            if (Transport.IsPlaying) Transport.Start();
            Changed?.Invoke();
        }

        /// <summary>順序表の前後へ移動（テンキー − / ＋）。再生中ならそこから鳴らし直す。</summary>
        public void StepOrder(int delta)
        {
            if (Song == null) return;
            SelectOrder(State.orderIndex + delta);
            if (Transport.IsPlaying) Transport.Start();
        }

        /// <summary>再生追従：再生中の位置へ表示を移す。</summary>
        public void FollowPlayback(int pattern, int order)
        {
            if (Song == null) return;
            State.patternIndex = pattern;
            State.orderIndex = Mathf.Clamp(order, 0, Song.order.Count - 1);
            State.Clamp();
            Changed?.Invoke();
        }

        // ---- 表示・入力設定 --------------------------------------------------------

        public void SetOctave(int o)
        {
            State.octave = Mathf.Clamp(o, 0, ComposerState.MaxOctave);
            Changed?.Invoke();
        }

        public void SetEditStep(int s)
        {
            State.editStep = Mathf.Clamp(s, 0, ComposerState.MaxEditStep);
            Changed?.Invoke();
        }

        public void SetFollow(bool on) => State.follow = on;

        public void SetRecording(bool on)
        {
            State.recording = on;
            Changed?.Invoke();
        }

        public void SetView(PatternViewKind kind)
        {
            if (State.view == kind) return;
            State.view = kind;
            Changed?.Invoke();
        }

        public void SetRollTool(RollTool tool)
        {
            if (State.rollTool == tool) return;
            State.rollTool = tool;
            Changed?.Invoke();
        }

        public void SetKeyPreset(DawKeyPreset preset)
        {
            DawKeys.Preset = preset;
            Changed?.Invoke();
        }

        /// <summary>下ゾーンの開閉（Cubase: Ctrl+Alt+E）。</summary>
        public void ToggleLowerZone()
        {
            State.lowerOpen = !State.lowerOpen;
            Changed?.Invoke();
        }

        /// <summary>下ゾーンのタブを表示。すでにそのタブが開いていれば閉じる（F3 のミキサー切替と同じ挙動）。</summary>
        public void ToggleLowerTab(LowerTab tab)
        {
            if (State.lowerOpen && State.lowerTab == tab) State.lowerOpen = false;
            else ShowLowerTab(tab);
            Changed?.Invoke();
        }

        public void ShowLowerTab(LowerTab tab)
        {
            State.lowerOpen = true;
            State.lowerTab = tab;
            Changed?.Invoke();
        }

        public void SetMacro(MacroKind kind)
        {
            if (State.macro == kind) return;
            State.macro = kind;
            Changed?.Invoke();
        }

        public void CycleView()
        {
            int n = Enum.GetValues(typeof(PatternViewKind)).Length;
            State.view = (PatternViewKind)(((int)State.view + 1) % n);
            Changed?.Invoke();
        }

        // ---- 編集の土台 ------------------------------------------------------------

        public bool CanEdit()
        {
            if (Song == null) return false;
            if (IsReadOnly)
            {
                Notify("読み取り専用の Song ですわ（編集用に複製してくださいませ）");
                return false;
            }
            return true;
        }

        /// <summary>Undo 記録 → 変更 → Dirty → 状態補正 → 通知、の共通手順。</summary>
        bool Edit(string undoName, Action change)
        {
            if (!CanEdit()) return false;
            Undo.RecordObject(Song, undoName);
            change();
            EditorUtility.SetDirty(Song);
            State.Clamp();
            Changed?.Invoke();
            return true;
        }

        public void ModifySong(string undoName, Action<Song> change) => Edit(undoName, () => change(Song));

        public void ModifyPattern(string undoName, Action<Pattern> change) => Edit(undoName, () => change(CurrentPattern));

        public void ModifyCell(string undoName, CellEdit change) =>
            ModifyCellAt(State.patternIndex, State.cursorCh, State.cursorRow, undoName, change);

        /// <summary>任意位置のセルを編集。ポリメーターでは表示・再生と同じ（折り返した）セルへ書く。</summary>
        public void ModifyCellAt(int pat, int ch, int row, string undoName, CellEdit change, int loop = 0)
        {
            var p = Song != null ? Song.GetPattern(pat) : null;
            if (p == null || row < 0 || row >= p.length) return;
            Edit(undoName, () => change(ref p.tracks[ch].cells[p.CellIndex(ch, row, loop)]));
        }

        // ---- 曲の設定 --------------------------------------------------------------

        public void SetBpm(float bpm) => ModifySong("BPM", s => s.bpm = Mathf.Clamp(bpm, 20, 400));
        public void SetRowsPerBeat(int rpb) => ModifySong("Rows/Beat", s => s.rowsPerBeat = Mathf.Clamp(rpb, 1, 16));
        public void SetSpeed(int speed) => ModifySong("Speed", s => s.speed = Mathf.Clamp(speed, 1, 31));

        // ---- パターン --------------------------------------------------------------

        public void RenamePattern(string name) => ModifyPattern("Rename Pattern", p => p.name = name);
        public void ResizePattern(int length) => ModifyPattern("Pattern Length", p => p.Resize(length));

        /// <summary>トラック長（0 またはパターン長以上 = パターン長）。パターン長より短いとポリメーター。</summary>
        public void SetTrackLength(int ch, int len)
        {
            ModifyPattern("Track Length", p =>
            {
                int l = Mathf.Clamp(len, 0, p.length);
                p.tracks[ch].length = l >= p.length ? 0 : l;
            });
        }

        public void NewPattern()
        {
            int len = CurrentPattern?.length ?? 64;
            ModifySong("New Pattern", s =>
            {
                s.patterns.Add(new Pattern(len, $"P{s.patterns.Count:X2}"));
                State.patternIndex = s.patterns.Count - 1;
            });
        }

        public void ClonePattern()
        {
            ModifySong("Clone Pattern", s =>
            {
                var c = CurrentPattern.Clone();
                c.name += "'";
                s.patterns.Add(c);
                State.patternIndex = s.patterns.Count - 1;
            });
        }

        // ---- 順序表 ----------------------------------------------------------------

        /// <summary>順序表の編集。終わったら選択中エントリのパターンを表示する。</summary>
        void OrderEdit(string undoName, Action<Song> change) => Edit(undoName, () =>
        {
            change(Song);
            State.orderIndex = Mathf.Clamp(State.orderIndex, 0, Song.order.Count - 1);
            State.patternIndex = Song.order[State.orderIndex];
        });

        public void InsertOrderEntry() => OrderEdit("Add Order", s =>
        {
            s.InsertOrder(State.orderIndex + 1, State.patternIndex, s.TransposeAtOrder(State.orderIndex));
            State.orderIndex++;
        });

        public void RemoveOrderEntry() => OrderEdit("Remove Order", s =>
        {
            if (s.order.Count > 1) s.RemoveOrder(State.orderIndex);
        });

        public void MoveOrderEntry(int delta) => OrderEdit("Move Order", s =>
        {
            int a = State.orderIndex, b = a + delta;
            if (b < 0 || b >= s.order.Count) return;
            s.SwapOrder(a, b);
            State.orderIndex = b;
        });

        /// <summary>エントリを別の位置へ移す（アレンジ表示のドラッグ）。移調も一緒に動く。</summary>
        public void MoveOrderTo(int from, int to)
        {
            if (Song == null || from == to || from < 0 || from >= Song.order.Count) return;
            OrderEdit("Move Order", s =>
            {
                int pat = s.order[from], tr = s.TransposeAtOrder(from);
                s.RemoveOrder(from);
                int dst = Mathf.Clamp(to, 0, s.order.Count);
                s.InsertOrder(dst, pat, tr);
                State.orderIndex = dst;
            });
        }

        /// <summary>エントリをすぐ後ろに複製する（同じパターンを共有）。</summary>
        public void DuplicateOrderEntry(int index)
        {
            if (Song == null || index < 0 || index >= Song.order.Count) return;
            OrderEdit("Duplicate Order", s =>
            {
                s.InsertOrder(index + 1, s.order[index], s.TransposeAtOrder(index));
                State.orderIndex = index + 1;
            });
        }

        public void RemoveOrderEntryAt(int index)
        {
            if (Song == null || Song.order.Count <= 1 || index < 0 || index >= Song.order.Count) return;
            OrderEdit("Remove Order", s =>
            {
                s.RemoveOrder(index);
                State.orderIndex = Mathf.Min(index, s.order.Count - 1);
            });
        }

        /// <summary>エントリのパターンを複製して、このエントリだけの別パターンにする（他の出現は変わらない）。</summary>
        public void MakeOrderUnique(int index)
        {
            if (Song == null || index < 0 || index >= Song.order.Count) return;
            OrderEdit("Make Unique", s =>
            {
                var c = s.GetPattern(s.order[index]).Clone();
                c.name += "'";
                s.patterns.Add(c);
                s.order[index] = s.patterns.Count - 1;
                State.orderIndex = index;
            });
        }

        public void SetOrderPattern(int index, int pattern)
        {
            if (Song == null || index < 0 || index >= Song.order.Count) return;
            OrderEdit("Order Pattern", s =>
            {
                s.order[index] = Mathf.Clamp(pattern, 0, s.patterns.Count - 1);
                State.orderIndex = index;
            });
        }

        public void TransposeOrderAt(int index, int? delta)
        {
            if (Song == null || index < 0 || index >= Song.order.Count) return;
            OrderEdit("Order Transpose", s =>
            {
                s.SetOrderTranspose(index, delta.HasValue ? s.TransposeAtOrder(index) + delta.Value : 0);
                State.orderIndex = index;
            });
        }

        public void SetArrangeZoom(float zoom)
        {
            State.arrangeZoom = Mathf.Clamp(zoom, ComposerState.MinArrangeZoom, ComposerState.MaxArrangeZoom);
            Changed?.Invoke();
        }

        public void ShiftOrderPattern(int delta) => OrderEdit("Order Pattern", s =>
            s.order[State.orderIndex] = Mathf.Clamp(s.order[State.orderIndex] + delta, 0, s.patterns.Count - 1));

        /// <summary>エントリ単位の移調（パターンは共有のまま）。null で移調なしに戻す。</summary>
        public void TransposeOrderEntry(int? delta) => OrderEdit("Order Transpose", s =>
            s.SetOrderTranspose(State.orderIndex, delta.HasValue ? s.TransposeAtOrder(State.orderIndex) + delta.Value : 0));

        // ---- インストゥルメント ----------------------------------------------------

        /// <summary>チャンネルで入力に使うインストゥルメント番号（MIDI 入力と共有）。</summary>
        public int ChannelInstrument(int ch) => UniDAWMidiBridge.ChannelInstrument[ch];

        public Instrument CursorInstrument => Song != null ? Song.GetInstrument(ChannelInstrument(State.cursorCh)) : null;

        public void SetChannelInstrument(int ch, int inst)
        {
            if (inst >= 0) UniDAWMidiBridge.ChannelInstrument[ch] = inst;
            Changed?.Invoke();
        }

        public void ModifyInstrument(string undoName, Action<Instrument> change)
        {
            var inst = CursorInstrument;
            if (inst != null) Edit(undoName, () => change(inst));
        }

        /// <summary>編集中の楽器のマクロを丸ごと差し替える（グラフの 1 ストロークで Undo 1 回）。</summary>
        public void SetMacro(MacroKind kind, Sequence seq) =>
            ModifyInstrument("Edit " + kind + " Macro", i => i.SetMacro(kind, seq));

        public void AddInstrument(bool duplicate)
        {
            var src = CursorInstrument;
            ModifySong("Add Instrument", s =>
            {
                var inst = duplicate && src != null ? src.Clone() : new Instrument("New", "15 | 12", "", "2");
                if (duplicate) inst.name += " copy";
                s.instruments.Add(inst);
                UniDAWMidiBridge.ChannelInstrument[State.cursorCh] = s.instruments.Count - 1;
            });
        }

        /// <summary>カーソルのチャンネルの楽器を試聴する。</summary>
        public void Audition()
        {
            int ch = State.cursorCh;
            int note = Song.Kinds[ch] == ChannelKind.Noise ? 9 : (State.octave + 1) * 12;
            Transport.PreviewNote(ch, note, ChannelInstrument(ch), 0.4);
        }

        // ---- 表示用の問い合わせ ----------------------------------------------------

        /// <summary>カーソルがエフェクト列にあるとき、その効果の説明（なければ一覧）。</summary>
        public string FxHelpAtCursor()
        {
            if (State.CellMode || CurrentPattern == null || State.cursorCol < 3) return null;
            var cell = State.CursorCell;
            return FxText.Help(State.cursorCol <= 4 ? cell.fx1.type : cell.fx2.type);
        }
    }
}
