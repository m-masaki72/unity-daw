using UnityEngine;

namespace UniDAW
{
    /// <summary>
    /// 映像連携のサンプル。チャンネル毎のオブジェクトをノートで弾ませ、音高で高さ・色を変える。
    /// VFX Graph 等へは <see cref="UniDAWPlayer.NoteTriggered"/> を購読して同様に繋げる。
    /// </summary>
    [AddComponentMenu("UniDAW/UniDAW Visualizer")]
    public class UniDAWVisualizer : MonoBehaviour
    {
        public UniDAWPlayer player;
        public Transform[] channelTargets = new Transform[ChipEngine.Channels];
        public float punch = 0.8f;
        public float decay = 10f;
        public float pitchHeight = 0.06f;
        public Color[] colors =
        {
            new Color(1f, 0.35f, 0.45f), new Color(1f, 0.75f, 0.3f), new Color(0.35f, 0.8f, 1f), new Color(0.75f, 1f, 0.5f),
        };

        readonly float[] pulse = new float[ChipEngine.Channels];
        readonly float[] targetY = new float[ChipEngine.Channels];
        Vector3[] baseScale;
        Vector3[] basePos;
        MaterialPropertyBlock mpb;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void OnEnable()
        {
            mpb = new MaterialPropertyBlock();
            baseScale = new Vector3[channelTargets.Length];
            basePos = new Vector3[channelTargets.Length];
            for (int i = 0; i < channelTargets.Length; i++)
            {
                if (channelTargets[i] == null) continue;
                baseScale[i] = channelTargets[i].localScale;
                basePos[i] = channelTargets[i].localPosition;
            }
            if (player != null) player.NoteTriggered += OnNote;
        }

        void OnDisable()
        {
            if (player != null) player.NoteTriggered -= OnNote;
        }

        void OnNote(NoteEvent e)
        {
            if (e.channel < 0 || e.channel >= pulse.Length) return;
            pulse[e.channel] = Mathf.Max(pulse[e.channel], 0.3f + 0.7f * e.volume);
            targetY[e.channel] = e.channel == 3 ? e.note * 0.1f : (e.note - 60) * pitchHeight;
        }

        void Update()
        {
            for (int i = 0; i < channelTargets.Length && i < pulse.Length; i++)
            {
                var t = channelTargets[i];
                if (t == null) continue;
                pulse[i] = Mathf.Max(0f, pulse[i] - decay * Time.deltaTime * pulse[i]);
                float level = player != null ? player.GetLevel(i) : 0f;
                t.localScale = baseScale[i] * (1f + pulse[i] * punch + level * 0.3f);
                var p = basePos[i];
                p.y = Mathf.Lerp(t.localPosition.y, basePos[i].y + targetY[i], 1f - Mathf.Exp(-12f * Time.deltaTime));
                t.localPosition = p;

                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                var c = Color.Lerp(colors[i % colors.Length] * 0.35f, colors[i % colors.Length] * 1.6f, pulse[i]);
                r.GetPropertyBlock(mpb);
                mpb.SetColor(ColorId, c);
                mpb.SetColor(BaseColorId, c);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
