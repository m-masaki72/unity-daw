using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// 入力支援（下ゾーン）：キーとスケール補正 / コード進行から伴奏生成 / メロディの続きの提案。
    /// 操作はすべて <see cref="AssistCommands"/> に渡す。
    /// </summary>
    public sealed class AssistPanel : VisualElement
    {
        readonly ComposerContext ctx;
        readonly MenuChip rootChip, modeChip, bassChip, chordChip, drumChip;
        readonly ToggleChip fillChip;
        readonly TextField chordField;
        readonly Label chordInfo;
        readonly NumberBox rowsBox, tempBox;
        readonly Button rerollButton;
        readonly MenuChip aiTargetChip, aiModelChip, aiBackendChip;
        readonly TextField aiPrompt, keyField;
        readonly Button aiSend, aiCancel;
        readonly Label aiStatus, keyInfo;
        readonly Button[] candButtons = new Button[ClaudePrompt.CandidateCount + 1];
        readonly VisualElement candRow;
        readonly VisualElement keyRow;

        AssistCommands A => ctx.Assist;

        public AssistPanel(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("cd-assist");

            // ---- キー
            var key = Section("Key / Scale");
            var keyRow = Row(key);
            rootChip = new MenuChip(a => ShowMenu(a, Scale.RootNames, A.Key.root, i => A.SetKey(i, A.Key.IsChromatic ? ScaleMode.Major : A.Key.mode)), "主音");
            modeChip = new MenuChip(a => ShowMenu(a, Scale.ModeNames, (int)A.Key.mode, i => A.SetKey(A.Key.root, (ScaleMode)i)), "スケール（Chromatic = 指定なし）");
            keyRow.Add(rootChip);
            keyRow.Add(modeChip);
            key.Add(ComposerContext.ButtonRow(
                new Button(() => { A.ConformToScale(false); ctx.FocusMain(); }) { text = "スケールに合わせる", tooltip = "ピアノロールの選択（なければカーソルのチャンネル）のスケール外の音を最寄りの音へ" },
                new Button(() => { A.ConformToScale(true); ctx.FocusMain(); }) { text = "パターン全体", tooltip = "パターンの全チャンネル（ノイズ以外）をスケールに合わせる" }));

            // ---- コード・伴奏
            var acc = Section("Chords → 伴奏");
            chordField = new TextField { isDelayed = true, tooltip = "コード進行（パターンに均等割り付け）。例: C G Am F / I V vi IV / Dm7 G7 Cmaj7 %  （% = 直前の繰り返し）" };
            chordField.AddToClassList("cd-assist-chords");
            chordField.RegisterValueChangedCallback(e => A.SetChords(e.newValue));
            acc.Add(chordField);
            var partRow = Row(acc);
            bassChip = new MenuChip(a => ShowMenu(a, Accompaniment.BassNames, (int)ctx.State.assistBass, i => A.SetBassStyle((BassStyle)i)), "ベース（Triangle）");
            chordChip = new MenuChip(a => ShowMenu(a, Accompaniment.ChordNames, (int)ctx.State.assistChord, i => A.SetChordStyle((ChordStyle)i)), "和音（Pulse 2）");
            drumChip = new MenuChip(a => ShowMenu(a, Accompaniment.DrumNames, (int)ctx.State.assistDrums, i => A.SetDrumStyle((DrumStyle)i)), "ドラム（Noise）");
            fillChip = new ToggleChip("Fill", A.SetFill, "最後の 1 拍をスネアのフィルに");
            partRow.Add(bassChip);
            partRow.Add(chordChip);
            partRow.Add(drumChip);
            partRow.Add(fillChip);
            acc.Add(ComposerContext.ButtonRow(
                new Button(() => { A.GenerateAccompaniment(); ctx.FocusMain(); }) { text = "伴奏を生成", tooltip = "Pulse 2 / Triangle / Noise を作り直す（「なし」のパートはそのまま。Ctrl+Z で戻せます）" }));
            chordInfo = new Label();
            chordInfo.AddToClassList("cd-assist-info");
            acc.Add(chordInfo);

            // ---- メロディ
            var mel = Section("Melody");
            var melRow = Row(mel);
            rowsBox = new NumberBox("Rows", 1, Pattern.MaxRows, 1, "0", v => A.SetRows((int)v), "提案する長さ（行）");
            tempBox = new NumberBox("ばらつき", 0.2f, 2f, 0.1f, "0.0", A.SetTemperature, "小さいほど曲の傾向どおり、大きいほど冒険");
            melRow.Add(rowsBox);
            melRow.Add(tempBox);
            rerollButton = new Button(() => { A.ContinueMelody(true); ctx.FocusMain(); }) { text = "別案", tooltip = "直前の提案を作り直す" };
            mel.Add(ComposerContext.ButtonRow(
                new Button(() => { A.ContinueMelody(false); ctx.FocusMain(); }) { text = "続きを提案", tooltip = "カーソルのチャンネルで、選択の直後（なければカーソル行）からメロディを提案。曲中のフレーズから学習します" },
                rerollButton));
            var hint = new Label("曲の中のフレーズから音の動きとリズムを学びます。キーとコードを入れると、より音楽的になりますわ。");
            hint.AddToClassList("cd-assist-info");
            mel.Add(hint);

            // ---- Claude
            var ai = Section("Claude");
            aiPrompt = new TextField { multiline = true, tooltip = "頼みたいこと。例: サビっぽく盛り上げて / 切なく、ゆったり / 前のパターンの動機を発展させて" };
            aiPrompt.AddToClassList("cd-assist-prompt");
            aiPrompt.RegisterValueChangedCallback(e => C.SetPrompt(e.newValue));
            ai.Add(aiPrompt);
            var aiRow = Row(ai);
            aiTargetChip = new MenuChip(a => ShowMenu(a, ClaudePrompt.TargetNames, (int)ctx.State.aiTarget, i => C.SetTarget((AiTarget)i)), "頼む内容（メロディはカーソルのチャンネル、選択があればその範囲・なければカーソルから Rows 行）");
            aiModelChip = new MenuChip(a => ShowMenu(a, ClaudePrompt.ModelNames, ctx.State.aiModel, C.SetModel), "使うモデル");
            aiRow.Add(aiTargetChip);
            aiRow.Add(aiModelChip);
            aiBackendChip = new MenuChip(a => ShowMenu(a, ClaudeClient.BackendNames, (int)ClaudeClient.Backend, i => { ClaudeClient.Backend = (ClaudeBackend)i; Refresh(); }),
                "接続方法：自動（API キーがあれば API、なければローカルの Claude Code）/ Claude Code（claude -p、ログイン済みのアカウントを使う）/ API キー");
            aiRow.Add(aiBackendChip);
            aiSend = new Button(() => { C.Request(); ctx.FocusMain(); }) { text = "Claude に頼む", tooltip = "曲の内容と頼みごとを送り、提案を書き込みます（Ctrl+Z で戻せます）" };
            aiCancel = new Button(C.Cancel) { text = "中止" };
            var keyButton = new Button(() => keyRow.style.display = keyRow.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None) { text = "API キー…" };
            ai.Add(ComposerContext.ButtonRow(aiSend, aiCancel, keyButton));
            keyRow = Row(ai);
            keyRow.style.display = DisplayStyle.None;
            keyField = new TextField { isPasswordField = true, tooltip = "Anthropic の API キー。この PC の EditorPrefs に保存し、プロジェクトには書きません" };
            keyField.AddToClassList("cd-assist-key");
            keyRow.Add(keyField);
            keyRow.Add(new Button(() => { ClaudeClient.StoredKey = keyField.value; keyField.SetValueWithoutNotify(""); keyRow.style.display = DisplayStyle.None; Refresh(); }) { text = "保存" });
            keyRow.Add(new Button(() => { ClaudeClient.StoredKey = ""; Refresh(); }) { text = "消去" });
            aiStatus = new Label();
            aiStatus.AddToClassList("cd-assist-info");
            ai.Add(aiStatus);
            // 候補の切り替え（元 / 案1..）と試聴
            candRow = Row(ai);
            for (int i = 0; i < candButtons.Length; i++)
            {
                int k = i - 1;
                var b = new Button(() => { C.Choose(k); ctx.FocusMain(); })
                {
                    text = k < 0 ? "元" : $"案{k + 1}",
                    tooltip = k < 0 ? "依頼前の内容に戻す" : $"候補 {k + 1} を書き込む（切り替えるたびに Ctrl+Z で戻せます）",
                };
                b.AddToClassList("cd-assist-cand");
                candButtons[i] = b;
                candRow.Add(b);
            }
            candRow.Add(new Button(() => { ctx.Transport.PlayPatternFrom(C.AuditionRow); ctx.FocusMain(); }) { text = "▶ 試聴", tooltip = "提案した範囲の頭からパターンを鳴らす" });
            keyInfo = new Label();
            keyInfo.AddToClassList("cd-assist-info");
            ai.Add(keyInfo);
        }

        ClaudeAssist C => A.Claude;

        VisualElement Section(string title)
        {
            var sec = new VisualElement();
            sec.AddToClassList("cd-assist-sec");
            sec.Add(ComposerContext.Title(title));
            Add(sec);
            return sec;
        }

        static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("cd-assist-row");
            parent.Add(row);
            return row;
        }

        static void ShowMenu(VisualElement anchor, string[] names, int current, Action<int> pick)
        {
            var m = new GenericMenu();
            for (int i = 0; i < names.Length; i++)
            {
                int k = i;
                m.AddItem(new UnityEngine.GUIContent(names[i]), i == current, () => pick(k));
            }
            m.DropDown(anchor.worldBound);
        }

        public void Refresh()
        {
            var st = ctx.State;
            bool has = st.song != null && st.CurrentPattern != null;
            SetEnabled(has);
            var key = A.Key;
            rootChip.Text = key.IsChromatic ? "—" : Scale.RootNames[key.root];
            modeChip.Text = Scale.ModeNames[(int)key.mode];
            bassChip.Text = "Bass: " + Accompaniment.BassNames[(int)st.assistBass];
            chordChip.Text = "Chord: " + Accompaniment.ChordNames[(int)st.assistChord];
            drumChip.Text = "Drums: " + Accompaniment.DrumNames[(int)st.assistDrums];
            fillChip.Value = st.assistFill;
            rowsBox.SetValueWithoutNotify(st.assistRows);
            tempBox.SetValueWithoutNotify(st.assistTemperature);
            rerollButton.SetEnabled(A.CanReroll);
            aiTargetChip.Text = ClaudePrompt.TargetNames[(int)st.aiTarget];
            aiModelChip.Text = ClaudePrompt.ModelNames[System.Math.Max(0, System.Math.Min(ClaudePrompt.ModelNames.Length - 1, st.aiModel))];
            if (aiPrompt.focusController?.focusedElement != aiPrompt) aiPrompt.SetValueWithoutNotify(st.aiPrompt ?? "");
            aiSend.SetEnabled(!C.Busy);
            aiCancel.SetEnabled(C.Busy);
            aiStatus.text = C.Status;
            candRow.style.display = C.CandidateCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; i < candButtons.Length; i++)
            {
                int k = i - 1;
                candButtons[i].SetEnabled(!C.Busy && k < C.CandidateCount);
                candButtons[i].EnableInClassList("on", k == C.Chosen);
                if (k >= 0) candButtons[i].tooltip = C.CandidateComment(k);
            }
            aiStatus.EnableInClassList("error", C.StatusIsError);
            aiBackendChip.Text = "接続: " + ClaudeClient.BackendNames[(int)ClaudeClient.Backend];
            keyInfo.text = ClaudeClient.Describe();
            if (!has) return;

            if (chordField.focusController?.focusedElement != chordField) chordField.SetValueWithoutNotify(A.ChordText);
            var chords = A.Chords(out string error);
            chordInfo.text = error ?? (chords.Count == 0
                ? "例: C G Am F / I V vi IV（ローマ数字はキー基準）"
                : $"{chords.Count} コード × {Math.Max(1, st.CurrentPattern.length / chords.Count)} 行: " + string.Join(" ", chords.ConvertAll(c => c.Name)));
            chordInfo.EnableInClassList("error", error != null);
        }
    }
}
