using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yurube
{
    // あかだるの必殺技「トロン」。
    // ゲージが満タンのとき L キー / ゲームパッドの上(Y / △)で発動する。
    // 自分の積み木を1個飛ばし、発射地点から伸びる残像ビームに一定時間当たり判定を付ける。
    // ビームが相手に当たると、相手と自分が即死し、積み木は破壊される。
    [RequireComponent(typeof(DarumaCharacter))]
    public class AkadaruSpecial : MonoBehaviour, ISpecialAbility
    {
        [Header("弾")]
        [Tooltip("必殺技の積み木の速さ(m/s)")]
        [SerializeField] float m_speed = 20.0f;
        [Tooltip("残像ビームが残る時間(秒)。弾が止まった後もこの時間までレーザーが残る")]
        [SerializeField] float m_trailDuration = 15.0f;
        [Tooltip("通常の射撃エフェクトの速さの下限")]
        [SerializeField] float m_effectMinSpeed = 30.0f;
        [Tooltip("通常の射撃エフェクトの速さの上限")]
        [SerializeField] float m_effectMaxSpeed = 60.0f;
        [Tooltip("発動直後、自分が自分の残像に当たってもダメージを受けない時間(秒)")]
        [SerializeField] float m_ownerGrace = 0.6f;
        [Tooltip("同じ相手に連続で当たったとき、次の積み木を壊すまでの間隔(秒)")]
        [SerializeField] float m_hitInterval = 0.4f;
        [Tooltip("発動・命中をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logSpecial = true;

        [Header("カットイン")]
        [SerializeField] string m_cutInSpecialName = "終焉の軌跡";
        [SerializeField] Color m_cutInColor = new Color(0.91f, 0.29f, 0.25f);

        DarumaCharacter m_character;
        SpecialGauge m_gauge;
        Material m_effectMaterial;

        // CPU 用(中平): 必殺技の積み木の速さ(m/s。狙いの先読みに使う)
        public float Speed => m_speed;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            m_gauge = GetComponent<SpecialGauge>();
            // 通常の射撃エフェクトと同じマテリアルを使う
            var passive = GetComponent<AkadaruPassive>();
            m_effectMaterial = passive != null ? passive.EffectMaterialForSpecial : null;
        }

        private void Update()
        {
            if (m_gauge == null || !IsLocalHuman()) return;

            var keyboard = Keyboard.current;
            // F3で当たり判定のデバッグ表示を切り替える
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame)
                TronHitboxView.Visible = !TronHitboxView.Visible;

            bool pressed = SpecialInput.WasPressed(m_character);
            if (pressed) TryActivate();
        }

        // CPUのあかだるが、人間のキー入力で必殺技を出さないようにする
        bool IsLocalHuman()
        {
            if (DarumaMatch.Current == null) return true;   // ラボ
            foreach (var p in DarumaMatch.Current.Players)
            {
                if (p.Character == m_character) return !p.IsCpu;
            }
            return false;
        }

        // ラボのボタンからも呼べる。発動できたら true
        public bool TryActivate()
        {
            if (m_gauge == null || !m_gauge.IsFull) return false;
            if (m_character == null || m_character.Stack == null || m_character.Stack.Count == 0) return false;
            if (!m_character.CanBeAffected) return false;
            if (m_character.Hammer != null && m_character.Hammer.IsBusy) return false;

            int cutInBlocks = m_character.Stack.Count;
            var block = m_character.Stack.RemoveAt(0);
            if (block == null) return false;
            m_gauge.Consume();

            Vector3 direction = m_character.transform.forward;
            Vector3 center = m_character.transform.position + Vector3.up * (TumikiBlock.kHeight * 0.5f);
            Vector3 position = center + direction * (DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.05f);
            bool embedded = Nakagawa.TumikiPlacement.ResolveEmbed(center, ref position);
            block.transform.position = position;
            block.Launch(direction, m_speed, m_character);
            if (embedded) Nakagawa.TumikiPlacement.IgnoreCharactersAfterEmbed(block);

            var tron = block.gameObject.AddComponent<TronProjectile>();
            tron.Begin(m_character, m_trailDuration, m_ownerGrace, m_hitInterval, m_logSpecial);

            // 見た目は通常の射撃と同じ AkadaruShotTrail をそのまま使う
            var shotTrail = block.GetComponent<AkadaruShotTrail>();
            if (shotTrail == null) shotTrail = block.gameObject.AddComponent<AkadaruShotTrail>();
            // 必殺技は通常より太く・白く・長く残るレーザーにする
            shotTrail.BeginSpecial(m_effectMaterial, m_effectMinSpeed, m_effectMaxSpeed, position);

            // 通った地面を一定時間焼け焦げさせる
            var scorchObject = new GameObject("TronScorch");
            scorchObject.AddComponent<TronScorchTrail>().Begin(shotTrail);

            // 発動カットイン(ゲームを一時停止、実モデルの3D演出)
            SpecialCutIn.Show(m_cutInSpecialName, m_cutInColor, m_effectMaterial,
                m_character, block, cutInBlocks);

            if (m_logSpecial) Debug.Log("[あかだる] 必殺技『終焉の軌跡』発動", this);
            return true;
        }

        // 試合中だけ、画面左下にゲージを出す(ラボはラボUI側で表示する)
        private void OnGUI()
        {
            SpecialGaugeHUD.Draw(m_gauge, m_character);
        }
    }
}
