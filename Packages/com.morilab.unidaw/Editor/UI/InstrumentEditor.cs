using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// 楽器エディタ（下ゾーン）：左に楽器一覧、右にマクロ（Volume / Arp / Pitch / Duty）のグラフとテキスト。
    /// 編集対象はカーソルのチャンネルで入力に使う楽器（一覧のクリックで切り替え）。
    /// </summary>
    public sealed class InstrumentEditor : VisualElement
    {
        static readonly string[] MacroNames = { "Volume", "Arp", "Pitch", "Duty" };
        static readonly string[] MacroHelp =
        {
            "音量 0〜15。tick ごとに 1 つ進む",
            "半音オフセット（-24〜24）。| 0 4 7 で和音アルペジオ",
            "音程の微調整（1/16 半音、tick ごとに積み上がる）。正で上昇、負で下降",
            "デューティ 0〜3（12.5 / 25 / 50 / 75%）。ノイズは 1 で短周期、三角波は無効",
        };
        static readonly Color[] MacroColors =
        {
            new Color(0.45f, 0.65f, 1f), new Color(1f, 0.75f, 0.35f), new Color(0.5f, 0.9f, 0.6f), new Color(0.85f, 0.55f, 1f),
        };

        readonly ComposerContext ctx;
        readonly ScrollView list, presetList;
        readonly SegmentedControl sourceSeg;
        readonly VisualElement songButtons, presetButtons;
        readonly Button deleteButton;
        bool showPresets;
        List<InstrumentPreset> presets;
        int presetSel = -1;
        readonly TextField nameField, seqField;
        readonly SegmentedControl macroSeg;
        readonly MacroGraph graph;
        readonly Label info;
        int listCount = -1;

        public InstrumentEditor(ComposerContext ctx)
        {
            this.ctx = ctx;
            var s = ctx.Session;
            AddToClassList("cd-insted");

            // ---- 左：楽器一覧
            var left = new VisualElement();
            left.AddToClassList("cd-insted-left");
            sourceSeg = new SegmentedControl(new[] { "Song", "Presets" }, i => { showPresets = i == 1; Refresh(); });
            sourceSeg.AddToClassList("cd-roll-tools");
            left.Add(sourceSeg);
            list = new ScrollView();
            list.AddToClassList("cd-insted-list");
            left.Add(list);
            presetList = new ScrollView();
            presetList.AddToClassList("cd-insted-list");
            left.Add(presetList);
            songButtons = ComposerContext.ButtonRow(
                new Button(() => s.AddInstrument(false)) { text = "New", tooltip = "新しい楽器を追加" },
                new Button(() => s.AddInstrument(true)) { text = "Dup", tooltip = "選択中の楽器を複製" },
                new Button(SaveCurrent) { text = "Save", tooltip = "選択中の楽器をプリセットに保存（同じ名前は上書き）" });
            left.Add(songButtons);
            deleteButton = new Button(DeletePreset) { text = "Del", tooltip = "保存したプリセットを消す（内蔵は消せない）" };
            presetButtons = ComposerContext.ButtonRow(
                new Button(() => s.LoadPreset(SelectedPreset?.instrument)) { text = "Load", tooltip = "選択中の楽器をこのプリセットで置き換える（ダブルクリックでも）" },
                new Button(() => s.AddPreset(SelectedPreset?.instrument)) { text = "Add", tooltip = "新しい楽器として曲に足す" },
                deleteButton);
            left.Add(presetButtons);
            Add(left);

            // ---- 右：マクロ編集
            var right = new VisualElement();
            right.AddToClassList("cd-insted-right");
            var head = new VisualElement();
            head.AddToClassList("cd-insted-head");
            nameField = new TextField { isDelayed = true, tooltip = "楽器名" };
            nameField.AddToClassList("cd-insted-name");
            nameField.RegisterValueChangedCallback(e => s.ModifyInstrument("Rename Instrument", i => i.name = e.newValue));
            head.Add(nameField);
            macroSeg = new SegmentedControl(MacroNames, i => s.SetMacro((MacroKind)i));
            head.Add(macroSeg);
            var audition = new Button(s.Audition) { text = "▶ Audition", tooltip = "カーソルのチャンネルで試聴" };
            head.Add(audition);
            right.Add(head);

            graph = new MacroGraph();
            graph.Committed += seq => s.SetMacro(ctx.State.macro, seq);
            right.Add(graph);

            var foot = new VisualElement();
            foot.AddToClassList("cd-insted-foot");
            seqField = new TextField { isDelayed = true, tooltip = "テキストで直接編集。| = ループ開始、/ = リリース開始" };
            seqField.AddToClassList("cd-insted-seq");
            seqField.RegisterValueChangedCallback(e => s.SetMacro(ctx.State.macro, Sequence.Parse(e.newValue)));
            foot.Add(seqField);
            info = new Label();
            info.AddToClassList("cd-insted-info");
            foot.Add(info);
            right.Add(foot);
            Add(right);
        }

        public void Refresh()
        {
            var song = ctx.State.song;
            if (song == null) return;
            int sel = ctx.Session.ChannelInstrument(ctx.State.cursorCh);
            sourceSeg.SetIndex(showPresets ? 1 : 0);
            Show(list, !showPresets);
            Show(songButtons, !showPresets);
            Show(presetList, showPresets);
            Show(presetButtons, showPresets);
            if (showPresets) RebuildPresets();
            else RebuildList(song, sel);

            var inst = song.GetInstrument(sel);
            var kind = ctx.State.macro;
            macroSeg.SetIndex((int)kind);
            nameField.SetValueWithoutNotify(inst?.name ?? "");
            var seq = inst?.GetMacro(kind) ?? new Sequence();
            Instrument.MacroRange(kind, out int min, out int max);
            graph.SetData(seq, min, max, MacroColors[(int)kind]);
            seqField.SetValueWithoutNotify(seq.ToString());
            info.text = $"{Song.ChannelNames[ctx.State.cursorCh]} の入力楽器 {sel:X2} ・ 長さ {seq.values.Length}\n{MacroHelp[(int)kind]}";
        }

        static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        InstrumentPreset SelectedPreset => presets != null && presetSel >= 0 && presetSel < presets.Count ? presets[presetSel] : null;

        void SaveCurrent()
        {
            var inst = ctx.Session.CursorInstrument;
            if (inst == null) return;
            InstrumentPresets.Save(inst);
            presets = null;
            ctx.Session.Notify($"プリセット「{inst.name}」を保存しましたわ");
        }

        void DeletePreset()
        {
            var p = SelectedPreset;
            if (p == null || !p.user) return;
            InstrumentPresets.Delete(p.Name);
            presets = null;
            presetSel = -1;
            Refresh();
        }

        /// <summary>プリセット一覧。クリックで試聴、ダブルクリックで読み込み。</summary>
        void RebuildPresets()
        {
            if (presets == null)
            {
                presets = InstrumentPresets.All();
                presetList.Clear();
                for (int i = 0; i < presets.Count; i++)
                {
                    int idx = i;
                    var row = new Label((presets[i].user ? "★ " : "") + presets[i].Label);
                    row.AddToClassList("cd-insted-item");
                    row.RegisterCallback<PointerDownEvent>(e =>
                    {
                        if (e.button != 0) return;
                        presetSel = idx;
                        if (e.clickCount >= 2) ctx.Session.LoadPreset(presets[idx].instrument);
                        else ctx.Session.AuditionPreset(presets[idx].instrument);
                        Refresh();
                    });
                    presetList.Add(row);
                }
            }
            for (int i = 0; i < presetList.childCount; i++) presetList[i].EnableInClassList("on", i == presetSel);
            deleteButton.SetEnabled(SelectedPreset?.user == true);
        }

        void RebuildList(Song song, int sel)
        {
            int n = song.instruments.Count;
            if (n != listCount)
            {
                listCount = n;
                list.Clear();
                for (int i = 0; i < n; i++)
                {
                    int idx = i;
                    var row = new Label();
                    row.AddToClassList("cd-insted-item");
                    row.RegisterCallback<PointerDownEvent>(e =>
                    {
                        if (e.button != 0) return;
                        ctx.Session.SetChannelInstrument(ctx.State.cursorCh, idx);
                        if (e.clickCount >= 2) ctx.Session.Audition();
                    });
                    list.Add(row);
                }
            }
            for (int i = 0; i < n; i++)
            {
                var row = (Label)list[i];
                string t = $"{i:X2} {song.instruments[i]?.name}";
                if (row.text != t) row.text = t;
                row.EnableInClassList("on", i == Mathf.Clamp(sel, 0, Math.Max(0, n - 1)));
            }
        }
    }
}
