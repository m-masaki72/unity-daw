using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    public enum Glyph { Play, PlayFromStart, Stop, Record, Menu }

    /// <summary>ベクター描画のアイコン＋小さな見出しのボタン。フォーカスを奪わない。</summary>
    public sealed class IconButton : VisualElement
    {
        readonly Glyph glyph;
        readonly VisualElement icon;

        public IconButton(Glyph glyph, string caption, Action onClick, string tooltip = null)
        {
            this.glyph = glyph;
            this.tooltip = tooltip;
            AddToClassList("cd-ibtn");
            icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("cd-ibtn-icon");
            icon.generateVisualContent += Draw;
            Add(icon);
            if (caption != null)
            {
                var l = new Label(caption) { pickingMode = PickingMode.Ignore };
                l.AddToClassList("cd-ibtn-cap");
                Add(l);
            }
            if (onClick != null) this.AddManipulator(new Clickable(onClick));
            // 色は USS の hover/on で変わるので、そのたびに描き直す
            RegisterCallback<PointerEnterEvent>(_ => icon.MarkDirtyRepaint());
            RegisterCallback<PointerLeaveEvent>(_ => icon.MarkDirtyRepaint());
        }

        public bool Active
        {
            get => ClassListContains("on");
            set
            {
                if (Active == value) return;
                EnableInClassList("on", value);
                icon.MarkDirtyRepaint();
            }
        }

        void Draw(MeshGenerationContext mgc)
        {
            var r = icon.contentRect;
            float w = r.width, h = r.height;
            if (w <= 0 || h <= 0) return;
            var p = mgc.painter2D;
            p.fillColor = icon.resolvedStyle.color;
            switch (glyph)
            {
                case Glyph.Play:
                    Tri(p, 1, 0, w, h);
                    break;
                case Glyph.PlayFromStart:
                    Box(p, 0, 0, 2.5f, h);
                    Tri(p, 4, 0, w, h);
                    break;
                case Glyph.Stop:
                    Box(p, 1, 1, w - 2, h - 2);
                    break;
                case Glyph.Record:
                    p.BeginPath();
                    p.Arc(new Vector2(w / 2, h / 2), Mathf.Min(w, h) / 2 - 0.5f, Angle.Degrees(0), Angle.Degrees(360));
                    p.Fill();
                    break;
                case Glyph.Menu:
                    for (int i = 0; i < 3; i++) Box(p, 0, h * (0.15f + i * 0.3f), w, 1.6f);
                    break;
            }
        }

        static void Tri(Painter2D p, float x0, float y0, float x1, float y1)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y0));
            p.LineTo(new Vector2(x1, (y0 + y1) / 2));
            p.LineTo(new Vector2(x0, y1));
            p.ClosePath();
            p.Fill();
        }

        static void Box(Painter2D p, float x, float y, float w, float h)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
            p.Fill();
        }
    }
}
