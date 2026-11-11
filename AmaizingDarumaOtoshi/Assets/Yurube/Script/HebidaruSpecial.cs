using Nakahira;
using UnityEngine;

namespace Yurube
{
    // へびだるの必殺技「反転毒沼」。
    // カットイン後、発動位置を中心に円形の毒沼を生成する。
    // 沼に入った敵には移動速度低下＋移動入力反転のデバフを付与する。
    [RequireComponent(typeof(DarumaCharacter))]
    public class HebidaruSpecial : MonoBehaviour, ISpecialAbility, INetSpecial
    {
        [Header("必殺技")]
        [SerializeField] string m_specialName = "反転毒沼";
        [Tooltip("積み木1個のときの毒沼の半径(m)。表示円と当たり判定の両方に使う")]
        [SerializeField, Min(0.5f)] float m_minSwampRadius = 2.5f;
        [Tooltip("最大積み木数のときの毒沼の半径(m)")]
        [SerializeField, Min(0.5f)] float m_maxSwampRadius = 6.5f;
        [Tooltip("毒沼の半径の全体倍率(表示円・当たり判定の両方に適用)")]
        [SerializeField, Min(0.1f)] float m_swampRadiusScale = 1.5f;
        [Tooltip("性能計算に使う最大積み木数")]
        [SerializeField, Min(1)] int m_maxBlocks = 10;
        [Tooltip("毒沼がフィールドに残る時間(秒)")]
        [SerializeField, Min(0.1f)] float m_swampDuration = 8.0f;
        [Tooltip("毒沼が消えるときのフェード時間(秒)")]
        [SerializeField, Min(0.0f)] float m_swampFadeDuration = 0.7f;
        [Tooltip("毒沼に入った敵に付与するデバフの持続時間(秒)")]
        [SerializeField, Min(0.1f)] float m_debuffDuration = 3.0f;
        [Tooltip("デバフ中の移動速度倍率(1で通常、0.5で半分)")]
        [SerializeField, Range(0.05f, 1.0f)] float m_slowMultiplier = 0.55f;
        [Tooltip("毒沼の当たり判定を確認する間隔(秒)")]
        [SerializeField, Min(0.02f)] float m_tickInterval = 0.15f;
        [Tooltip("毒沼が発動者に追従するか")]
        [SerializeField] bool m_followCaster = true;
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
        int m_snapshotBlocks;
        float m_activeSwampRadius;

        public bool IsActive => m_state != State.Idle || m_swamp != null;

        // CPU 用(中平): 積み木 blocks 個で撃ったときの毒沼の半径(m。TryActivate と同じ計算)
        public float PredictRadius(int blocks)
        {
            int n = Mathf.Clamp(blocks, 1, m_maxBlocks);
            float t = m_maxBlocks > 1 ? Mathf.InverseLerp(1.0f, m_maxBlocks, n) : 1.0f;
            return Mathf.Lerp(m_minSwampRadius, m_maxSwampRadius, t) * Mathf.Max(0.1f, m_swampRadiusScale);
        }

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
                // CPUのへびだるが、人間のキー入力で必殺技を出さないようにする(ほかのキャラと同じ)
                if (IsLocalHuman() && SpecialInput.WasPressed(m_character)) TryActivate();
                return;
            }

            if (m_state == State.CutIn && !HebidaruCutIn.IsPlaying)
            {
                SpawnSwamp();
                m_state = State.Idle;
            }
        }

        // ラボのボタンや CPU からも呼べる。発動できたら true
        public bool TryActivate()
        {
            if (m_state != State.Idle || m_swamp != null) return false;
            if (m_character == null || m_gauge == null || !m_gauge.IsFull) return false;

            int blocks = m_character.Stack != null ? m_character.Stack.Count : 0;
            // ネット対戦用(suehara): クライアントでも同じカットイン・毒沼を出す(積み木の数で沼の大きさが決まる)
            SpecialNet.Send(m_character, this, kNetActivate, new float[] { blocks });
            m_gauge.Consume();
            BeginCutIn(blocks);

            if (m_logSpecial) Debug.Log("[へびだる] 反転毒沼", this);
            return true;
        }

        // 沼の大きさを決めてカットインを始める(終わったら Update で毒沼を出す。ホストとクライアントで同じ)
        void BeginCutIn(int blocks)
        {
            m_snapshotBlocks = Mathf.Clamp(blocks, 1, m_maxBlocks);
            float t = m_maxBlocks > 1
                ? Mathf.InverseLerp(1.0f, m_maxBlocks, m_snapshotBlocks)
                : 1.0f;
            m_activeSwampRadius = Mathf.Lerp(m_minSwampRadius, m_maxSwampRadius, t)
                * Mathf.Max(0.1f, m_swampRadiusScale);

            m_state = State.CutIn;

            GameObject subject = m_character.Stack != null && m_character.Stack.Head != null
                ? m_character.Stack.Head.gameObject
                : m_character.gameObject;
            HebidaruCutIn.Show(m_specialName, subject);
        }

        //========================================
        // ネット対戦用(suehara): クライアントで、ホストの発動と同じカットイン・毒沼を出す
        // (毒沼が付ける毒はクライアントでは見た目だけ。表示専用のキャラクターは動きが止めてあるため)
        //========================================

        const int kNetActivate = 0;

        public void NetReceive(int ev, float[] v, TumikiBlock[] blocks)
        {
            if (ev != kNetActivate || v.Length < 1 || m_state != State.Idle || m_swamp != null) return;
            BeginCutIn(Mathf.RoundToInt(v[0]));
        }

        void SpawnSwamp()
        {
            if (m_swamp != null || m_character == null) return;

            var go = new GameObject("HebiPoisonSwamp");
            var swamp = go.AddComponent<HebiPoisonSwamp>();
            swamp.Initialize(m_character, m_activeSwampRadius, m_swampDuration, m_swampFadeDuration,
                m_debuffDuration, m_slowMultiplier, m_tickInterval, m_followCaster, m_swampOffset);
            swamp.Finished += OnSwampFinished;
            m_swamp = swamp;

            if (m_logSpecial)
                Debug.Log($"[へびだる] 毒沼発生 積み木{m_snapshotBlocks}個 / 半径{m_activeSwampRadius:0.0}m / {m_swampDuration:0.0}秒", this);
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
