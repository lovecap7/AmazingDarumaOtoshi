using UnityEngine;

namespace Yurube
{
    // いわだる着弾の短いヒットストップ。
    // Time.timeScaleを使うため、終了・無効化時に必ず元へ戻す。
    // (中平変更) カットインと重なっても正しく戻るように、時間の変更は共通の仕組み(Nakahira.TimeStop)で行う。
    public class IwaImpactHitStop : MonoBehaviour
    {
        float m_endTime;
        bool m_active;

        public static void Spawn(float duration, float timeScale)
        {
            if (duration <= 0.0f) return;
            var go = new GameObject("IwaImpactHitStop");
            var hitStop = go.AddComponent<IwaImpactHitStop>();
            hitStop.Begin(duration, timeScale);
        }

        void Begin(float duration, float timeScale)
        {
            m_active = true;
            Nakahira.TimeStop.Begin(this, Mathf.Clamp(timeScale, 0.01f, 1.0f));
            m_endTime = Time.unscaledTime + Mathf.Max(0.01f, duration);
        }

        private void Update()
        {
            if (!m_active) return;
            if (Time.unscaledTime < m_endTime) return;
            Restore();
            Destroy(gameObject);
        }

        private void OnDisable()
        {
            Restore();
        }

        void Restore()
        {
            if (!m_active) return;
            m_active = false;
            Nakahira.TimeStop.End(this);
        }
    }
}
