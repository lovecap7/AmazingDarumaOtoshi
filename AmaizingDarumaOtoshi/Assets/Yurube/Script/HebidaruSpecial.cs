using Nakahira;
using UnityEngine;

namespace Yurube
{
    // へびだるの必殺技「反転毒沼」。
    // カットイン後、発動位置を中心に円形の毒沼を生成する。
    // 沼に入った敵には移動速度低下＋移動入力反転のデバフを付与する。
    [RequireComponent(typeof(DarumaCharacter))]
    public class HebidaruSpecial : MonoBehaviour, ISpecialAbility
    {
        [Header("必殺技")]
        [SerializeField] string m_specialName = "反転毒沼";
        [Tooltip("毒沼の半径(m)。表示円と当たり判定の両方に使う")]
        [SerializeField, Min(0.5f)] float m_swampRadius = 4.5f;
        [Tooltip("毒沼がフィールドに残る時間(秒)")]
        [SerializeField, Min(0.1f)] float m_swampDuration = 8.0f;
        [Tooltip("毒沼に入った敵に付与するデバフの持続時間(秒)")]
        [SerializeField, Min(0.1f)] float m_debuffDuration = 3.0f;
        [Tooltip("デバフ中の移動速度倍率(1で通常、0.5で半分)")]
        [SerializeField, Range(0.05f, 1.0f)] float m_slowMultiplier = 0.55f;
        [Tooltip("毒沼の当たり判定を確認する間隔(秒)")]
        [SerializeField, Min(0.02f)] float m_tickInterval = 0.15f;
        [Tooltip("毒沼が発動者に追従するか")]
        [SerializeField] bool m_followCaster = false;
        [Tooltip("毒沼の中心位置オフセット")]
        [SerializeField] Vector3 m_swampOffset = Vector3.zero;
        [Tooltip("発動ログをConsoleに出す(テスト用)")]
        [SerializeField] bool m_logSpecial = true;

        enum State
        {
            Idle,
            CutIn,
        }

        DarumaCharacter m_character;
        SpecialGauge m_gauge;
        HebiPoisonSwamp m_swamp;
        State m_state = State.Idle;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            m_gauge = GetComponent<SpecialGauge>();
        }

        private void Update()
        {
            if (m_character == null || m_gauge == null) return;

            if (m_state == State.Idle)
            {
                if (SpecialInput.WasPressed(m_character)) TryActivate();
                return;
            }

            if (m_state == State.CutIn && !HebidaruCutIn.IsPlaying)
            {
                SpawnSwamp();
                m_state = State.Idle;
            }
        }

        public bool TryActivate()
        {
            if (m_state != State.Idle || m_swamp != null) return false;
            if (m_character == null || m_gauge == null || !m_gauge.IsFull) return false;
            if (!IsLocalHuman()) return false;

            m_gauge.Consume();
            m_state = State.CutIn;

            GameObject subject = m_character.Stack != null && m_character.Stack.Head != null
                ? m_character.Stack.Head.gameObject
                : m_character.gameObject;
            HebidaruCutIn.Show(m_specialName, subject);

            if (m_logSpecial) Debug.Log("[へびだる] 反転毒沼", this);
            return true;
        }

        void SpawnSwamp()
        {
            if (m_swamp != null || m_character == null) return;

            var go = new GameObject("HebiPoisonSwamp");
            var swamp = go.AddComponent<HebiPoisonSwamp>();
            swamp.Initialize(m_character, m_swampRadius, m_swampDuration, m_debuffDuration,
                m_slowMultiplier, m_tickInterval, m_followCaster, m_swampOffset);
            swamp.Finished += OnSwampFinished;
            m_swamp = swamp;

            if (m_logSpecial)
                Debug.Log($"[へびだる] 毒沼発生 半径{m_swampRadius:0.0}m / {m_swampDuration:0.0}秒", this);
        }

        void OnSwampFinished()
        {
            if (m_swamp != null) m_swamp.Finished -= OnSwampFinished;
            m_swamp = null;
        }

        bool IsLocalHuman()
        {
            if (DarumaMatch.Current == null) return true;

            foreach (var p in DarumaMatch.Current.Players)
            {
                if (p.Character == m_character) return !p.IsCpu;
            }
            return false;
        }

        private void OnDisable()
        {
            if (m_swamp != null)
            {
                m_swamp.Finished -= OnSwampFinished;
                m_swamp.ClearAndDestroy(true);
                m_swamp = null;
            }
            m_state = State.Idle;
        }

        private void OnDestroy()
        {
            OnDisable();
        }

        private void OnGUI()
        {
            SpecialGaugeHUD.Draw(m_gauge, m_character);
        }
    }
}
