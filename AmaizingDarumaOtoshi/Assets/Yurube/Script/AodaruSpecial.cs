using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yurube
{
    // あおだるの必殺技「六腕」。
    // 発動すると6本のハンマー(阿修羅のような腕)を出し、
    // プレイヤーを起点にした3方向から近づく積み木を、操作に関係なくはじき返す。
    [RequireComponent(typeof(DarumaCharacter))]
    public class AodaruSpecial : MonoBehaviour, ISpecialAbility
    {
        [Header("必殺技")]
        [Tooltip("効果時間(秒)")]
        [SerializeField] float m_duration = 4.0f;
        [Tooltip("積み木をはじき返す範囲(m)")]
        [SerializeField] float m_knockRadius = 3.0f;
        [Tooltip("はじき返す速さ(m/s)")]
        [SerializeField] float m_knockSpeed = 24.0f;
        [Tooltip("同じ積み木を続けてはじかない間隔(秒)")]
        [SerializeField] float m_knockInterval = 0.5f;
        [Tooltip("発動をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logSpecial = true;

        DarumaCharacter m_character;
        SpecialGauge m_gauge;
        AodaruSixArms m_arms;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            m_gauge = GetComponent<SpecialGauge>();
        }

        private void Update()
        {
            if (m_gauge == null || !IsLocalHuman()) return;

            var keyboard = Keyboard.current;
            bool pressed = keyboard != null && keyboard.lKey.wasPressedThisFrame;

            var gamepad = Gamepad.current;
            if (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame) pressed = true;

            if (pressed) TryActivate();
        }

        // CPUのあおだるが、人間のキー入力で必殺技を出さないようにする
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
            if (m_character == null || !m_character.CanBeAffected) return false;
            if (m_arms != null) return false;   // 発動中は重ねられない
            if (m_character.Hammer != null && m_character.Hammer.IsBusy) return false;

            m_gauge.Consume();

            var root = new GameObject("AodaruSixArms");
            m_arms = root.AddComponent<AodaruSixArms>();

            var passive = GetComponent<AodaruPassive>();
            Material energySource = passive != null ? passive.EffectMaterial : null;

            m_arms.Initialize(
                m_character,
                m_duration,
                m_character.transform.eulerAngles.y,
                m_knockRadius,
                m_knockSpeed,
                m_knockInterval,
                energySource);

            if (m_logSpecial) Debug.Log("[あおだる] 必殺技『六腕』発動", this);
            return true;
        }

        private void OnDestroy()
        {
            if (m_arms != null) Destroy(m_arms.gameObject);
        }

        // 試合中だけ、画面左下にゲージを出す(ラボはラボUI側で表示する)
        private void OnGUI()
        {
            if (m_gauge == null || DarumaMatch.Current == null) return;

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
