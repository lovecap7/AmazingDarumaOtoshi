using Nakagawa;
using UnityEngine;

namespace Nakahira
{
    // 幽霊が復活するときの演出: 幽霊の位置から魂が飛び出し、弧を描いて出現ポイントへ飛ぶ
    // たどり着いたら消え、そこから中川さんの復活の出現演出(RespawnAppearEffect の光の柱)が始まる
    //  ・DarumaMatch が復活のときに呼ぶ(時間は DarumaMatch の「復活で魂が移動する時間」)
    //  ・飛んでいる間はカメラがプレイヤーと同じように画面に収める(CameraFocusTarget)
    //  ・見た目・弧の高さは GhostAppearParams。時間はゲーム内の時間(Time.deltaTime)で進む
    public class GhostReviveSoulEffect : MonoBehaviour
    {
        GhostAppearParams m_params;
        Vector3 m_from;
        Vector3 m_to;
        float m_duration;
        float m_time;
        GhostSoulVisual m_soul;

        // from: 幽霊の位置(足元) / to: 出現ポイント(足元) / duration: 飛ぶ時間(秒)
        public static GhostReviveSoulEffect Play(GhostAppearParams p, Vector3 from, Vector3 to, float duration)
        {
            if (p == null || duration <= 0.0f) return null;
            var effect = new GameObject("GhostReviveSoulEffect").AddComponent<GhostReviveSoulEffect>();
            effect.Begin(p, from, to, duration);
            return effect;
        }

        private void Begin(GhostAppearParams p, Vector3 from, Vector3 to, float duration)
        {
            m_params = p;
            m_from = from + Vector3.up * p.ReviveStartHeight;
            m_to = to + Vector3.up * p.ReviveEndHeight;
            m_duration = duration;
            m_time = 0.0f;
            transform.position = to;

            m_soul = new GhostSoulVisual(transform, p);
            // カメラは魂をこれから出るプレイヤーとして映しておく
            m_soul.Root.gameObject.AddComponent<CameraFocusTarget>();
            m_soul.Move(m_from, true);
            Apply();
        }

        private void Update()
        {
            m_time += Time.deltaTime;
            if (m_time >= m_duration)
            {
                Destroy(gameObject);
                return;
            }
            Apply();
        }

        private void Apply()
        {
            var p = m_params;
            float t = Mathf.Clamp01(m_time / m_duration);
            float u = p.ReviveCurve.Evaluate(t);

            // 弧の高さは距離に合わせる
            Vector3 flat = m_to - m_from;
            flat.y = 0.0f;
            float arc = Mathf.Min(p.ReviveArcMax, p.ReviveArcHeight + p.ReviveArcPerMeter * flat.magnitude);
            Vector3 pos = Vector3.LerpUnclamped(m_from, m_to, u) + Vector3.up * (4.0f * arc * u * (1.0f - u));
            m_soul.Move(pos, false);

            // 出た瞬間はぽんと大きくなり、着く直前に少し縮んで光の柱へ変わる
            float pop = EaseOut(m_time / 0.08f);
            float shrink = Mathf.Lerp(1.0f, 0.6f, Smooth((t - 0.85f) / 0.15f));
            m_soul.Apply(pop * shrink, 1.0f, m_time);
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3.0f - 2.0f * t);
        }

        private static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1.0f - (1.0f - t) * (1.0f - t);
        }
    }
}
