using UnityEngine;

namespace Nakagawa
{
    // 一回きりのエフェクトの動き(エフェクトのプレハブのルートに付ける)
    //  ・光: 出た瞬間に明るく光り、すぐに消える
    //  ・文字: ポンと大きく飛び出してから少し縮み、上へ浮かびながら消える。常にカメラの方を向く
    //  ・一定時間で自分ごと消える
    public class EffectPopup : MonoBehaviour
    {
        [Tooltip("この時間(秒)で自分ごと消える")]
        [SerializeField, Min(0.1f)] float m_lifetime = 1.8f;

        [Header("光")]
        [SerializeField] Light m_light;
        [SerializeField, Min(0.0f)] float m_lightIntensity = 8.0f;
        [SerializeField, Min(0.01f)] float m_lightDuration = 0.45f;

        [Header("文字")]
        [Tooltip("動かす文字のルート(子に文字と影を置く)")]
        [SerializeField] Transform m_text;
        [SerializeField] TextMesh[] m_textMeshes;
        [Tooltip("浮かび上がる高さ(m)")]
        [SerializeField] float m_textRise = 1.0f;
        [SerializeField, Min(0.01f)] float m_textDuration = 1.2f;
        [Tooltip("大きさの変化(横軸: 0〜1の経過割合)")]
        [SerializeField] AnimationCurve m_textScale = new AnimationCurve(
            new Keyframe(0.0f, 0.0f), new Keyframe(0.1f, 1.35f), new Keyframe(0.22f, 0.95f), new Keyframe(0.3f, 1.0f), new Keyframe(1.0f, 1.0f));
        [Tooltip("不透明度の変化(横軸: 0〜1の経過割合)")]
        [SerializeField] AnimationCurve m_textAlpha = new AnimationCurve(
            new Keyframe(0.0f, 1.0f), new Keyframe(0.65f, 1.0f), new Keyframe(1.0f, 0.0f));

        float m_time;
        Vector3 m_textBasePosition;

        // 文字を出さない(同じエフェクトを文字なしで使うとき。生成した直後に呼ぶ)
        public void HideText()
        {
            if (m_text != null) m_text.gameObject.SetActive(false);
            m_text = null;
            m_textMeshes = null;
        }
        Vector3 m_textBaseScale;
        Color[] m_textColors;

        private void Start()
        {
            if (m_text != null)
            {
                m_textBasePosition = m_text.localPosition;
                m_textBaseScale = m_text.localScale;
            }
            if (m_textMeshes != null)
            {
                m_textColors = new Color[m_textMeshes.Length];
                for (int i = 0; i < m_textMeshes.Length; i++) m_textColors[i] = m_textMeshes[i].color;
            }
            Destroy(gameObject, m_lifetime);
            UpdateVisuals();
        }

        private void Update()
        {
            m_time += Time.deltaTime;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (m_light != null)
            {
                float t = Mathf.Clamp01(m_time / m_lightDuration);
                m_light.intensity = m_lightIntensity * (1.0f - t) * (1.0f - t);
                m_light.enabled = t < 1.0f;
            }

            if (m_text != null)
            {
                float t = Mathf.Clamp01(m_time / m_textDuration);
                float rise = 1.0f - (1.0f - t) * (1.0f - t);
                m_text.localPosition = m_textBasePosition + Vector3.up * (m_textRise * rise);
                m_text.localScale = m_textBaseScale * m_textScale.Evaluate(t);
                var cam = Camera.main;
                if (cam != null) m_text.rotation = cam.transform.rotation;

                if (m_textMeshes != null && m_textColors != null)
                {
                    float alpha = m_textAlpha.Evaluate(t);
                    for (int i = 0; i < m_textMeshes.Length; i++)
                    {
                        Color c = m_textColors[i];
                        c.a *= alpha;
                        m_textMeshes[i].color = c;
                    }
                }
            }
        }
    }
}
