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
        // 気づいている危ない場所(隕石の予告円・ビーム・エクスカリバー)
        public CpuHazards Hazards { get; } = new CpuHazards();
        public float DeltaTime { get; private set; }

        public int StackCount => Self.Stack.Count;
        // 満タン近く(PlayfulStack 段以上)で、撃って遊ぶ側に切り替わっているか
        // (同じ段数の相手にも攻めに行く・積むより攻めを選ぶ・近くの積み木の固まりにも撃ち込む)
        public bool IsPlayful => StackCount >= Params.PlayfulStack;
        public Vector3 Position => Self.transform.position;

        // きだるの黄金化(無敵＋速さ 1.5 倍)の残り時間(秒。黄金化していなければ 0)
        // 黄金化中は逃げない・弾に反応しない・相手の振りをかわさずに攻め続ける・無敵で防げる危ない場所を気にしない
        public float GoldenRemaining => m_kidaru != null && m_kidaru.IsActive && Self.IsInvincible ? m_kidaru.RemainingSeconds : 0.0f;
        public bool IsGolden => GoldenRemaining > 0.0f;
        Yurube.KidaruSpecial m_kidaru;
        DarumaCharacter m_kidaruOwner;

        // 必殺技で無敵になっている残り時間の見込み(秒)。きだるの黄金化・いわだるの隕石(カウントダウン〜落ちたあとまで無敵)
        // この間は逃げない・弾に反応しない・無敵で防げる危ない場所を気にしない
        public float SpecialInvincibleRemaining
        {
            get
            {
                float t = GoldenRemaining;
                // いわだるは落ちたあとも 1.4 秒以上無敵が続く(IwadaruSpecial.TryActivate)。余裕を見て 1 秒足す
                if (m_iwadaru != null && m_iwadaru.IsActive && Self.IsInvincible) t = Mathf.Max(t, m_iwadaru.TimeToImpact + 1.0f);
                return t;
            }
        }
        public bool IsSpecialInvincible => SpecialInvincibleRemaining > 0.0f;
        Yurube.IwadaruSpecial m_iwadaru;

        // へびだるの毒沼が出ている(カットイン中も含む)。この間は、反転している敵から逃げず、段数が不利でも殴りに行く
        public bool IsSwampOut => m_hebidaru != null && m_hebidaru.IsActive;
        Yurube.HebidaruSpecial m_hebidaru;
        // 自分の毒沼で反転している敵か(へびだるの毒沼が出ている間だけ true)
        public bool IsTrapped(CpuOpponent o) => IsSwampOut && o != null && !o.IsGhost && o.Character != null && o.Character.MovementInverted;

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
            if (self != m_kidaruOwner)
            {
                m_kidaruOwner = self;
                m_kidaru = self != null ? self.GetComponent<Yurube.KidaruSpecial>() : null;
                m_iwadaru = self != null ? self.GetComponent<Yurube.IwadaruSpecial>() : null;
                m_hebidaru = self != null ? self.GetComponent<Yurube.HebidaruSpecial>() : null;
            }
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
