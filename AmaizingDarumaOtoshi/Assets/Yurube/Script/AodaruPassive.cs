using Nakahira;
using UnityEngine;

namespace Yurube
{
    // あおだるのパッシブ。積んだ青い積み木の数だけ移動速度が上がる。
    // あかだる(AkadaruPassive)の「赤い積み木で弾速UP」と同じ考え方で、1個ごとに加算する。
    // 速度は DarumaCharacter.RequestSpeedScale で次の FixedUpdate に1回だけ掛かるため、
    // このコンポーネントの FixedUpdate が DarumaCharacter より先に走るよう実行順を下げている。
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(DarumaCharacter))]
    public class AodaruPassive : MonoBehaviour
    {
        [Tooltip("青い積み木1個あたりの移動速度アップ(m/s)")]
        [SerializeField, Min(0.0f)] float m_speedPerBlueBlock = 1.0f;
        [Tooltip("移動速度の上限(m/s)。積みすぎてもここで止まる")]
        [SerializeField, Min(0.0f)] float m_maxMoveSpeed = 12.0f;
        [Tooltip("青の数と移動速度をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logMoveSpeed = true;

        [Header("移動速度の見た目")]
        [Tooltip("風の粒・残像に使うマテリアル(FireParticle など。未設定なら自動生成)")]
        [SerializeField] Material m_effectMaterial;

        DarumaCharacter m_character;
        int m_lastBlue = -1;
        float m_lastLoggedSpeed = -1.0f;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();

            // 移動速度UPを見た目で出す(青い風・残像)
            var effect = GetComponent<AodaruSpeedEffect>();
            if (effect == null) effect = gameObject.AddComponent<AodaruSpeedEffect>();
            effect.Init(m_effectMaterial);
        }

        // 表示用。青の数か移動速度が変わったときだけ出す(毎フレームのログで埋もれないように)
        private void Update()
        {
            if (m_character == null || !m_logMoveSpeed) return;

            int blue = CountBlue();
            float speed = TargetSpeed(blue);
            if (blue == m_lastBlue && Mathf.Abs(speed - m_lastLoggedSpeed) < 0.05f) return;

            m_lastBlue = blue;
            m_lastLoggedSpeed = speed;
            Debug.Log($"[あおだる] 青{blue}個 → 移動速度 {speed:0.0} m/s (+{speed - m_character.MoveSpeed:0.0})");
        }

        // DarumaCharacter より先に実行され、1固定ステップにつき1回だけ速度倍率を積む
        private void FixedUpdate()
        {
            // 停止中(ワープ・カウントダウン等)は DarumaCharacter 側が倍率を消費しないので、
            // ここで掛けると復帰時に溜まって爆発する。動ける状態のときだけ掛ける。
            if (m_character == null || !m_character.CanBeAffected) return;

            float current = m_character.MoveSpeed;
            if (current <= 0.0f) return;

            float scale = TargetSpeed(CountBlue()) / current;
            if (scale <= 1.0001f) return;
            m_character.RequestSpeedScale(scale);
        }

        int CountBlue()
        {
            var stack = m_character.Stack;
            return stack != null ? stack.CountColor(TumikiColor.Blue) : 0;
        }

        // 今の移動速度(積み木の重さで下がった分)に青の数だけ足す
        float TargetSpeed(int blue)
        {
            return Mathf.Min(m_character.MoveSpeed + blue * m_speedPerBlueBlock, m_maxMoveSpeed);
        }

        // ---- デバッグ表示用(キャラクターラボから読む) ----
        public int BlueCount => CountBlue();
        public float EffectiveSpeed => TargetSpeed(CountBlue());
        public float SpeedPerBlueBlock => m_speedPerBlueBlock;
        public float MaxMoveSpeed => m_maxMoveSpeed;
    }
}
