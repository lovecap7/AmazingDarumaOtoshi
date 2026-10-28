using UnityEngine;

namespace Nakahira
{
    // CPU(コンピュータープレイヤー)。人の入力と同じ操作命令(DarumaCommand)を出してキャラクターを動かす
    // 試合では MatchPlayer と同じGameObjectに付ける(操作する体は MatchPlayer から取る)。
    // キャラクターに直接付けた場合はそのキャラクターを動かす
    //
    // 知る(CpuPerception) → 決める(ここ) → 状態(積む・逃げる・殴る) → とっさの反応(CpuReflex。弾が来たら割り込む)
    // → 動きの部品(道をたどる・跳んで拾う)
    // 決める:ThinkInterval ごとに各状態のやりたさを比べ、一番高い状態にする
    //  ・今の状態には StateStickiness を足す(ころころ変えない)
    //  ・選んでから MinStateTime の間は変えない(UrgentScore 以上のやりたさなら変える)
    //  ・集中度(認識の疲れ。CpuContext.Load)の割合が状態のしきい値を超えていると、その状態へは移れない
    //    (今の状態が終わったときは移れる状態から選び、どれも移れなければ積む)
    // 詳しい考え方は Assets/Nakahira/Cpu/CPU設計メモ.md
    public class DarumaCpu : MonoBehaviour, IDarumaCommandSource
    {
        [SerializeField] CpuParams m_params;
        // 狙っている積み木・道・追っ手をシーンビューに表示する
        [SerializeField] bool m_drawGizmos = true;

        MatchPlayer m_player;
        DarumaCharacter m_character;
        readonly CpuContext m_context = new CpuContext();
        readonly CpuCollectState m_collect = new CpuCollectState();
        readonly CpuFleeState m_flee = new CpuFleeState();
        readonly CpuMeleeState m_melee = new CpuMeleeState();
        readonly CpuReflex m_reflex = new CpuReflex();
        CpuState[] m_states;
        CpuState m_current;
        float m_stateTime;
        float m_thinkTimer;
        // 前のフレームで動かしていた体と、その積み木の数(復活・1段→0段の判定用)
        DarumaCharacter m_lastSelf;
        int m_lastStackCount;

        public CpuParams Params
        {
            get => m_params;
            set => m_params = value;
        }

        // 今の状態の名前(デバッグ表示用。なければ空)
        public string StateName => m_current != null ? m_current.Name : "";
        // 今のとっさの反応(デバッグ表示用)
        public CpuReflex.Action ReflexAction => m_reflex.Current;
        // 集中度(認識の疲れ)の割合(デバッグ表示用)
        public float LoadRatio => m_params != null ? m_context.LoadRatio : 0.0f;

        private void Awake()
        {
            m_states = new CpuState[] { m_collect, m_flee, m_melee };
        }

        public DarumaCommand ReadCommand()
        {
            var cmd = new DarumaCommand();
            DarumaCharacter self = FindCharacter();
            // 幽霊のときは今は何もしない
            if (self == null || m_params == null)
            {
                ChangeState(null);
                m_lastSelf = null;
                return cmd;
            }

            float dt = Time.deltaTime;
            m_context.Begin(self, m_player, m_params, dt);

            // 復活などで体が変わったら、状態を最初から選び直す(疲れも消える)
            if (self != m_lastSelf)
            {
                ChangeState(null);
                m_lastSelf = self;
                m_lastStackCount = self.Stack.Count;
                m_context.ResetLoad();
                m_reflex.Reset();
            }

            m_context.DecayLoad();
            m_context.Perception.Update(m_context);

            int stack = self.Stack.Count;
            // 攻撃を受けた(自分の股抜きショットで減った分は除く):焦って疲れる
            if (stack < m_lastStackCount && !self.Hammer.IsShooting) m_context.AddLoad(m_params.AttentionOnHit);
            // 1段 → 0段になった瞬間は、逃げるかどうかをすぐに判断する
            bool becameZero = m_lastStackCount >= 1 && stack == 0;
            m_lastStackCount = stack;

            m_stateTime += dt;
            m_thinkTimer -= dt;
            if (m_current == null || becameZero || m_thinkTimer <= 0.0f)
            {
                m_thinkTimer = m_params.ThinkInterval;
                m_flee.ZeroStackCheck = becameZero;
                Decide(becameZero);
                m_flee.ZeroStackCheck = false;
            }

            if (m_current != null && !m_current.Tick(m_context, ref cmd))
            {
                // 状態が終わった(逃げ切ったなど):その時点の盤面で選び直す
                ChangeState(null);
                Decide(true);
                cmd = new DarumaCommand();
                if (m_current != null) m_current.Tick(m_context, ref cmd);
            }

            // とっさの反応:飛んでくる弾に、どの状態の最中でも割り込む
            m_reflex.Apply(m_context, ref cmd);
            return cmd;
        }

        // 各状態のやりたさを比べて、一番高い状態にする。force なら最低継続時間を待たない
        private void Decide(bool force)
        {
            m_context.ForgetExpiredGiveUps();

            CpuState best = m_current;
            float bestScore = m_current != null ? m_current.Evaluate(m_context) + m_params.StateStickiness : float.MinValue;
            float loadRatio = m_context.LoadRatio;
            foreach (var state in m_states)
            {
                if (state == m_current) continue;
                // 疲れていて(集中度の割合がしきい値を超えていて)気が回らない状態へは移れない
                if (loadRatio > state.Threshold(m_context)) continue;
                float score = state.Evaluate(m_context);
                if (score > bestScore)
                {
                    best = state;
                    bestScore = score;
                }
            }
            // 何も選べない(今の状態が終わって、ほかはどれも移れない)ときは積む
            if (best == null) best = m_collect;
            if (best == m_current) return;

            // 選んだばかりの状態は少し続ける(急ぐ理由があれば切り替える)
            if (m_current != null && !force && m_stateTime < m_params.MinStateTime && bestScore < m_params.UrgentScore) return;
            ChangeState(best);
        }

        private void ChangeState(CpuState next)
        {
            if (next == m_current) return;
            if (m_current != null) m_current.Exit(m_context);
            m_current = next;
            m_stateTime = 0.0f;
            if (m_current != null) m_current.Enter(m_context);
        }

        private DarumaCharacter FindCharacter()
        {
            // MatchPlayer はこのコンポーネントより後に付くので、使うときに取る
            if (m_player == null) m_player = GetComponent<MatchPlayer>();
            if (m_player != null) return m_player.Character;
            if (m_character == null) m_character = GetComponent<DarumaCharacter>();
            return m_character;
        }

        private void OnDrawGizmos()
        {
            if (!m_drawGizmos || !Application.isPlaying || m_current == null || m_lastSelf == null) return;
            m_current.DrawGizmos(m_context);
        }
    }
}
