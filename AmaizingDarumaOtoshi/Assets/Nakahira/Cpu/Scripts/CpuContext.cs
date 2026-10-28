using System.Collections.Generic;
using UnityEngine;

namespace Nakahira
{
    // CPU の状態・動きの部品が共通で使う情報(1人の CPU につき1つ)
    public class CpuContext
    {
        public DarumaCharacter Self { get; private set; }
        // 試合での自分(試合の外で動かしているときは null)
        public MatchPlayer Player { get; private set; }
        public CpuParams Params { get; private set; }
        public CpuPerception Perception { get; } = new CpuPerception();
        public float DeltaTime { get; private set; }

        public int StackCount => Self.Stack.Count;
        public Vector3 Position => Self.transform.position;

        // 集中度(認識の疲れ)。出来事(弾に反応した・攻撃を受けた等)のたびに足され、時間で減る
        // 判定には「疲れ ÷ 容量」の割合(LoadRatio)を使う。うまい CPU ほど容量が大きく、割合が上がりにくい
        //  ・割合が高いほど、弾・相手の振りに反応できる確率が下がる
        //  ・割合が状態のしきい値を超えていると、その状態へは移れない(殴り合いに夢中で逃げ遅れる など)
        public float Load { get; private set; }
        public float LoadRatio => Params.AttentionCapacity > 0.0f ? Load / Params.AttentionCapacity : 1.0f;
        // 反応できる確率に掛ける倍率(疲れていないと 1、容量いっぱいで 0)
        public float Alertness => 1.0f - Mathf.Clamp01(LoadRatio);

        // 殴り合いの相手が間合いの外へ逃げた(殴るが終わった)ときの相手と時刻。撃つの「追い撃ち」で使う
        public MatchPlayer FledTarget { get; set; }
        public float FledTime { get; set; } = -100.0f;

        // 焦り(0〜1)。集中度の割合が PanicStart を超えると上がり始め、割合 1 で最大
        public float Panic
        {
            get
            {
                float start = Params.PanicStart;
                return Mathf.Clamp01((LoadRatio - start) / Mathf.Max(0.01f, 1.0f - start));
            }
        }

        // 疲れの上限(容量に対する割合。いつまでも回復しないことがないように)
        const float kMaxLoadRatio = 1.5f;

        // 諦めた積み木と、また狙ってよくなる時刻(状態をまたいで覚えておく)
        readonly Dictionary<TumikiBlock, float> m_givenUp = new Dictionary<TumikiBlock, float>();
        readonly List<TumikiBlock> m_expired = new List<TumikiBlock>();

        public void Begin(DarumaCharacter self, MatchPlayer player, CpuParams p, float dt)
        {
            Self = self;
            Player = player;
            Params = p;
            DeltaTime = dt;
        }

        // 疲れを足す
        public void AddLoad(float amount)
        {
            Load = Mathf.Min(Load + amount, Params.AttentionCapacity * kMaxLoadRatio);
        }

        // 時間で疲れが減る(毎フレーム呼ぶ)
        public void DecayLoad()
        {
            Load = Mathf.Max(0.0f, Load - Params.AttentionDecay * DeltaTime);
        }

        public void ResetLoad()
        {
            Load = 0.0f;
        }

        public bool IsGivenUp(TumikiBlock block) => m_givenUp.ContainsKey(block);

        public void GiveUp(TumikiBlock block)
        {
            GiveUp(block, Params.GiveUpCooldown);
        }

        // seconds 秒のあいだ、この積み木を狙わない
        public void GiveUp(TumikiBlock block, float seconds)
        {
            if (block != null) m_givenUp[block] = Time.time + seconds;
        }

        // また狙ってよくなった積み木を忘れる
        public void ForgetExpiredGiveUps()
        {
            m_expired.Clear();
            foreach (var pair in m_givenUp)
            {
                if (pair.Key == null || Time.time >= pair.Value) m_expired.Add(pair.Key);
            }
            foreach (var block in m_expired) m_givenUp.Remove(block);
        }

        // 2点の水平の距離
        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0.0f;
            b.y = 0.0f;
            return Vector3.Distance(a, b);
        }
    }
}
