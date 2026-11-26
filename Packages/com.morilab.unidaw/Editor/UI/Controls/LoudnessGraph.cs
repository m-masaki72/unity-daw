using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>
    /// 再生中のラウドネスの推移（ショートターム）。マスターの後を実線、前を薄い線で描き、目標ラウドネスに横線を引く。
    /// 再生した長さに合わせて横に縮める（最短 30 秒ぶんの幅）。
    /// </summary>
    public sealed class LoudnessGraph : VisualElement
    {
        public const float MinDb = -36f, MaxDb = 0f;
        const float MinSpanSeconds = 30f;

        static readonly Color Bg = new Color(0.07f, 0.07f, 0.09f);
        static readonly Color Grid = new Color(1f, 1f, 1f, 0.06f);
        static readonly Color TargetColor = new Color(0.95f, 0.62f, 0.32f, 0.8f);
        static readonly Color PostColor = new Color(0.45f, 0.8f, 1f);
        static readonly Color PreColor = new Color(1f, 1f, 1f, 0.3f);

        IReadOnlyList<float> post, pre;
        float target = -14f;
        int drawn = -1;

        public LoudnessGraph()
        {
            AddToClassList("cd-loud-graph");
            tooltip = "ラウドネスの推移（ショートターム 3 秒）。青 = マスターの後、灰 = マスターの前、橙 = 目標。横線は 6 dB ごと";
            generateVisualContent += Draw;
        }

        /// <summary>データが増えたときだけ描き直す。</summary>
        public void SetData(IReadOnlyList<float> post, IReadOnlyList<float> pre, float target)
        {
            int n = post?.Count ?? 0;
            if (n == drawn && Mathf.Approximately(target, this.target) && ReferenceEquals(post, this.post)) return;
            this.post = post;
            this.pre = pre;
            this.target = target;
            drawn = n;
            MarkDirtyRepaint();
        }

        float YOf(float db, float h) => (1f - Mathf.InverseLerp(MinDb, MaxDb, db)) * h;

        void Draw(MeshGenerationContext mgc)
        {
            var r = contentRect;
            if (r.width < 4 || r.height < 4) return;
            var p = mgc.painter2D;
            Fader.Rect(p, 0, 0, r.width, r.height, Bg);
            for (float db = MaxDb - 6f; db > MinDb; db -= 6f) Fader.Rect(p, 0, YOf(db, r.height), r.width, 1, Grid);
            Fader.Rect(p, 0, YOf(target, r.height) - 0.5f, r.width, 1, TargetColor);
            int n = post?.Count ?? 0;
            if (n < 2) return;
            float span = Mathf.Max(n, MinSpanSeconds / LoudnessTrack.HopSeconds);
            float dx = r.width / span;
            Line(p, pre, dx, r.height, PreColor, 1f);
            Line(p, post, dx, r.height, PostColor, 1.5f);
        }

        void Line(Painter2D p, IReadOnlyList<float> v, float dx, float h, Color c, float width)
        {
            if (v == null) return;
            p.strokeColor = c;
            p.lineWidth = width;
            p.lineJoin = LineJoin.Round;
            bool open = false;
            // 横 1 ピクセルに何点も入るときは間引く
            int step = Mathf.Max(1, Mathf.FloorToInt(1f / Mathf.Max(dx, 1e-4f)));
            for (int i = 0; i < v.Count; i += step)
            {
                float db = v[i];
                if (float.IsNegativeInfinity(db) || db < MinDb - 6f)
                {
                    if (open) p.Stroke();
                    open = false;
                    continue;
                }
                var pt = new Vector2(i * dx, YOf(Mathf.Clamp(db, MinDb, MaxDb), h));
                if (!open)
                {
                    p.BeginPath();
                    p.MoveTo(pt);
                    open = true;
                }
                else p.LineTo(pt);
            }
            if (open) p.Stroke();
        }
    }
}
