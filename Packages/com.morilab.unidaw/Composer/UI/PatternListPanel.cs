using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    /// <summary>
    /// 左パネル：パターン一覧（番号・名前・行数・曲中で使われている回数）。
    /// クリックで編集対象にし、ダブルクリックで曲中のその位置から再生、上のアレンジ帯へドラッグするとその位置に並べる。並び自体の編集はアレンジ帯で行う。
    /// ドラッグはポインタのキャプチャで行い、ウィンドウの MIDI / Song ファイルのドロップ（DragAndDrop）とは混ぜない。
    /// </summary>
    public sealed class PatternListPanel : VisualElement
    {
        const float DragThreshold = 4f;

        readonly ComposerContext ctx;
        readonly ArrangeView band;
        readonly VisualElement list;
        readonly List<VisualElement> items = new List<VisualElement>();
        string builtKey;
        int selected = -1;

        int dragPattern = -1;
        Vector2 dragStart;
        bool dragging;

        ComposerSession S => ctx.Session;

        public PatternListPanel(ComposerContext ctx, ArrangeView band)
        {
            this.ctx = ctx;
            this.band = band;
            AddToClassList("cd-left");
            var title = ComposerContext.Title("Patterns");
            title.tooltip = "クリックで編集、ダブルクリックで曲中のその位置から再生、上のアレンジ帯へドラッグで曲に並べる、右クリックでメニュー";
            Add(title);
            var sv = new ScrollView();
            sv.style.flexGrow = 1;
            list = new VisualElement();
            sv.Add(list);
            Add(sv);
            Add(ComposerContext.ButtonRow(
                new Button(() => { S.NewPattern(); ctx.FocusMain(); }) { text = "New", tooltip = "空のパターンを追加" },
                new Button(() => { S.ClonePattern(); ctx.FocusMain(); }) { text = "Clone", tooltip = "編集中のパターンを複製" },
                new Button(ctx.Order.Insert) { text = "→ Song", tooltip = "編集中のパターンを、アレンジ帯で選択中のエントリの後ろに並べる（Alt+Insert）" }));
        }

        public void Refresh()
        {
            var song = ctx.State.song;
            if (song == null)
            {
                list.Clear();
                items.Clear();
                builtKey = null;
                return;
            }
            var uses = new int[song.patterns.Count];
            foreach (int pi in song.order)
                if (pi >= 0 && pi < uses.Length) uses[pi]++;

            var sb = new System.Text.StringBuilder();
            sb.Append(song.GetHashCode());
            for (int i = 0; i < song.patterns.Count; i++)
                sb.Append('|').Append(song.patterns[i].name).Append(',').Append(song.patterns[i].length).Append(',').Append(uses[i]);
            string key = sb.ToString();
            if (key != builtKey)
            {
                builtKey = key;
                selected = -1;
                list.Clear();
                items.Clear();
                for (int i = 0; i < song.patterns.Count; i++)
                {
                    var v = BuildItem(song.patterns[i], i, uses[i]);
                    list.Add(v);
                    items.Add(v);
                }
            }
            int sel = ctx.State.patternIndex;
            if (sel == selected) return;
            if (selected >= 0 && selected < items.Count) items[selected].RemoveFromClassList("selected");
            if (sel >= 0 && sel < items.Count) items[sel].AddToClassList("selected");
            selected = sel;
        }

        VisualElement BuildItem(Pattern p, int index, int uses)
        {
            var v = new VisualElement();
            v.AddToClassList("cd-patitem");
            var name = new Label($"{index:X2}  {p.name}") { pickingMode = PickingMode.Ignore };
            name.AddToClassList("cd-patitem-name");
            v.Add(name);
            var meta = new Label(uses == 0 ? $"{p.length}" : $"{p.length} ×{uses}") { pickingMode = PickingMode.Ignore };
            meta.AddToClassList("cd-patitem-meta");
            v.Add(meta);
            v.EnableInClassList("unused", uses == 0);
            v.tooltip = $"パターン {index:X2} {p.name}：{p.length} 行、曲中で {uses} 回使用\nクリック：編集   ダブルクリック：そこから再生   アレンジ帯へドラッグ：並べる   右クリック：メニュー";
            v.RegisterCallback<PointerDownEvent>(e => OnDown(e, v, index));
            v.RegisterCallback<PointerMoveEvent>(e => OnMove(e, v));
            v.RegisterCallback<PointerUpEvent>(e => OnUp(e, v, index));
            v.RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag(v));
            return v;
        }

        void OnDown(PointerDownEvent e, VisualElement v, int index)
        {
            if (e.button == 1)
            {
                S.SelectPattern(index);
                ShowMenu(index);
                e.StopPropagation();
                return;
            }
            if (e.button != 0) return;
            if (e.clickCount == 2)
            {
                S.PlayFromPattern(index);
                ctx.FocusMain();
                e.StopPropagation();
                return;
            }
            dragPattern = index;
            dragStart = e.position;
            dragging = false;
            v.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e, VisualElement v)
        {
            if (dragPattern < 0 || !v.HasPointerCapture(e.pointerId)) return;
            if (!dragging && ((Vector2)e.position - dragStart).magnitude < DragThreshold) return;
            dragging = true;
            v.AddToClassList("drag");
            band.ShowPatternDrop(e.position);
        }

        void OnUp(PointerUpEvent e, VisualElement v, int index)
        {
            if (dragPattern < 0) return;
            bool moved = dragging;
            int pattern = dragPattern;
            if (v.HasPointerCapture(e.pointerId)) v.ReleasePointer(e.pointerId);
            EndDrag(v);
            if (moved) band.DropPattern(e.position, pattern);
            else
            {
                S.SelectPattern(index);
                ctx.FocusMain();
            }
            e.StopPropagation();
        }

        void EndDrag(VisualElement v)
        {
            dragPattern = -1;
            dragging = false;
            v.RemoveFromClassList("drag");
            band.ShowPatternDrop(null);
        }

        void ShowMenu(int index)
        {
            var song = ctx.State.song;
            var m = new DawMenu();
            m.AddItem("アレンジの選択中エントリの後ろに並べる", false, ctx.Order.Insert);
            m.AddItem("曲の最後に並べる", false, () => ctx.Order.InsertPattern(song.order.Count, index));
            m.AddSeparator("");
            m.AddItem("複製", false, S.ClonePattern);
            m.AddItem("新規パターン", false, S.NewPattern);
            m.ShowAsContext();
        }
    }
}
