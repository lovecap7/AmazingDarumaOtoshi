using UnityEngine;

namespace Nakagawa
{
    // 生成された積み木がポンと出てくる演出(見た目だけ拡大する。当たり判定の大きさは変えない)
    // 終わったら自分を消す
    public class TumikiSpawnPop : MonoBehaviour
    {
        const float kDuration = 0.3f;
        // 行き過ぎてから戻る強さ
        const float kOvershoot = 1.7f;

        Transform m_visual;
        Vector3 m_scale;
        float m_time;

        private void Start()
        {
            // 積み木の見た目の部品(なければ最初に見つかった描画物)
            m_visual = transform.Find("Visual");
            if (m_visual == null)
            {
                var r = GetComponentInChildren<Renderer>();
                if (r != null && r.transform != transform) m_visual = r.transform;
            }
            if (m_visual == null)
            {
                Destroy(this);
                return;
            }
            m_scale = m_visual.localScale;
            m_visual.localScale = Vector3.zero;
        }

        private void Update()
        {
            m_time += Time.deltaTime;
            float t = Mathf.Clamp01(m_time / kDuration);
            // 少し大きくなってから元に戻る
            float s = 1.0f + (kOvershoot + 1.0f) * Mathf.Pow(t - 1.0f, 3) + kOvershoot * Mathf.Pow(t - 1.0f, 2);
            m_visual.localScale = m_scale * s;
            if (t >= 1.0f)
            {
                m_visual.localScale = m_scale;
                Destroy(this);
            }
        }

        private void OnDestroy()
        {
            if (m_visual != null && m_scale != Vector3.zero) m_visual.localScale = m_scale;
        }
    }
}
