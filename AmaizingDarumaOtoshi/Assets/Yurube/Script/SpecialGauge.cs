using System;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // 必殺技ゲージ(2026-11-12 中平が作り直した)。必殺技を持つキャラクターのプレハブに付け、必殺技側から IsFull / Consume を読む。
    // 最大ポイントと得る量はキャラごとの SpecialGaugeParams で決める。表示は最大に対する割合(0〜100%)。
    // 得る量(多い順): 攻撃判定のある積み木にジャンプして乗る > 打ち返す > 直接殴ってダメージ > 積む > 敵が近くにいる(毎秒) > 移動する(毎秒)
    //  ・ジャンプして乗った積み木は積まれるが、乗る分だけ入れる(多い方だけ)
    //  ・上限で拾って古い段がこぼれる「入れ替え」と、パッシブで増えた段は「積む」に入れない
    //  ・カウントダウン中・時間が止まっている間・試合が終わったあと・必殺技の効果中(ISpecialAbility.IsActive)は溜めない
    //  ・やられたら溜まり具合を次の体に引き継ぎ、おまけを足す(DarumaMatch が預かる)
    // 表示は Canvas の SpecialGaugeHud(Nakahira)。必殺技の OnGUI から呼ぶ SpecialGaugeHUD.Draw は、状態の文字を渡すだけになった
    [RequireComponent(typeof(DarumaCharacter))]
    public class SpecialGauge : MonoBehaviour
    {
        // 何で得たか(テスト・演出用)
        public enum GainReason { RideHarmfulBlock, ReturnBlock, DirectHit, StackBlock, NearEnemy, Move, Carry, Other }

        [Tooltip("最大ポイント・得る量(キャラごと)。空なら初期値で動く")]
        [SerializeField] SpecialGaugeParams m_params;

        static SpecialGaugeParams s_fallback;

        DarumaCharacter m_character;
        Rigidbody m_rb;
        ISpecialAbility m_ability;
        float m_points;
        Vector2 m_lastMove;
        bool m_subscribed;
        bool m_carryChecked;
        string m_status;
        int m_statusFrame = -100;

        public SpecialGaugeParams Params
        {
            get
            {
                if (m_params != null) return m_params;
                if (s_fallback == null)
                {
                    s_fallback = ScriptableObject.CreateInstance<SpecialGaugeParams>();
                    s_fallback.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_fallback;
            }
        }

        public DarumaCharacter Character => m_character;
        public float Points => m_points;
        public float MaxPoints => Mathf.Max(1.0f, Params.MaxPoints);
        public bool IsFull => m_points >= MaxPoints;
        public float Normalized => Mathf.Clamp01(m_points / MaxPoints);
        // 表示用の割合(満タンのときだけ 100)
        public int Percent => IsFull ? 100 : Mathf.Min(99, Mathf.FloorToInt(Normalized * 100.0f));
        // 必殺技の効果中(溜めない)
        public bool IsSpecialActive
        {
            get
            {
                if (m_ability == null) m_ability = GetComponent<ISpecialAbility>();
                return m_ability != null && m_ability.IsActive;
            }
        }
        // 必殺技が出している状態の文字(「POWER UP 3.2s」など。なければ null)。必殺技の OnGUI から毎フレーム渡される
        public string StatusOverride => Time.frameCount - m_statusFrame <= 2 ? m_status : null;
        // ネット対戦のクライアント: 自分では溜めず、ホストの値を受け取るだけ(NetworkDarumaCharacter が立てる)
        public bool NetDriven { get; set; }

        // 必殺技を発動してゲージを消費した（実績・演出用。発動した瞬間に1回だけ来る）
        public static event Action<SpecialGauge> Consumed;
        // ゲージが増えた(増えた量・理由)
        public static event Action<SpecialGauge, float, GainReason> Gained;

        // ドメインの再読み込みをしない設定でも、再生のたびに受け取り手を空にする
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Consumed = null;
            Gained = null;
        }

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            m_rb = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            if (m_character == null || m_subscribed) return;
            m_subscribed = true;
            DarumaCharacter.AnyPickedUp += OnPickedUp;
            DarumaHammer.Hit += OnHammerHit;
            m_character.Eliminated += OnEliminated;
            m_character.CommandReceived += OnCommand;
        }

        private void OnDisable()
        {
            if (!m_subscribed) return;
            m_subscribed = false;
            DarumaCharacter.AnyPickedUp -= OnPickedUp;
            DarumaHammer.Hit -= OnHammerHit;
            if (m_character != null)
            {
                m_character.Eliminated -= OnEliminated;
                m_character.CommandReceived -= OnCommand;
            }
        }

        private void Start()
        {
            // 試合のゲージの表示を出す(試合で 1 つだけ)
            if (DarumaMatch.Current != null) SpecialGaugeHud.Ensure(Params.HudPrefab);
            TakeCarry();
        }

        private void Update()
        {
            // 体が試合に登録される前に Start が来ても受け取れるように、まだなら毎フレーム見る
            if (!m_carryChecked) TakeCarry();
            if (!CanGain()) return;

            float dt = Time.deltaTime;
            if (dt <= 0.0f) return;
            var p = Params;
            if (p.NearEnemyPerSecond > 0.0f && IsEnemyNear(p.NearEnemyDistance)) Add(p.NearEnemyPerSecond * dt, GainReason.NearEnemy);
            if (p.MovePerSecond > 0.0f && IsMoving(p.MoveMinSpeed)) Add(p.MovePerSecond * dt, GainReason.Move);
        }

        //========================================
        // 得る
        //========================================

        void OnPickedUp(TumikiPickupInfo info)
        {
            if (info.Character != m_character || !CanGain()) return;
            // 乗ると積むが重なるときは多い方(乗る)だけ
            if (info.WasHarmful) Add(Params.RideHarmfulBlock, GainReason.RideHarmfulBlock);
            else if (info.StackGrew) Add(Params.StackBlock, GainReason.StackBlock);
        }

        void OnHammerHit(HammerHitInfo hit)
        {
            if (m_character == null || hit.Hammer == null || hit.Hammer != m_character.Hammer || !CanGain()) return;
            if (hit.ReturnCount > 0) Add(Params.ReturnBlock, GainReason.ReturnBlock);
            else if (hit.Damaged && hit.Target is DarumaCharacter) Add(Params.DirectHit, GainReason.DirectHit);
        }

        void OnCommand(DarumaCommand command)
        {
            m_lastMove = command.Move;
        }

        // やられた: 溜まり具合とおまけを試合に預け、次の体が受け取る
        void OnEliminated(DarumaCharacter character)
        {
            if (NetDriven) return;
            var match = DarumaMatch.Current;
            if (match == null || match.IsFinished) return;
            var player = match.PlayerOf(m_character);
            if (player == null) return;
            float carry = Mathf.Min(MaxPoints, m_points + MaxPoints * Params.DeathBonusRatio);
            match.StoreSpecialGaugeCarry(player, carry);
        }

        void TakeCarry()
        {
            var match = DarumaMatch.Current;
            if (NetDriven || match == null) { m_carryChecked = true; return; }
            var player = match.PlayerOf(m_character);
            if (player == null) return;
            m_carryChecked = true;
            if (match.TakeSpecialGaugeCarry(player, out float carry)) Add(carry, GainReason.Carry);
        }

        bool CanGain()
        {
            if (NetDriven || m_character == null || m_character.IsEliminated || IsFull) return false;
            if (Time.timeScale <= 0.0f) return false;
            var match = DarumaMatch.Current;
            if (match != null && (match.IsCountingDown || match.IsFinished || match.IsPaused)) return false;
            return !IsSpecialActive;
        }

        bool IsEnemyNear(float distance)
        {
            var match = DarumaMatch.Current;
            if (match == null) return false;
            var me = match.PlayerOf(m_character);
            Vector3 pos = m_character.transform.position;
            float sqr = distance * distance;
            foreach (var other in match.Players)
            {
                if (other == null || other == me) continue;
                var c = other.Character;
                // 幽霊・復活待ち(体がない)は数えない
                if (c == null || c == m_character || c.IsEliminated) continue;
                if (me != null && match.AreTeammates(me, other)) continue;
                Vector3 d = c.transform.position - pos;
                d.y = 0.0f;
                if (d.sqrMagnitude <= sqr) return true;
            }
            return false;
        }

        bool IsMoving(float minSpeed)
        {
            if (m_lastMove.sqrMagnitude < 0.04f || m_rb == null) return false;
            Vector3 v = m_rb.linearVelocity;
            v.y = 0.0f;
            return v.sqrMagnitude >= minSpeed * minSpeed;
        }

        //========================================
        // 外から使う
        //========================================

        // ポイントを足す(満タンで止まる)
        public void Add(float points, GainReason reason = GainReason.Other)
        {
            if (points <= 0.0f || IsFull) return;
            float before = m_points;
            m_points = Mathf.Min(MaxPoints, m_points + points);
            Gained?.Invoke(this, m_points - before, reason);
        }

        public void Fill()
        {
            m_points = MaxPoints;
        }

        public void Consume()
        {
            m_points = 0.0f;
            Consumed?.Invoke(this);
        }

        // 必殺技の OnGUI から、いま出したい状態の文字を渡す(null なら割合を出す)
        public void SetStatusOverride(string status)
        {
            m_status = status;
            m_statusFrame = Time.frameCount;
        }

        // ネット対戦のクライアント: ホストから届いた割合を入れる(1 なら満タン)
        public void NetSetNormalized(float normalized)
        {
            m_points = normalized >= 1.0f ? MaxPoints : Mathf.Clamp01(normalized) * MaxPoints;
        }
    }
}
