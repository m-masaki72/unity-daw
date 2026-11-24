using UnityEngine;
using UnityEngine.UIElements;

namespace UniDAW.Editor
{
    /// <summary>チャンネルごとの縦型レベルメーター（ピークを少しずつ落とす）。</summary>
    public sealed class LevelMeter : VisualElement
    {
        const float Decay = 0.88f;

        readonly VisualElement[] fills;
        readonly float[] shown;

        public LevelMeter(int channels)
        {
            AddToClassList("cd-meter");
            fills = new VisualElement[channels];
            shown = new float[channels];
            for (int c = 0; c < channels; c++)
            {
                var track = new VisualElement();
                track.AddToClassList("cd-meter-track");
                var fill = new VisualElement();
                fill.AddToClassList("cd-meter-fill");
                fill.AddToClassList("c" + c);
                track.Add(fill);
                Add(track);
                fills[c] = fill;
            }
        }

        /// <summary>levels は 0..1 程度の振幅。null なら全チャンネル 0 として減衰させる。</summary>
        public void Push(float[] levels)
        {
            for (int c = 0; c < fills.Length; c++)
            {
                float target = levels != null && c < levels.Length ? Mathf.Clamp01(Mathf.Sqrt(Mathf.Max(0, levels[c]))) : 0f;
                float v = Mathf.Max(target, shown[c] * Decay);
                if (v < 0.01f) v = 0;
                if (Mathf.Abs(v - shown[c]) < 0.002f) continue;
                shown[c] = v;
                fills[c].style.height = Length.Percent(v * 100f);
            }
        }
    }
}
