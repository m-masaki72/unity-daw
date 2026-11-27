using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Composer
{
    public enum Glyph { Play, PlayFromStart, Stop, Record, Menu, ToStart, Loop, PulseWave, TriangleWave, NoiseWave }

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
                case Glyph.ToStart:
                    Box(p, 0, 0, 2.5f, h);
                    Tri(p, w, 0, 3, h);
                    break;
                case Glyph.Loop:
                    // 円弧＋矢じり（サイクル）
                    var c = new Vector2(w / 2, h / 2);
                    float rad = Mathf.Min(w, h) / 2 - 1.5f;
                    p.strokeColor = p.fillColor;
                    p.lineWidth = 2f;
                    p.BeginPath();
                    p.Arc(c, rad, Angle.Degrees(-60), Angle.Degrees(210));
                    p.Stroke();
                    var tip = c + new Vector2(Mathf.Cos(-60 * Mathf.Deg2Rad), Mathf.Sin(-60 * Mathf.Deg2Rad)) * rad;
                    p.BeginPath();
                    p.MoveTo(tip + new Vector2(-4.5f, -1f));
                    p.LineTo(tip + new Vector2(1.5f, -3.5f));
                    p.LineTo(tip + new Vector2(1f, 3f));
                    p.ClosePath();
                    p.Fill();
                    break;
                case Glyph.Menu:
                    for (int i = 0; i < 3; i++) Box(p, 0, h * (0.15f + i * 0.3f), w, 1.6f);
                    break;
                case Glyph.PulseWave:
                    Wave(p, w, h, 0f, 0f, 0.25f, 0.25f, 1f, 0.25f, 1f, 0f, 0.5f, 0f, 0.5f, 1f, 0.75f, 1f, 0.75f, 0f, 1f, 0f);
                    break;
                case Glyph.TriangleWave:
                    Wave(p, w, h, 0f, 0.5f, 0.125f, 0f, 0.375f, 1f, 0.625f, 0f, 0.875f, 1f, 1f, 0.5f);
                    break;
                case Glyph.NoiseWave:
                    Wave(p, w, h, 0f, 0.5f, 0.1f, 0.1f, 0.2f, 0.8f, 0.3f, 0.3f, 0.4f, 1f, 0.5f, 0f, 0.6f, 0.65f, 0.7f, 0.2f, 0.8f, 0.9f, 0.9f, 0.35f, 1f, 0.6f);
                    break;
            }
        }

        /// <summary>0..1 の (x, y) の折れ線（y は上が 0）を線で描く。</summary>
        static void Wave(Painter2D p, float w, float h, params float[] xy)
        {
            const float pad = 1f;
            p.strokeColor = p.fillColor;
            p.lineWidth = 1.5f;
            p.lineJoin = LineJoin.Miter;
            p.BeginPath();
            for (int i = 0; i + 1 < xy.Length; i += 2)
            {
                var pt = new Vector2(pad + xy[i] * (w - 2 * pad), pad + xy[i + 1] * (h - 2 * pad));
                if (i == 0) p.MoveTo(pt);
                else p.LineTo(pt);
            }
            p.Stroke();
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
