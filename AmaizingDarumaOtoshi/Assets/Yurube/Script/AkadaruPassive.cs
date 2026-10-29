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

        [Header("弾速の見た目")]
        [Tooltip("発射時に残光トレイルとマズルフラッシュを出す")]
        [SerializeField] bool m_enableShotVisuals = true;
        [Tooltip("自分が飛ばした積み木を探す範囲(m)")]
        [SerializeField] float m_searchRadius = 3.0f;
        [Tooltip("トレイル・フラッシュに使うマテリアル(未設定なら Sprites/Default)")]
        [SerializeField] Material m_effectMaterial;

        DarumaCharacter m_character;
        DarumaHammer m_hammer;
        HammerParams m_params;
        float m_baseShotSpeed;
        bool m_wasShooting;
        readonly Collider[] m_overlaps = new Collider[32];
        static Material s_fallbackEffectMaterial;

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

        // 弾速の見た目: 飛び始めた自分の積み木に残光トレイルを付け、発射時はマズルフラッシュを出す
        private void FixedUpdate()
        {
            if (!m_enableShotVisuals || m_character == null || !m_character.CanBeAffected) return;

            Vector3 center = m_character.transform.position + Vector3.up * 0.5f;
            int count = Physics.OverlapSphereNonAlloc(center, m_searchRadius, m_overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var block = m_overlaps[i].GetComponentInParent<TumikiBlock>();
                if (block == null) continue;
                if (block.CurrentState != TumikiBlock.State.Flying) continue;
                // 自分が飛ばした積み木だけ
                if (!ReferenceEquals(block.Owner, m_character)) continue;
                // 必殺技トロンの積み木には通常の残光を付けない
                if (block.GetComponent<TronProjectile>() != null) continue;

                var trail = block.GetComponent<AkadaruShotTrail>();
                // この発射でまだ付けていなければ付ける(投げ直しでもまた付く)
                if (trail != null && trail.ConsumedLaunch) continue;
                if (trail == null) trail = block.gameObject.AddComponent<AkadaruShotTrail>();

                // 弾が出る位置(キャラクターの正面)を渡す
                Vector3 muzzle = m_character.transform.position
                    + Vector3.up * (TumikiBlock.kHeight * 0.5f)
                    + m_character.transform.forward * (DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.05f);
                trail.Begin(EffectMaterial, m_baseShotSpeed > 0.0f ? m_baseShotSpeed : 10.0f, m_maxShotSpeed, muzzle);
            }
        }

        private Material EffectMaterial
        {
            get
            {
                if (m_effectMaterial != null) return m_effectMaterial;
                if (s_fallbackEffectMaterial == null)
                {
                    var shader = Shader.Find("Sprites/Default");
                    if (shader != null) s_fallbackEffectMaterial = new Material(shader);
                }
                return s_fallbackEffectMaterial;
            }
        }

        // 必殺技の残像でも、通常の射撃と同じマテリアルを使う
        public Material EffectMaterialForSpecial => EffectMaterial;
    }
}
