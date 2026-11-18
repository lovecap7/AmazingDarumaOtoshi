using System.Collections;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // ねずみ花火(Stage26「縁側のねずみ花火」)。時間切れで積み木がはじけ飛ぶ火種を、ダメージを与えて押し付け合う
    //  ・最初の火種: 開始から m_firstDelay 秒後に、いちばん高く積んでいる人に付く(同じ数なら抽選)
    //  ・持っている人は移動が m_holderSpeed 倍。積み木かハンマーでほかの人に「崩した」と数えられるダメージを与えると、その人に移る
    //    (移った時点で残りが m_minTimeOnPass 秒未満なら、その秒数に戻す。移した直後 m_passBackBlock 秒は、移してきた相手へ移し返せない)
    //    移るときは、火種が当てた人の頭から当てられた人の頭へ m_passDuration 秒かけて弧を描いて飛ぶ(その間は時間が減らない)
    //  ・持っている人の攻撃判定のある弾(当たると火種が移る弾)からは、火花が出る
    //  ・時間切れ: 持っている人の積み木が、上から1つずつ m_blockInterval 秒おきに爆発してはじけ飛ぶ(持ち主なしの弾になる)。頭だけなら、やられる
    //    (ストックは1減る・タイムは「相手なしでやられた」・ゴーストは幽霊になる。どれも試合のいつもの処理)
    //  ・時間切れ・持っている人がやられたあと、m_cooldown 秒で次の火種がいちばん高い人に付く。場にいる生存者が2人未満なら付かない
    //  ・幽霊・復活待ち・無敵中の人は受け取らない
    // 見た目: 持っている人の頭の上で花火が回り、足元に暗い輪。残りが減るほど火花が大きく速く、音が速くなる
    //  爆発直前(残り m_warningTime 秒): 花火が大きくなって黄色く点滅し、足元から黄色の光の柱が立ち、床に黄色の輪が広がり、拍子の音が速くなる
    // 開始のカウントダウン中は止まる。ネット対戦ではホストが判定し、クライアントは状態を受け取って見た目だけ合わせる
    public class NezumiHanabiGimmick : MonoBehaviour, INetGimmickState
    {
        // Passing: 火種が持っている人から当てられた人へ飛んでいる途中(m_passDuration 秒。その間は時間が減らない)
        public enum Phase { Waiting, Burning, Cooldown, Passing }

        [Header("時間")]
        [Tooltip("試合が始まってから、最初の火種がいちばん高く積んでいる人に付くまでの時間(秒)")]
        [SerializeField, Min(0.0f)] float m_firstDelay = 15.0f;
        [Tooltip("火種が付いてから爆発するまでの時間(秒)。ほかの人に移しても、残り時間はそのまま引き継ぐ")]
        [SerializeField, Min(1.0f)] float m_fuseTime = 10.0f;
        [Tooltip("爆発した・持っている人がやられて火が消えたあと、次の火種が付くまでの時間(秒)")]
        [SerializeField, Min(0.0f)] float m_cooldown = 15.0f;
        [Tooltip("ほかの人に移ったとき、残り時間がこれより短ければ、この秒数に戻す(移された直後に爆発しないように)")]
        [SerializeField, Min(0.0f)] float m_minTimeOnPass = 2.0f;
        [Tooltip("移した直後、移してきた相手へ移し返せない時間(秒)。着いた時点から数える")]
        [SerializeField, Min(0.0f)] float m_passBackBlock = 0.5f;
        // 移るとき、火種が当てた人から当てられた人へ飛んでいく時間(秒)と、弧の高さ(m)
        [Tooltip("移るとき、火種が当てた人の頭から当てられた人の頭へ飛んでいく時間(秒)。飛んでいる間は残り時間が減らない")]
        [SerializeField, Min(0.05f)] float m_passDuration = 0.5f;
        [Tooltip("移るときに火種が描く弧の高さ(m)")]
        [SerializeField] float m_passArcHeight = 1.5f;
        [Tooltip("爆発の何秒前から、爆発直前の予兆(黄色の点滅・光の柱・床の輪・速い拍子の音)を出すか")]
        [SerializeField, Min(0.0f)] float m_warningTime = 3.0f;

        [Header("持っている人")]
        [Tooltip("火種を持っている人の移動の速さの倍率(1.2 = 20% 速い)")]
        [SerializeField] float m_holderSpeed = 1.2f;

        [Header("はじける")]
        [Tooltip("爆発したとき、積み木がはじけ飛ぶ速さ")]
        [SerializeField] float m_scatterSpeed = 9.0f;
        // 積み木を上から1つずつ爆発させる間隔(秒)
        [Tooltip("積み木を上から1つずつ爆発させる間隔(秒)。0 なら全部いっぺんに")]
        [SerializeField, Min(0.0f)] float m_blockInterval = 0.07f;
        [Tooltip("積み木1つが爆発するときのエフェクト(数・速さ・大きさ・色など)")]
        [SerializeField] GimmickBurstSettings m_blockBurst = new GimmickBurstSettings
        {
            Additive = true, ColorA = new Color(1.0f, 0.97f, 0.9f, 1.0f), ColorB = new Color(0.95f, 0.8f, 0.5f, 1.0f),
            Count = 22, Speed = new Vector2(2.5f, 6.0f), Size = new Vector2(0.06f, 0.16f), Lifetime = new Vector2(0.2f, 0.45f),
            Gravity = 1.0f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.4f, 0, 0), Stretch = true,
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.95f, 0.85f, 0.5f), ShockwaveRadius = 1.4f, ShockwaveTime = 0.2f,
        };

        [Header("見た目")]
        [Tooltip("頭の上で回る花火の色(明るい側)")]
        [SerializeField] Color m_sparkColorA = new Color(1.0f, 0.97f, 0.88f, 1.0f);
        [Tooltip("頭の上で回る花火の色(暗い側)")]
        [SerializeField] Color m_sparkColorB = new Color(0.95f, 0.85f, 0.6f, 1.0f);
        [Tooltip("火種を持っている人の足元に出る輪の色")]
        [SerializeField] Color m_ringColor = new Color(0.12f, 0.1f, 0.16f, 0.55f);
        [Tooltip("爆発直前の予兆の色(黄色 = 注意。ギミック共通の色)")]
        [SerializeField] Color m_warningColor = GimmickPalette.Warning;
        [Tooltip("「誰かに花火がついた！」の告知の文字色")]
        [SerializeField] Color m_messageColor = new Color(1.0f, 0.93f, 0.78f, 1.0f);
        [Tooltip("頭の上の花火から常に散る火花のエフェクト")]
        [SerializeField] GimmickBurstSettings m_sparks = new GimmickBurstSettings
        {
            Additive = true, ColorA = new Color(1.0f, 0.97f, 0.88f, 1.0f), ColorB = new Color(0.95f, 0.82f, 0.55f, 1.0f),
            Count = 5, Speed = new Vector2(1.5f, 3.5f), Size = new Vector2(0.04f, 0.09f), Lifetime = new Vector2(0.12f, 0.25f),
            Gravity = 1.5f, Shape = GimmickBurstShape.Ring, ShapeSize = new Vector3(0.3f, 0, 0), Stretch = true,
        };
        [Tooltip("火種が移ったとき(着いたとき)のエフェクト")]
        [SerializeField] GimmickBurstSettings m_passEffect = new GimmickBurstSettings
        {
            Additive = true, ColorA = new Color(1.0f, 0.97f, 0.88f, 1.0f), ColorB = new Color(0.95f, 0.85f, 0.6f, 1.0f),
            Count = 18, Speed = new Vector2(2.0f, 4.0f), Size = new Vector2(0.05f, 0.12f), Lifetime = new Vector2(0.2f, 0.4f),
            Gravity = 1.0f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.3f, 0, 0), Stretch = true,
        };
        [Tooltip("時間切れで頭の上の花火が破裂するときの大きなエフェクト")]
        [SerializeField] GimmickBurstSettings m_explosion = new GimmickBurstSettings
        {
            Additive = true, ColorA = new Color(1.0f, 0.97f, 0.9f, 1.0f), ColorB = new Color(0.95f, 0.8f, 0.5f, 1.0f),
            Count = 70, Speed = new Vector2(4.0f, 10.0f), Size = new Vector2(0.08f, 0.22f), Lifetime = new Vector2(0.3f, 0.7f),
            Gravity = 1.2f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.6f, 0, 0), Stretch = true,
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.95f, 0.85f, 0.6f), ShockwaveRadius = 4.0f, ShockwaveTime = 0.35f,
        };
        // 火種を持っている人の弾(当たると火種が移る弾)から出る火花
        [Tooltip("火種を持っている人が打った弾(当たると火種が移る弾)から出る火花のエフェクト")]
        [SerializeField] GimmickBurstSettings m_shotSparks = new GimmickBurstSettings
        {
            Additive = true, ColorA = new Color(1.0f, 0.97f, 0.88f, 1.0f), ColorB = new Color(0.95f, 0.82f, 0.55f, 1.0f),
            Count = 8, Speed = new Vector2(1.5f, 3.5f), Size = new Vector2(0.07f, 0.15f), Lifetime = new Vector2(0.2f, 0.4f),
            Gravity = 1.5f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.4f, 0, 0), Stretch = true,
        };
        [Tooltip("火種が移る弾から火花を出す間隔(秒)。小さいほど火花が多い")]
        [SerializeField, Min(0.01f)] float m_shotSparkInterval = 0.04f;
        // 火種が移る弾の上で回る、小さな花火の輪の大きさ(m)。通常の弾の炎の尾と見分けるため
        [Tooltip("火種が移る弾の上で回る、小さな花火の輪の大きさ(m)。ふつうの弾の炎の尾と見分けるためのもの")]
        [SerializeField] float m_shotWheelSize = 0.55f;
        [Tooltip("爆発直前に頭の上の花火から出る黄色の火花のエフェクト")]
        [SerializeField] GimmickBurstSettings m_warningSparks = new GimmickBurstSettings
        {
            Additive = true, ColorA = new Color(1.0f, 0.95f, 0.45f, 1.0f), ColorB = new Color(1.0f, 0.98f, 0.85f, 1.0f),
            Count = 16, Speed = new Vector2(3.0f, 6.0f), Size = new Vector2(0.06f, 0.14f), Lifetime = new Vector2(0.18f, 0.35f),
            Gravity = 1.2f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.4f, 0, 0), Stretch = true,
        };
        // 爆発直前の光の柱の高さ(m)と、床に広がる輪の大きさ(m)
        [Tooltip("爆発直前に足元から立つ黄色の光の柱の高さ(m)")]
        [SerializeField] float m_warningBeamHeight = 7.0f;
        [Tooltip("爆発直前に足元から床に広がる黄色の輪の大きさ(半径 m)")]
        [SerializeField] float m_warningPulseRadius = 3.0f;

        [Header("音")]
        [Tooltip("火種が燃えている間のシュルシュル音(ループ)の音量")]
        [SerializeField, Range(0.0f, 1.0f)] float m_fuseVolume = 0.25f;
        [Tooltip("爆発直前の拍子の音の音量")]
        [SerializeField, Range(0.0f, 1.0f)] float m_warningVolume = 0.45f;
        [Tooltip("爆発の音の音量")]
        [SerializeField, Range(0.0f, 1.0f)] float m_explosionVolume = 0.8f;

        Phase m_phase = Phase.Waiting;
        float m_timer;          // Waiting / Cooldown: 次の火種までの残り。Burning: 爆発までの残り
        int m_holderIndex = -1; // 持っている人のプレイヤー番号(0 = 1P)
        int m_lastGiverIndex = -1;
        float m_passTimer;
        float m_time;
        float m_emitTimer;
        float m_beatTimer;
        float m_pulse;          // 爆発直前の拍子(1 で打った瞬間 → 0)
        DarumaMatch m_match;
        Transform m_firework;
        MeshRenderer m_wheel, m_wheelGlow, m_ring, m_beam, m_pulseRing;
        AudioSource m_fuseSound;
        static AudioClip s_fuseClip;

        // 火種が飛んでいる途中: 行き先のプレイヤー番号・飛び始めてからの時間・両端の位置(体がなくなったときのため覚えておく)
        int m_passToIndex = -1;
        float m_passElapsed;
        Vector3 m_passFromPos, m_passToPos;

        // 火種を持っている人の弾の位置(火花を出す所)。ホストは毎回探し、クライアントはホストから受け取る
        const int kMaxShots = 6;
        readonly Vector3[] m_shots = new Vector3[kMaxShots];
        readonly bool[] m_shotActive = new bool[kMaxShots];
        readonly MeshRenderer[] m_shotWheels = new MeshRenderer[kMaxShots];
        float m_shotSparkTimer;

        public Phase CurrentPhase => m_phase;
        public float TimeLeft => m_timer;

        private void Awake()
        {
            m_firework = new GameObject("Firework").transform;
            m_firework.SetParent(transform, false);
            m_wheel = EffectMeshes.Create("Wheel", m_firework, EffectMeshes.ZigzagRing(10, 0.25f), true);
            m_wheelGlow = EffectMeshes.Create("Glow", m_firework, EffectMeshes.Disc, true);
            m_ring = EffectMeshes.Create("HolderRing", transform, EffectMeshes.Ring, false);
            m_beam = EffectMeshes.Create("WarningBeam", transform, EffectMeshes.Tube, true);
            m_pulseRing = EffectMeshes.Create("WarningPulse", transform, EffectMeshes.Ring, true);
            var shotWheel = EffectMeshes.ZigzagRing(8, 0.3f);
            for (int i = 0; i < kMaxShots; i++)
            {
                m_shotWheels[i] = EffectMeshes.Create($"ShotWheel{i}", transform, shotWheel, true);
                m_shotWheels[i].enabled = false;
            }
            m_fuseSound = GimmickSound.CreateLoop(transform, FuseClip);
            SetVisible(false);
        }

        private void OnEnable()
        {
            m_match = DarumaMatch.Current;
            if (m_match != null)
            {
                m_match.Started += ResetFire;
                m_match.PlayerKnocked += OnPlayerKnocked;
            }
            ResetFire();
        }

        private void OnDisable()
        {
            if (m_match != null)
            {
                m_match.Started -= ResetFire;
                m_match.PlayerKnocked -= OnPlayerKnocked;
            }
            m_match = null;
            StopAllCoroutines();
            GimmickSound.SetLoopVolume(m_fuseSound, 0.0f);
        }

        public void ResetFire()
        {
            m_phase = Phase.Waiting;
            m_timer = m_firstDelay;
            m_holderIndex = -1;
            m_lastGiverIndex = -1;
            m_passTimer = 0.0f;
            m_passToIndex = -1;
            for (int i = 0; i < kMaxShots; i++) m_shotActive[i] = false;
            SetVisible(false);
        }

        //========================================
        // 判定(ホスト)
        //========================================

        private void FixedUpdate()
        {
            if (GimmickClock.IsNetClient || GimmickClock.IsHeld) return;
            var match = DarumaMatch.Current;
            if (match == null || match.IsFinished || match.IsNetWaiting) return;
            using var relay = GimmickNet.Relay();   // ネット対戦用(suehara)
            float dt = Time.fixedDeltaTime;
            // 火種が飛んでいる間は、爆発までの時間を減らさない
            if (m_phase != Phase.Passing) m_timer -= dt;
            m_passTimer += dt;

            switch (m_phase)
            {
                case Phase.Passing:
                    UpdatePassing(match, dt);
                    break;
                case Phase.Waiting:
                case Phase.Cooldown:
                    if (m_timer > 0.0f) break;
                    if (!Ignite(match, m_phase == Phase.Waiting)) m_timer = 1.0f;   // 付けられなければ1秒後にもう一度
                    break;
                case Phase.Burning:
                    MatchPlayer holder = Holder(match);
                    DarumaCharacter c = holder != null && holder.State == MatchPlayer.PlayerState.Alive ? holder.Character : null;
                    if (c == null || c.IsEliminated)
                    {
                        // 持っている人が別の理由でやられた: 火は消える
                        PutOut();
                        break;
                    }
                    if (c.CanBeAffected) c.RequestSpeedScale(m_holderSpeed);
                    if (m_timer <= 0.0f) Explode(c);
                    break;
            }
        }

        // いちばん高く積んでいる人に火を付ける。付けたら true
        private bool Ignite(DarumaMatch match, bool first)
        {
            int alive = 0, best = -1;
            int count = 0;
            MatchPlayer pick = null;
            foreach (var p in match.Players)
            {
                if (!CanHold(p)) continue;
                alive++;
                int n = p.StackCount;
                if (n > best)
                {
                    best = n;
                    count = 1;
                    pick = p;
                }
                else if (n == best)
                {
                    // 同じ数なら抽選(数えた人数で割った確率で入れ替える)
                    count++;
                    if (Random.Range(0, count) == 0) pick = p;
                }
            }
            if (alive < 2 || pick == null) return false;

            m_phase = Phase.Burning;
            m_timer = m_fuseTime;
            m_holderIndex = pick.Index;
            m_lastGiverIndex = -1;
            m_passTimer = m_passBackBlock;
            if (first) StageMessageUI.Show("誰かに花火がついた！", m_messageColor);
            if (pick.Character != null) GimmickBurst.Play(m_passEffect, HeadPosition(pick.Character));
            GimmickSound.Play(GimmickSound.Pop, 0.5f, 1.4f);
            return true;
        }

        private static bool CanHold(MatchPlayer p)
        {
            return p != null && p.State == MatchPlayer.PlayerState.Alive && p.Character != null
                && !p.Character.IsEliminated && !p.Character.IsInvincible;
        }

        // 火種を持っている人が、ほかの人を崩した(試合が当てた人を決めたあとに呼ばれる)
        private void OnPlayerKnocked(MatchPlayer victim, MatchPlayer attacker)
        {
            if (GimmickClock.IsNetClient || m_phase != Phase.Burning) return;
            if (attacker == null || victim == null || attacker == victim || attacker.Index != m_holderIndex) return;
            if (!CanHold(victim)) return;
            if (victim.Index == m_lastGiverIndex && m_passTimer < m_passBackBlock) return;

            using var relay = GimmickNet.Relay();   // ネット対戦用(suehara)
            // すぐには移さず、火種が当てた人の頭から当てられた人の頭へ飛んでいく
            m_phase = Phase.Passing;
            m_lastGiverIndex = attacker.Index;
            m_passToIndex = victim.Index;
            m_passElapsed = 0.0f;
            m_passFromPos = attacker.Character != null ? HeadPosition(attacker.Character) : HeadPosition(victim.Character);
            m_passToPos = HeadPosition(victim.Character);
            GimmickBurst.Play(m_passEffect, m_passFromPos);
            GimmickSound.Play(GimmickSound.Launch, 0.4f, 1.5f);
        }

        // 火種が飛んでいる途中。着いたら当てられた人のものになる(着いたときに受け取れなければ、火は消える)
        private void UpdatePassing(DarumaMatch match, float dt)
        {
            m_passElapsed += dt;
            MatchPlayer to = Player(match, m_passToIndex);
            if (to != null && to.Character != null) m_passToPos = HeadPosition(to.Character);
            if (m_passElapsed < m_passDuration) return;

            if (!CanHold(to))
            {
                PutOut();
                return;
            }
            m_phase = Phase.Burning;
            m_holderIndex = to.Index;
            m_passToIndex = -1;
            m_passTimer = 0.0f;
            m_timer = Mathf.Max(m_timer, m_minTimeOnPass);
            GimmickBurst.Play(m_passEffect, HeadPosition(to.Character));
            GimmickSound.Play(GimmickSound.Pop, 0.45f, 1.6f);
        }

        private static MatchPlayer Player(DarumaMatch match, int index)
        {
            if (match == null || index < 0) return null;
            foreach (var p in match.Players) if (p != null && p.Entry != null && p.Index == index) return p;
            return null;
        }

        private void PutOut()
        {
            m_phase = Phase.Cooldown;
            m_timer = m_cooldown;
            m_holderIndex = -1;
            m_lastGiverIndex = -1;
            m_passToIndex = -1;
        }

        // 時間切れ: 頭の上の花火が破裂し、積み木を上から1つずつ爆発させてはじき飛ばす。頭だけならやられる
        private void Explode(DarumaCharacter c)
        {
            GimmickBurst.Play(m_explosion, HeadPosition(c));
            GimmickSound.Play(GimmickSound.Pop, m_explosionVolume, 0.55f);
            GimmickSound.Play(GimmickSound.Launch, m_explosionVolume * 0.8f, 0.7f);
            BilliardFx.Shake(0.35f, 0.3f);
            PutOut();

            if (c.Stack.Count == 0)
            {
                c.Eliminate(EliminationCause.Other, Vector3.up, c.transform.position);
                return;
            }
            StartCoroutine(ExplodeBlocks(c));
        }

        // 積み木を上から1つずつ、少しずつ時間をずらして爆発させる(爆発のエフェクトを出し、持ち主なしの弾としてはじき飛ばす)
        private IEnumerator ExplodeBlocks(DarumaCharacter c)
        {
            int n = c.Stack.Count;
            float start = Random.Range(0.0f, 360.0f);
            for (int i = 0; i < n; i++)
            {
                if (c == null || c.IsEliminated || c.Stack.Count == 0) yield break;
                using (GimmickNet.Relay())   // ネット対戦用(suehara)
                {
                    TumikiBlock block = c.Stack.RemoveAt(c.Stack.Count - 1);
                    if (block != null)
                    {
                        // 積まれていた場所で爆発させてから、足元の高さで外へ飛ばす
                        GimmickBurst.Play(m_blockBurst, block.transform.position);
                        GimmickSound.Play(GimmickSound.Pop, m_explosionVolume * 0.6f, 0.8f + i * 0.08f);
                        Vector3 center = c.transform.position;
                        Vector3 dir = Quaternion.Euler(0.0f, start + i * 360.0f / n, 0.0f) * Vector3.forward;
                        block.transform.position = center + dir * (DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.1f)
                                                 + Vector3.up * (TumikiBlock.kHeight * 0.5f + 0.05f);
                        block.Launch(dir, m_scatterSpeed, null, c);
                    }
                }
                if (m_blockInterval > 0.0f) yield return new WaitForSeconds(m_blockInterval);
            }
        }

        private MatchPlayer Holder(DarumaMatch match)
        {
            if (match == null || m_holderIndex < 0) return null;
            foreach (var p in match.Players) if (p != null && p.Entry != null && p.Index == m_holderIndex) return p;
            return null;
        }

        private static Vector3 HeadPosition(DarumaCharacter c)
        {
            return c.transform.position + Vector3.up * (c.Stack.Height + 0.55f);
        }

        //========================================
        // 見た目(ホスト・クライアント共通)
        //========================================

        private void Update()
        {
            if (!GimmickClock.IsHeld) m_time += Time.deltaTime;
            var match = DarumaMatch.Current;
            MatchPlayer holder = m_phase == Phase.Burning ? Holder(match) : null;
            DarumaCharacter c = holder != null ? holder.Character : null;

            // 火種を持っている人の弾から火花を出す(当たると火種が移る弾が見分けられるように)
            UpdateShotSparks(c);

            if (m_phase == Phase.Passing)
            {
                UpdatePassingLook(match);
                return;
            }
            if (c == null)
            {
                SetVisible(false);
                return;
            }
            SetVisible(true);

            float progress = 1.0f - Mathf.Clamp01(m_timer / Mathf.Max(0.01f, m_fuseTime));
            bool warning = m_timer <= m_warningTime;
            // 爆発直前の進み具合(0: 予兆の始まり → 1: 爆発)
            float warn01 = warning ? 1.0f - Mathf.Clamp01(m_timer / Mathf.Max(0.01f, m_warningTime)) : 0.0f;
            float blink = warning ? GimmickPalette.Blink(m_time, Mathf.Lerp(3.0f, 10.0f, warn01)) : 0.0f;
            Vector3 head = HeadPosition(c);
            Vector3 feet = c.transform.position;

            // 爆発直前の拍子: だんだん速く打ち、打つたびに音・黄色の火花・床に広がる輪を出す(各端末で出す)
            m_pulse = Mathf.Max(0.0f, m_pulse - Time.deltaTime * 2.5f);
            if (warning)
            {
                m_beatTimer -= Time.deltaTime;
                if (m_beatTimer <= 0.0f)
                {
                    m_beatTimer = Mathf.Lerp(0.5f, 0.12f, warn01);
                    m_pulse = 1.0f;
                    GimmickSound.Play(GimmickSound.Pop, m_warningVolume, Mathf.Lerp(1.6f, 2.4f, warn01));
                    GimmickBurst.Play(m_warningSparks, head, Quaternion.identity, Mathf.Lerp(1.0f, 1.6f, warn01));
                }
            }
            else
            {
                m_beatTimer = 0.0f;
            }

            // 頭の上で回る花火(火花の輪)。残りが減るほど大きく速く。爆発直前はさらに大きくなり、拍子に合わせてふくらむ
            m_firework.position = head;
            float size = Mathf.Lerp(0.35f, 0.6f, progress) * Mathf.Lerp(1.0f, 1.9f, warn01) * (1.0f + m_pulse * 0.35f);
            m_firework.rotation = Quaternion.Euler(0.0f, m_time * Mathf.Lerp(540.0f, 1440.0f, progress) * (warning ? 1.6f : 1.0f), 0.0f);
            m_wheel.transform.localScale = new Vector3(size, 1.0f, size);
            m_wheelGlow.transform.localScale = new Vector3(size * (warning ? 2.1f : 1.6f), 1.0f, size * (warning ? 2.1f : 1.6f));
            Color spark = Color.Lerp(m_sparkColorA, m_warningColor, blink);
            EffectMeshes.SetColor(m_wheel, spark);
            EffectMeshes.SetColor(m_wheelGlow, GimmickPalette.WithAlpha(Color.Lerp(m_sparkColorB, m_warningColor, blink), warning ? 0.45f : 0.35f));

            // 足元の輪(だれが持っているか、縮めた画面でも分かるように)
            m_ring.transform.position = new Vector3(feet.x, feet.y + 0.03f, feet.z);
            float r = (DarumaCharacter.kBodyRadius + 0.55f) * (warning ? 1.3f : 1.0f);
            m_ring.transform.localScale = new Vector3(r, 1.0f, r);
            EffectMeshes.SetColor(m_ring, Color.Lerp(m_ringColor, GimmickPalette.WithAlpha(m_warningColor, 0.95f), blink));

            // 爆発直前の光の柱(足元から上へ。遠くからでも、だれが爆発するか分かるように)と、拍子ごとに床に広がる輪
            m_beam.enabled = warning;
            m_pulseRing.enabled = warning;
            if (warning)
            {
                m_beam.transform.position = feet + Vector3.up * 0.02f;
                float br = 0.8f + 0.25f * m_pulse;
                m_beam.transform.localScale = new Vector3(br, m_warningBeamHeight, br);
                // 持っている人の姿が隠れないよう、柱は薄めにする(点滅と拍子で目立たせる)
                EffectMeshes.SetColor(m_beam, GimmickPalette.WithAlpha(m_warningColor, Mathf.Lerp(0.12f, 0.4f, blink) * Mathf.Lerp(0.7f, 1.0f, warn01)));
                float pr = Mathf.Lerp(0.8f, m_warningPulseRadius, 1.0f - m_pulse);
                m_pulseRing.transform.position = new Vector3(feet.x, feet.y + 0.04f, feet.z);
                m_pulseRing.transform.localScale = new Vector3(pr, 1.0f, pr);
                EffectMeshes.SetColor(m_pulseRing, GimmickPalette.WithAlpha(m_warningColor, m_pulse * 0.9f));
            }

            // 火花(各端末で出す。ホストからは送らない)
            EmitSparks(head, size, progress);

            // シュルシュルの音(残りが減るほど速く)
            if (m_fuseSound != null)
            {
                GimmickSound.SetLoopVolume(m_fuseSound, m_fuseVolume);
                m_fuseSound.pitch = Mathf.Lerp(0.9f, 1.8f, progress);
            }
        }

        // 火種が飛んでいる途中の見た目: 花火が弧を描いて、当てた人の頭から当てられた人の頭へ飛ぶ(火花の尾を引く)
        private void UpdatePassingLook(DarumaMatch match)
        {
            MatchPlayer from = Player(match, m_holderIndex >= 0 ? m_holderIndex : m_lastGiverIndex);
            MatchPlayer to = Player(match, m_passToIndex);
            if (from != null && from.Character != null && m_passElapsed <= 0.0001f) m_passFromPos = HeadPosition(from.Character);
            if (to != null && to.Character != null) m_passToPos = HeadPosition(to.Character);

            float t = Mathf.Clamp01(m_passElapsed / Mathf.Max(0.01f, m_passDuration));
            float ease = t * t * (3.0f - 2.0f * t);
            Vector3 p = Vector3.Lerp(m_passFromPos, m_passToPos, ease) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * m_passArcHeight);

            if (m_firework != null) m_firework.gameObject.SetActive(true);
            if (m_ring != null) m_ring.enabled = false;
            if (m_beam != null) m_beam.enabled = false;
            if (m_pulseRing != null) m_pulseRing.enabled = false;
            m_firework.position = p;
            m_firework.rotation = Quaternion.Euler(0.0f, m_time * 1440.0f, 0.0f);
            const float size = 0.5f;
            m_wheel.transform.localScale = new Vector3(size, 1.0f, size);
            m_wheelGlow.transform.localScale = new Vector3(size * 1.8f, 1.0f, size * 1.8f);
            EffectMeshes.SetColor(m_wheel, m_sparkColorA);
            EffectMeshes.SetColor(m_wheelGlow, GimmickPalette.WithAlpha(m_sparkColorB, 0.45f));
            EmitSparks(p, size, 0.8f);
            if (m_fuseSound != null)
            {
                GimmickSound.SetLoopVolume(m_fuseSound, m_fuseVolume);
                m_fuseSound.pitch = 1.6f;
            }
        }

        // 火種を持っている人の、攻撃判定のある弾(当たると火種が移る弾)から火花を出す。各端末で出す
        private void UpdateShotSparks(DarumaCharacter holder)
        {
            if (!GimmickClock.IsNetClient) FindHolderShots(holder);
            // 弾の上で回る小さな花火の輪
            for (int i = 0; i < kMaxShots; i++)
            {
                var w = m_shotWheels[i];
                if (w == null) continue;
                w.enabled = m_shotActive[i];
                if (!m_shotActive[i]) continue;
                w.transform.position = m_shots[i] + Vector3.up * 0.45f;
                w.transform.rotation = Quaternion.Euler(0.0f, m_time * 1440.0f + i * 40.0f, 0.0f);
                float s = m_shotWheelSize * (1.0f + 0.15f * Mathf.Sin(m_time * 40.0f + i));
                w.transform.localScale = new Vector3(s, 1.0f, s);
                EffectMeshes.SetColor(w, m_sparkColorA);
            }
            m_shotSparkTimer -= Time.deltaTime;
            if (m_shotSparkTimer > 0.0f) return;
            m_shotSparkTimer = m_shotSparkInterval;
            for (int i = 0; i < kMaxShots; i++)
            {
                if (m_shotActive[i]) GimmickBurst.Play(m_shotSparks, m_shots[i]);
            }
        }

        // ホスト: 火種を持っている人が持ち主の、飛んでいる弾を探す(連鎖で飛んだ積み木も、持ち主が同じなら含む)
        private void FindHolderShots(DarumaCharacter holder)
        {
            int n = 0;
            if (holder != null && m_phase == Phase.Burning)
            {
                foreach (var b in TumikiBlock.All)
                {
                    if (n >= kMaxShots) break;
                    if (b == null || !b.IsHarmful || !ReferenceEquals(b.Owner, holder)) continue;
                    m_shots[n] = b.transform.position;
                    m_shotActive[n] = true;
                    n++;
                }
            }
            for (int i = n; i < kMaxShots; i++) m_shotActive[i] = false;
        }

        // 自分の端末だけで出す(ネット対戦でホストから送ると多すぎるので、Relay の範囲の外で出す)
        private void EmitSparks(Vector3 head, float size, float progress)
        {
            m_emitTimer -= Time.deltaTime;
            if (m_emitTimer > 0.0f) return;
            m_emitTimer = Mathf.Lerp(0.09f, 0.03f, progress);
            var s = m_sparks.Clone();
            s.ShapeSize = new Vector3(size, 0, 0);
            s.Count = Mathf.RoundToInt(Mathf.Lerp(4, 9, progress));
            GimmickBurst.Play(s, head, Quaternion.identity, Mathf.Lerp(1.0f, 1.4f, progress));
        }

        private void SetVisible(bool visible)
        {
            if (m_firework != null) m_firework.gameObject.SetActive(visible);
            if (m_ring != null) m_ring.enabled = visible;
            if (!visible)
            {
                if (m_beam != null) m_beam.enabled = false;
                if (m_pulseRing != null) m_pulseRing.enabled = false;
                m_pulse = 0.0f;
                GimmickSound.SetLoopVolume(m_fuseSound, 0.0f);
            }
        }

        // シュルシュル(導火線の燃える音。ループ)
        static AudioClip FuseClip
        {
            get
            {
                if (s_fuseClip != null) return s_fuseClip;
                const int rate = 44100;
                var b = new float[rate];
                var rng = new System.Random(20261118);
                float lp = 0.0f;
                for (int i = 0; i < b.Length; i++)
                {
                    float t = (float)i / rate;
                    float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp += (n - lp) * 0.35f;
                    // 1秒に8回の小さなはぜる音(ループの継ぎ目がずれないよう整数回)
                    float crackle = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(t * 8.0f * Mathf.PI * 2.0f), 12.0f);
                    b[i] = (n - lp) * (0.5f + 0.5f * crackle) * 0.6f;
                }
                s_fuseClip = AudioClip.Create("hanabi_fuse", b.Length, 1, rate, false);
                s_fuseClip.SetData(b, 0);
                return s_fuseClip;
            }
        }

        //========================================
        // ネット対戦用(suehara): 状態をホストから受け取る(積み木・体はネットワークに乗っているので別に届く)
        //========================================

        public void WriteNetState(GimmickNetState s)
        {
            s.AddInt((int)m_phase);
            s.AddInt(m_holderIndex);
            s.Add(m_timer);
            s.AddInt(m_lastGiverIndex);
            s.AddInt(m_passToIndex);
            s.Add(m_passElapsed);
            s.AddVec3(m_passFromPos);
            // 火種を持っている人の弾(火花を出す所)。数をそろえるため、いつも同じ数を書く
            for (int i = 0; i < kMaxShots; i++)
            {
                s.AddBool(m_shotActive[i]);
                s.AddVec3(m_shots[i]);
            }
        }

        public void ReadNetState(GimmickNetState s)
        {
            var phase = (Phase)s.Int();
            int holder = s.Int();
            float timer = s.Float();
            int giver = s.Int();
            int passTo = s.Int();
            float passElapsed = s.Float();
            Vector3 passFrom = s.Vec3();
            var active = new bool[kMaxShots];
            var shots = new Vector3[kMaxShots];
            for (int i = 0; i < kMaxShots; i++)
            {
                active[i] = s.Bool();
                shots[i] = s.Vec3();
            }
            if (!s.Ok) return;
            m_phase = phase;
            m_holderIndex = holder;
            m_timer = timer;
            m_lastGiverIndex = giver;
            m_passToIndex = passTo;
            m_passElapsed = passElapsed;
            m_passFromPos = passFrom;
            for (int i = 0; i < kMaxShots; i++)
            {
                m_shotActive[i] = active[i];
                m_shots[i] = shots[i];
            }
        }
    }
}
