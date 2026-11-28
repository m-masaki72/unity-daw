using System;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>
    /// 入力支援（右ゾーンの Assist タブ）：スケール補正 / コード進行から伴奏生成 / メロディの続きの提案 / Claude。
    /// キー（主音・スケール）は Inspector タブの Song で決める。
    /// 操作はすべて <see cref="AssistCommands"/> に渡す。
    /// </summary>
    public sealed class AssistPanel : VisualElement
    {
        readonly ComposerContext ctx;
        readonly MenuChip bassChip, chordChip, drumChip;
        readonly ToggleChip fillChip;
        readonly TextField chordField;
        readonly Label chordInfo;
        readonly NumberBox rowsBox, tempBox;
        readonly Button rerollButton;
        readonly MenuChip aiTargetChip, aiModelChip, aiBackendChip;
        readonly TextField aiPrompt, keyField;
        readonly Button aiSend, aiCancel;
        readonly Label aiStatus, keyInfo, scaleInfo;
        readonly Button[] candButtons = new Button[ClaudePrompt.CandidateCount + 1];
        readonly VisualElement candRow;
        readonly VisualElement keyRow;
        readonly Button refineButton, draftNextButton;
        readonly ToggleChip draftAutoChip, draftFinishChip;
        readonly ToggleChip[] lockChips = new ToggleChip[Song.ChannelCount];
        readonly TextField lockBars;
        readonly VisualElement draftRow, critiqueBox, nextRow;
        readonly Label draftInfo, critiqueText;
        ClaudeReply.Suggestion shownCritique;

        AssistCommands A => ctx.Assist;

        public AssistPanel(ComposerContext ctx)
        {
            this.ctx = ctx;
            AddToClassList("cd-assist");

            // ---- スケール補正（キーは Inspector タブの Song で選ぶ）
            var key = Section("Scale");
            scaleInfo = new Label();
            scaleInfo.AddToClassList("cd-assist-info");
            key.Add(scaleInfo);
            key.Add(ComposerContext.ButtonRow(
                new Button(() => { A.ConformToScale(false); ctx.FocusMain(); }) { text = "Conform", tooltip = "ピアノロールの選択（なければカーソルのチャンネル）のスケール外の音を最寄りの音へ" },
                new Button(() => { A.ConformToScale(true); ctx.FocusMain(); }) { text = "Whole Pattern", tooltip = "パターンの全チャンネル（ノイズ以外）をスケールに合わせる" }));

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
            aiTargetChip = new MenuChip(a => ShowMenu(a, ClaudePrompt.TargetLabels(ctx.State.cursorCh), (int)ctx.State.aiTarget, i => C.SetTarget((AiTarget)i)), "頼む内容（メロディ／ベース／ドラムはカーソルのチャンネルで決まる。選択があればその範囲・なければカーソルから Rows 行。曲構成は曲全体のセクションと順序表。ミックスは全チャンネルの音量・パン・EQ・DUCK・送り。マスタリングは曲を解析してマスターチェーンを 3 案、それぞれ目標ラウドネスにそろえて。" +
                "曲の下書きは依頼欄の概要から 曲構成 → セクションごとに各チャンネル の順に作る。音色はカーソルのチャンネルの楽器のマクロを 3 案。講評は曲全体を見て指摘と次の一手）");
            aiModelChip = new MenuChip(a => ShowMenu(a, ClaudePrompt.ModelNames, ctx.State.aiModel, C.SetModel), "使うモデル");
            aiRow.Add(aiTargetChip);
            aiRow.Add(aiModelChip);
            aiBackendChip = new MenuChip(a => ShowMenu(a, ClaudeClient.BackendNames, (int)ClaudeClient.Backend, i => { ClaudeClient.Backend = (ClaudeBackend)i; Refresh(); }),
                "接続方法：自動（API キーがあれば API、なければローカルの Claude Code）/ Claude Code（claude -p、ログイン済みのアカウントを使う）/ API キー");
            aiRow.Add(aiBackendChip);
            aiSend = new Button(() => { C.Request(); ctx.FocusMain(); }) { text = "Claude に頼む", tooltip = "曲の内容と頼みごとを送り、提案を書き込みます（Ctrl+Z で戻せます）" };
            aiCancel = new Button(C.Cancel) { text = "中止", tooltip = "Claude への依頼を取り消す" };
            var keyButton = new Button(() => keyRow.style.display = keyRow.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None) { text = "API キー…", tooltip = "Anthropic の API キーを入力・消去する欄を開く" };
            ai.Add(ComposerContext.ButtonRow(aiSend, aiCancel, keyButton));
            keyRow = Row(ai);
            keyRow.style.display = DisplayStyle.None;
            keyField = new TextField { isPasswordField = true, tooltip = "Anthropic の API キー。この PC の EditorPrefs に保存し、プロジェクトには書きません" };
            keyField.AddToClassList("cd-assist-key");
            keyRow.Add(keyField);
            keyRow.Add(new Button(() => { ClaudeClient.StoredKey = keyField.value; keyField.SetValueWithoutNotify(""); keyRow.style.display = DisplayStyle.None; Refresh(); }) { text = "保存", tooltip = "API キーをこの PC に保存" });
            keyRow.Add(new Button(() => { ClaudeClient.StoredKey = ""; Refresh(); }) { text = "消去", tooltip = "保存した API キーを消す" });
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
            refineButton = new Button(() => { C.Refine(); ctx.FocusMain(); })
            {
                text = "この案を直す",
                tooltip = "いま書き込まれている案を、依頼欄の言葉（例：後半をもっと跳ねて / 最後は主音で終わって）で直した 3 案をもらう。「元」は最初の依頼の前に戻る",
            };
            candRow.Add(refineButton);

            // 書き換えさせないチャンネル・小節
            var lockRow = Row(ai);
            var lockLabel = new Label("固定");
            lockLabel.AddToClassList("cd-assist-info");
            lockRow.Add(lockLabel);
            for (int ch = 0; ch < lockChips.Length; ch++)
            {
                int c = ch;
                lockChips[ch] = new ToggleChip(Song.ChannelNames[ch], on => C.SetChannelLock(c, on), $"ch{ch} {Song.ChannelNames[ch]} を Claude に書き換えさせない（下書きでも飛ばす）");
                lockRow.Add(lockChips[ch]);
            }
            lockBars = new TextField { isDelayed = true, tooltip = "書き換えさせない小節（パターン内の 1 始まり）。例: 1, 3-4。そこにある音は残し、提案はつながるように書かせます" };
            lockBars.AddToClassList("cd-assist-bars");
            lockBars.RegisterValueChangedCallback(e => C.SetLockBars(e.newValue));
            lockRow.Add(lockBars);

            // 曲の下書きの進行
            draftRow = Row(ai);
            draftNextButton = new Button(() => { C.DraftNext(); ctx.FocusMain(); }) { text = "次へ ▶", tooltip = "いまの案で決めて、下書きの次の段を頼む" };
            draftAutoChip = new ToggleChip("おまかせ", C.SetDraftAuto, "オン：最後まで自動で進める / オフ：段ごとに 3 案から選んで「次へ」");
            draftRow.Add(draftNextButton);
            draftRow.Add(draftAutoChip);
            draftFinishChip = new ToggleChip("仕上げまで", C.SetDraftFinish, "オン：下書きの後、続けてミックス・マスタリング・講評まで頼む");
            draftRow.Add(draftFinishChip);
            draftInfo = new Label();
            draftInfo.AddToClassList("cd-assist-info");
            ai.Add(draftInfo);

            // 講評と次の一手
            critiqueBox = new VisualElement();
            ai.Add(critiqueBox);
            critiqueText = new Label();
            critiqueText.AddToClassList("cd-assist-info");
            critiqueBox.Add(critiqueText);
            nextRow = Row(critiqueBox);
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

        void ShowMenu(VisualElement anchor, string[] names, int current, Action<int> pick)
        {
            var m = ctx.NewMenu();
            for (int i = 0; i < names.Length; i++)
            {
                int k = i;
                m.AddItem(names[i], i == current, () => pick(k));
            }
            m.DropDown(anchor.worldBound);
        }

        /// <summary>講評を表示し、次の一手をボタンにする（講評が変わったときだけ作り直す）。</summary>
        void RefreshCritique()
        {
            var cr = C.Critique;
            critiqueBox.style.display = cr != null ? DisplayStyle.Flex : DisplayStyle.None;
            nextRow.SetEnabled(!C.Busy);
            if (cr == shownCritique) return;
            shownCritique = cr;
            nextRow.Clear();
            if (cr == null) return;
            critiqueText.text = "講評: " + cr.comment + (cr.points.Length > 0 ? "\n・" + string.Join("\n・", cr.points) : "");
            for (int i = 0; i < cr.next.Length; i++)
            {
                int k = i;
                var n = cr.next[i];
                var b = new Button(() => { C.RunNext(k); ctx.FocusMain(); }) { text = n.label, tooltip = $"{n.target}: {n.request}" };
                b.AddToClassList("cd-assist-next");
                nextRow.Add(b);
            }
        }

        public void Refresh()
        {
            var st = ctx.State;
            bool has = st.song != null && st.CurrentPattern != null;
            SetEnabled(has);
            var key = A.Key;
            scaleInfo.text = key.IsChromatic
                ? "キー未指定（Chromatic）。Inspector タブの Song でキーを選ぶとスケール外の音を直せます"
                : $"キー: {Scale.RootNames[key.root]} {Scale.ModeNames[(int)key.mode]}（Inspector タブの Song で変更）";
            bassChip.Text = "Bass: " + Accompaniment.BassNames[(int)st.assistBass];
            chordChip.Text = "Chord: " + Accompaniment.ChordNames[(int)st.assistChord];
            drumChip.Text = "Drums: " + Accompaniment.DrumNames[(int)st.assistDrums];
            fillChip.Value = st.assistFill;
            rowsBox.SetValueWithoutNotify(st.assistRows);
            tempBox.SetValueWithoutNotify(st.assistTemperature);
            rerollButton.SetEnabled(A.CanReroll);
            aiTargetChip.Text = ClaudePrompt.TargetLabel(st.aiTarget, st.cursorCh);
            aiModelChip.Text = ClaudePrompt.ModelNames[System.Math.Max(0, System.Math.Min(ClaudePrompt.ModelNames.Length - 1, st.aiModel))];
            if (aiPrompt.focusController?.focusedElement != aiPrompt) aiPrompt.SetValueWithoutNotify(st.aiPrompt ?? "");
            aiSend.SetEnabled(!C.Busy);
            aiCancel.SetEnabled(C.Busy || C.Drafting);
            refineButton.SetEnabled(C.CanRefine);
            for (int ch = 0; ch < lockChips.Length; ch++) lockChips[ch].Value = C.IsChannelLocked(ch);
            if (lockBars.focusController?.focusedElement != lockBars) lockBars.SetValueWithoutNotify(st.aiLockBars ?? "");
            bool draftUi = C.Drafting || st.aiTarget == AiTarget.Draft;
            draftRow.style.display = draftUi ? DisplayStyle.Flex : DisplayStyle.None;
            draftNextButton.SetEnabled(C.Drafting && !C.Busy);
            draftAutoChip.Value = st.aiDraftAuto;
            draftFinishChip.Value = st.aiDraftFinish;
            draftInfo.text = C.DraftProgress;
            draftInfo.style.display = C.Drafting ? DisplayStyle.Flex : DisplayStyle.None;
            RefreshCritique();
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
