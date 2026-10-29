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
    public class AkadaruSpecial : MonoBehaviour
    {
        [Header("弾")]
        [Tooltip("必殺技の積み木の速さ(m/s)")]
        [SerializeField] float m_speed = 20.0f;
        [Tooltip("残像ビームが残る時間(秒)")]
        [SerializeField] float m_trailDuration = 1.2f;
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

        DarumaCharacter m_character;
        SpecialGauge m_gauge;
        Material m_effectMaterial;

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

            bool pressed = keyboard != null && keyboard.lKey.wasPressedThisFrame;
            var gamepad = Gamepad.current;
            if (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame) pressed = true;
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
            shotTrail.Begin(m_effectMaterial, m_effectMinSpeed, m_effectMaxSpeed, position);

            if (m_logSpecial) Debug.Log("[あかだる] 必殺技トロン発動", this);
            return true;
        }

        // 試合中だけ、画面左下にゲージを出す(ラボはラボUI側で表示する)
        private void OnGUI()
        {
            if (m_gauge == null || DarumaMatch.Current == null) return;

            // CPUのあかだるのゲージは出さない
            MatchPlayer me = null;
            foreach (var p in DarumaMatch.Current.Players)
            {
                if (p.Character == m_character) { me = p; break; }
            }
            if (me == null || me.IsCpu) return;

            var rect = new Rect(20.0f, Screen.height - 64.0f, 260.0f, 34.0f);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 8.0f, rect.y + 2.0f, 244.0f, 18.0f),
                m_gauge.IsFull ? "SPECIAL READY [L / Y]" : $"SPECIAL {m_gauge.Blocks}/{m_gauge.BlocksToFill}");

            var bar = new Rect(rect.x + 8.0f, rect.y + 22.0f, 244.0f, 6.0f);
            GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.6f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = m_gauge.IsFull ? Color.cyan : new Color(0.2f, 0.7f, 1.0f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * m_gauge.Normalized, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
