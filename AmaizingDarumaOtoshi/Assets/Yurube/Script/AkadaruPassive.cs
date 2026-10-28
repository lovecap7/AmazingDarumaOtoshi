using Nakahira;
using UnityEngine;

namespace Yurube
{
    // あかだるのパッシブ: 積んでいる赤色の積み木の数だけ、股抜きショットの弾速が上がる
    // ハンマーの性能(HammerParams)はこのキャラクター専用の複製を使い、元のアセットは書き換えない
    // 撃っている間は弾速を更新しない(弾が出る直前に最下段が減るため、撃ち始めた時点の段数で固定する)
    [RequireComponent(typeof(DarumaCharacter))]
    public class AkadaruPassive : MonoBehaviour
    {
        [Tooltip("赤色の積み木1個あたりの弾速の上昇量(m/s)")]
        [SerializeField, Min(0.0f)] float m_shotSpeedPerRedBlock = 1.0f;
        [Tooltip("弾速の上限(m/s)")]
        [SerializeField, Min(0.0f)] float m_maxShotSpeed = 60.0f;
        [Tooltip("ショット時の赤の数と弾速をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logShotSpeed = true;

        DarumaCharacter m_character;
        DarumaHammer m_hammer;
        HammerParams m_params;
        float m_baseShotSpeed;
        bool m_wasShooting;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            m_hammer = m_character != null && m_character.Hammer != null
                ? m_character.Hammer
                : GetComponentInChildren<DarumaHammer>();
            if (m_hammer == null || m_hammer.Params == null) return;

            // 元のアセットを書き換えないよう、このキャラクター用に複製する
            m_params = Instantiate(m_hammer.Params);
            m_baseShotSpeed = m_params.ShotSpeed;
            m_hammer.Params = m_params;
        }

        private void Update()
        {
            if (m_params == null) return;

            // 撃っている間は、撃ち始めた時点の段数で固定する
            if (m_hammer.IsShooting)
            {
                if (!m_wasShooting && m_logShotSpeed)
                {
                    int r = m_character.Stack != null ? m_character.Stack.CountColor(TumikiColor.Red) : 0;
                    Debug.Log($"[あかだる] 赤{r}個 → 弾速 {m_params.ShotSpeed:0.0} m/s");
                }
                m_wasShooting = true;
                return;
            }
            m_wasShooting = false;

            int red = m_character.Stack != null ? m_character.Stack.CountColor(TumikiColor.Red) : 0;
            m_params.ShotSpeed = Mathf.Min(m_baseShotSpeed + red * m_shotSpeedPerRedBlock, m_maxShotSpeed);
        }
    }
}
