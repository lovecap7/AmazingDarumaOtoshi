using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // サンドバッグ(Stage24「部室のサンドバッグ」)。ステージの中央に立つ大きなサンドバッグ
    //  ・ハンマーの薙ぎ払いが当たるたびに、殴った人の反対側へ積み木が1個出る(攻撃判定なし。間隔と盤面の上限あり)
    //  ・当たるたびに蓄積が1増え、しばらく殴られないと少しずつ減る。増えるほど速く点滅し、ふくらみ、揺れが大きくなる
    //  ・蓄積が上限に達すると、床に8方向の白い線(予告)を出してから破裂し、攻撃判定のある積み木を8方向へ撃ち出す(持ち主なし)
    //  ・破裂したあとは一定時間で元の場所に戻る(戻る場所に予告の円を出し、上にいるものは外へ押し出す)
    // ハンマーはキャラクターと積み木にしか当たらない作りなので、ハンマーの振りの角度と届く距離(DarumaHammer の公開している値)から
    // サンドバッグを通り過ぎたかをこちらで調べる(DarumaHammer は変えない)。幽霊のハンマー・飛んできた積み木は数えない
    // サンドバッグの当たり判定(この GameObject のコライダー)は壁と同じで、飛んでいる積み木は跳ね返る(ReflectWall)
    // 開始のカウントダウン中は止まる。ネット対戦ではホストが判定し、クライアントは状態を受け取って見た目だけ合わせる
    public class SandbagGimmick : MonoBehaviour, INetGimmickState
    {
        public enum Phase { Idle, Charging, Burst, Returning }

        [Header("形")]
        [SerializeField, Min(0.2f)] float m_radius = 1.5f;
        // 見た目(揺らす・ふくらませる。当たり判定は付けない)
        [SerializeField] Transform m_visual;
        // 点滅させる見た目(本体の帯など)
        [SerializeField] Renderer[] m_blinkRenderers;
        [SerializeField] Collider m_collider;

        [Header("殴る")]
        [SerializeField] TumikiBlock m_tumikiPrefab;
        [Tooltip("盤面の上限を調べる TumikiSpawner(色の決め方もここから借りる)")]
        [SerializeField] TumikiSpawner m_spawner;
        // 積み木を出す間隔の下限(秒)
        [SerializeField, Min(0.0f)] float m_dropInterval = 0.4f;
        // 出した積み木を押し出す速さ(攻撃判定が付かない遅さ)
        [SerializeField] float m_dropSpeed = 2.5f;
        // 1発で増える蓄積・殴られないと減る間隔
        [SerializeField, Min(1)] int m_hitGain = 1;
        // 攻撃判定のある積み木(飛んでいる積み木)が当たったときに増える蓄積(0 なら数えない)
        [SerializeField, Min(0)] int m_blockHitGain = 1;
        // この速さ(m/秒)以上でぶつかった飛んでいる積み木も、攻撃判定があるとみなす(跳ね返りの処理が先に動いて、判定が消えた後でも数えるため)
        [SerializeField] float m_blockHitSpeed = 6.0f;
        // 同じ積み木を続けて数えない時間(秒)
        [SerializeField, Min(0.0f)] float m_blockHitCooldown = 0.4f;
        [SerializeField, Min(0.1f)] float m_decayInterval = 1.5f;

        [Header("破裂")]
        [SerializeField, Min(1)] int m_burstThreshold = 12;
        [SerializeField, Min(0.0f)] float m_chargeTime = 0.8f;
        [SerializeField, Min(1)] int m_burstCount = 8;
        [SerializeField] float m_burstSpeed = 14.0f;
        // 破裂のあと元に戻るまで(秒)と、戻る前に予告の円を出す時間
        [SerializeField, Min(0.0f)] float m_returnTime = 10.0f;
        [SerializeField, Min(0.0f)] float m_returnWarningTime = 1.5f;
        // 戻るときに上にいるものを押し出す速さ
        [SerializeField] float m_pushOutSpeed = 7.0f;

        [Header("見た目")]
        [SerializeField] Color m_baseColor = new Color(0.93f, 0.91f, 0.86f, 1.0f);
        [SerializeField] Color m_warningColor = GimmickPalette.Warning;
        // 点滅の速さ(蓄積0 → 上限。1秒あたり)
        [SerializeField] Vector2 m_blinkRate = new Vector2(1.0f, 9.0f);
        // ふくらみ(上限での倍率)と揺れ(上限での幅 m)
        [SerializeField] float m_maxSwell = 1.18f;
        [SerializeField] float m_maxShake = 0.12f;
        // 殴られたときのへこみ
        [SerializeField] float m_hitSquash = 0.12f;
        [SerializeField] Color m_guideColor = new Color(1.0f, 1.0f, 1.0f, 0.75f);
        [SerializeField] float m_guideLength = 9.0f;

        [Header("エフェクト")]
        [SerializeField] GimmickBurstSettings m_hitEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.95f, 0.93f, 0.88f, 0.9f), ColorB = new Color(0.8f, 0.76f, 0.68f, 0.8f),
            Count = 12, Speed = new Vector2(1.0f, 3.0f), Size = new Vector2(0.08f, 0.18f), Lifetime = new Vector2(0.25f, 0.5f),
            Gravity = 0.5f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.3f, 0, 0),
        };
        [SerializeField] GimmickBurstSettings m_burstEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.95f, 0.93f, 0.88f, 0.95f), ColorB = new Color(0.75f, 0.7f, 0.62f, 0.85f),
            Count = 60, Speed = new Vector2(3.0f, 8.0f), Size = new Vector2(0.12f, 0.3f), Lifetime = new Vector2(0.4f, 0.8f),
            Gravity = 0.6f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(1.2f, 0, 0),
            Shockwave = true, ShockwaveColor = new Color(1.0f, 1.0f, 0.95f, 0.7f), ShockwaveRadius = 4.0f, ShockwaveTime = 0.35f,
        };
        [SerializeField] GimmickBurstSettings m_returnEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.9f, 0.88f, 0.82f, 0.8f), ColorB = new Color(0.7f, 0.66f, 0.6f, 0.7f),
            Count = 24, Speed = new Vector2(1.0f, 2.5f), Size = new Vector2(0.1f, 0.22f), Lifetime = new Vector2(0.3f, 0.6f),
            Shape = GimmickBurstShape.Ring, ShapeSize = new Vector3(1.5f, 0, 0),
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.95f, 0.85f, 0.5f), ShockwaveRadius = 2.5f, ShockwaveTime = 0.3f,
        };
        [SerializeField, Range(0.0f, 1.0f)] float m_hitVolume = 0.4f;
        [SerializeField, Range(0.0f, 1.0f)] float m_burstVolume = 0.7f;

        static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int s_emissionId = Shader.PropertyToID("_EmissionColor");

        // ハンマーごとの、前のステップの振りの角度と、今の一振りで当たったか
        class Swing
        {
            public float PrevYaw;
            public float PrevTime;
            public bool Hit;
        }

        readonly Dictionary<DarumaHammer, Swing> m_swings = new Dictionary<DarumaHammer, Swing>();
        readonly List<IGimmickAffectable> m_targets = new List<IGimmickAffectable>();
        readonly Collider[] m_overlaps = new Collider[32];
        Phase m_phase = Phase.Idle;
        int m_charge;
        float m_decayTimer;
        float m_dropTimer;
        float m_phaseTimer;
        float m_time;
        float m_squash;
        Vector3 m_visualScale = Vector3.one;
        MaterialPropertyBlock m_mpb;
        MeshRenderer[] m_guides;
        MeshRenderer m_returnFill, m_returnRing;
        DarumaMatch m_match;

        public Phase CurrentPhase => m_phase;
        public int Charge => m_charge;
        public int BurstThreshold => m_burstThreshold;
        public float Radius => m_radius;

        private void Awake()
        {
            m_mpb = new MaterialPropertyBlock();
            if (m_collider == null) m_collider = GetComponent<Collider>();
            if (m_visual != null) m_visualScale = m_visual.localScale;
            m_guides = new MeshRenderer[Mathf.Max(1, m_burstCount)];
            for (int i = 0; i < m_guides.Length; i++)
            {
                m_guides[i] = EffectMeshes.Create($"BurstGuide{i}", transform, EffectMeshes.Disc, false);
                m_guides[i].enabled = false;
            }
            m_returnFill = EffectMeshes.Create("ReturnFill", transform, EffectMeshes.Disc, false);
            m_returnRing = EffectMeshes.Create("ReturnRing", transform, EffectMeshes.Ring, true);
            m_returnFill.enabled = m_returnRing.enabled = false;
        }

        private void OnEnable()
        {
            m_match = DarumaMatch.Current;
            if (m_match != null) m_match.Started += ResetBag;
            ResetBag();
        }

        private void OnDisable()
        {
            if (m_match != null) m_match.Started -= ResetBag;
            m_match = null;
        }

        public void ResetBag()
        {
            m_phase = Phase.Idle;
            m_charge = 0;
            m_decayTimer = m_decayInterval;
            m_dropTimer = 0.0f;
            m_phaseTimer = 0.0f;
            m_squash = 0.0f;
            m_swings.Clear();
            SetPresent(true);
        }

        private void FixedUpdate()
        {
            if (GimmickClock.IsNetClient || GimmickClock.IsHeld) return;
            using var relay = GimmickNet.Relay();   // ネット対戦用(suehara)
            float dt = Time.fixedDeltaTime;
            m_dropTimer -= dt;

            switch (m_phase)
            {
                case Phase.Idle:
                    DetectHammers();
                    // しばらく殴られないと少しずつ減る
                    if (m_charge > 0)
                    {
                        m_decayTimer -= dt;
                        if (m_decayTimer <= 0.0f)
                        {
                            m_charge--;
                            m_decayTimer = m_decayInterval;
                        }
                    }
                    break;
                case Phase.Charging:
                    m_phaseTimer += dt;
                    if (m_phaseTimer >= m_chargeTime) Burst();
                    break;
                case Phase.Burst:
                    m_phaseTimer += dt;
                    if (m_phaseTimer >= m_returnTime) Return();
                    break;
            }
        }

        private void Update()
        {
            float dt = GimmickClock.IsHeld ? 0.0f : Time.deltaTime;
            m_time += dt;
            m_squash = Mathf.MoveTowards(m_squash, 0.0f, Time.deltaTime * 4.0f);
            UpdateLook();
        }

        //========================================
        // ハンマーの当たり
        //========================================

        private void DetectHammers()
        {
            var match = DarumaMatch.Current;
            if (match == null) return;
            foreach (var p in match.Players)
            {
                if (p == null || p.Character == null || !p.Character.CanBeAffected) continue;
                DarumaHammer hammer = p.Character.Hammer;
                if (hammer == null) continue;
                if (!m_swings.TryGetValue(hammer, out Swing swing))
                {
                    swing = new Swing();
                    m_swings[hammer] = swing;
                }
                if (!hammer.IsSweeping)
                {
                    swing.Hit = false;
                    swing.PrevTime = 0.0f;
                    swing.PrevYaw = hammer.SweepStartYaw;
                    continue;
                }
                // 新しい一振り(振り始めの時間に戻った)
                if (hammer.ActionTime < swing.PrevTime)
                {
                    swing.Hit = false;
                    swing.PrevYaw = hammer.SweepStartYaw;
                }
                float yaw = Mathf.Lerp(hammer.SweepStartYaw, hammer.SweepEndYaw, Mathf.Clamp01(hammer.ActionTime / Mathf.Max(0.01f, hammer.SweepDuration)));
                float prevYaw = swing.PrevYaw;
                swing.PrevYaw = yaw;
                swing.PrevTime = hammer.ActionTime;
                if (swing.Hit || !Crosses(p.Character, yaw, prevYaw)) continue;
                swing.Hit = true;
                OnHit(p.Character);
            }
        }

        // ハンマーがこのステップで、サンドバッグの見えている幅を通り過ぎたか
        private bool Crosses(DarumaCharacter attacker, float yaw, float prevYaw)
        {
            Transform root = attacker.transform;
            Vector3 local = root.InverseTransformPoint(new Vector3(transform.position.x, root.position.y, transform.position.z));
            float dist = new Vector2(local.x, local.z).magnitude;
            float reach = attacker.Hammer.CharacterHitDistance - DarumaCharacter.kBodyRadius;
            if (dist - m_radius > reach) return false;
            float bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            // サンドバッグの幅の分だけ角度に余裕を持たせる(すぐそばなら正面のどこを振っても当たる)
            float pad = dist <= m_radius ? 180.0f : Mathf.Asin(Mathf.Clamp01(m_radius / dist)) * Mathf.Rad2Deg + 5.0f;
            float lo = Mathf.Min(yaw, prevYaw), hi = Mathf.Max(yaw, prevYaw);
            return bearing >= lo - pad && bearing <= hi + pad;
        }

        private void OnHit(DarumaCharacter attacker)
        {
            m_squash = 1.0f;
            Vector3 away = transform.position - attacker.transform.position;
            away.y = 0.0f;
            away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
            GimmickBurst.Play(m_hitEffect, transform.position - away * m_radius + Vector3.up * 0.6f);
            GimmickSound.Play(GimmickSound.Pop, m_hitVolume, Random.Range(0.6f, 0.75f));

            if (m_dropTimer <= 0.0f && DropBlock(away)) m_dropTimer = m_dropInterval;

            m_charge += m_hitGain;
            m_decayTimer = m_decayInterval;
            if (m_charge >= m_burstThreshold) BeginCharge();
        }

        // 攻撃判定のある積み木が当たった: 蓄積が増える(積み木は出さない。積み木は壁と同じように跳ね返る)
        readonly Dictionary<TumikiBlock, float> m_blockHits = new Dictionary<TumikiBlock, float>();

        private void OnCollisionEnter(Collision collision)
        {
            if (m_blockHitGain <= 0 || m_phase != Phase.Idle || GimmickClock.IsNetClient || GimmickClock.IsHeld) return;
            var block = collision.rigidbody != null ? collision.rigidbody.GetComponent<TumikiBlock>() : collision.collider.GetComponentInParent<TumikiBlock>();
            if (block == null || block.CurrentState != TumikiBlock.State.Flying) return;
            if (!block.IsHarmful && collision.relativeVelocity.magnitude < m_blockHitSpeed) return;
            if (m_blockHits.TryGetValue(block, out float last) && Time.time - last < m_blockHitCooldown) return;
            m_blockHits[block] = Time.time;
            if (m_blockHits.Count > 32) m_blockHits.Clear();

            using var relay = GimmickNet.Relay();   // ネット対戦用(suehara)
            m_squash = 1.0f;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : block.transform.position;
            GimmickBurst.Play(m_hitEffect, point);
            GimmickSound.Play(GimmickSound.Pop, m_hitVolume, Random.Range(0.6f, 0.75f));
            m_charge += m_blockHitGain;
            m_decayTimer = m_decayInterval;
            if (m_charge >= m_burstThreshold) BeginCharge();
        }

        // 殴った人の反対側へ積み木を1個出す(攻撃判定なし)。盤面の上限なら出さない
        private bool DropBlock(Vector3 dir)
        {
            if (m_tumikiPrefab == null) return false;
            if (m_spawner != null && TumikiSpawner.CountBlocksOnBoard() >= m_spawner.MaxBlocksOnBoard) return false;
            Vector3 pos = transform.position + dir * (m_radius + TumikiBlock.kRadius + 0.15f) + Vector3.up * (TumikiBlock.kHeight * 0.5f + 0.3f);
            TumikiBlock block = Instantiate(m_tumikiPrefab, pos, Quaternion.LookRotation(dir));
            block.SetColor(PickColor());
            if (block.Rigidbody != null) block.Rigidbody.linearVelocity = dir * m_dropSpeed + Vector3.up * 3.0f;
            return true;
        }

        private TumikiColor PickColor()
        {
            var pattern = m_spawner != null ? m_spawner.NextPattern : null;
            if (pattern != null)
            {
                var colors = pattern.CreateColors(1);
                if (colors != null && colors.Count > 0) return colors[0];
            }
            return (TumikiColor)Random.Range(0, 4);
        }

        //========================================
        // 破裂と復活
        //========================================

        private void BeginCharge()
        {
            m_phase = Phase.Charging;
            m_phaseTimer = 0.0f;
            AudioClip clip = GimmickSound.Charge;
            GimmickSound.Play(clip, 0.35f, clip.length / Mathf.Max(0.1f, m_chargeTime));
        }

        private void Burst()
        {
            m_phase = Phase.Burst;
            m_phaseTimer = 0.0f;
            m_charge = 0;
            SetPresent(false);
            Vector3 center = transform.position;
            GimmickBurst.Play(m_burstEffect, center + Vector3.up * 1.0f);
            GimmickSound.Play(GimmickSound.Launch, m_burstVolume, 0.6f);
            BilliardFx.Shake(0.35f, 0.3f);

            if (m_tumikiPrefab == null) return;
            for (int i = 0; i < m_burstCount; i++)
            {
                Vector3 dir = BurstDirection(i);
                Vector3 pos = center + dir * (m_radius * 0.6f) + Vector3.up * (TumikiBlock.kHeight * 0.5f + 0.1f);
                TumikiBlock block = Instantiate(m_tumikiPrefab, pos, Quaternion.LookRotation(dir));
                block.SetColor(PickColor());
                block.Launch(dir, m_burstSpeed, null);
            }
        }

        private Vector3 BurstDirection(int i)
        {
            float a = transform.eulerAngles.y + i * 360.0f / Mathf.Max(1, m_burstCount);
            return Quaternion.Euler(0.0f, a, 0.0f) * Vector3.forward;
        }

        private void Return()
        {
            m_phase = Phase.Idle;
            m_phaseTimer = 0.0f;
            m_charge = 0;
            m_decayTimer = m_decayInterval;
            PushOut();
            SetPresent(true);
            m_squash = 1.0f;
            GimmickBurst.Play(m_returnEffect, transform.position + Vector3.up * 0.1f);
            GimmickSound.Play(GimmickSound.Pop, m_hitVolume, 0.5f);
        }

        // 戻る場所にいるもの(プレイヤー・積み木)を外へ押し出す
        private void PushOut()
        {
            Vector3 c = transform.position;
            int n = Physics.OverlapCapsuleNonAlloc(c + Vector3.up * 0.3f, c + Vector3.up * 2.5f, m_radius + 0.3f, m_overlaps, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            m_targets.Clear();
            for (int i = 0; i < n; i++)
            {
                var target = GimmickUtil.Find(m_overlaps[i]);
                if (target == null || m_targets.Contains(target)) continue;
                m_targets.Add(target);
                Vector3 away = target.Rigidbody.position - c;
                away.y = 0.0f;
                away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
                if (GimmickUtil.MovesWithSolidPlatform(target)) target.Rigidbody.position = c + away * (m_radius + TumikiBlock.kRadius + 0.2f) + Vector3.up * TumikiBlock.kHeight * 0.5f;
                target.AddKnockback(away * m_pushOutSpeed + Vector3.up * 3.0f);
            }
        }

        private void SetPresent(bool present)
        {
            if (m_collider != null) m_collider.enabled = present;
            if (m_visual != null) m_visual.gameObject.SetActive(present);
        }

        private void UpdateLook()
        {
            float charge01 = m_burstThreshold > 0 ? Mathf.Clamp01((float)m_charge / m_burstThreshold) : 0.0f;
            bool charging = m_phase == Phase.Charging;
            if (charging) charge01 = 1.0f;

            // 本体: ふくらみ・揺れ・殴られたときのへこみ
            if (m_visual != null && m_visual.gameObject.activeSelf)
            {
                float swell = Mathf.Lerp(1.0f, m_maxSwell, charge01 * charge01);
                if (charging) swell += 0.04f * Mathf.Sin(m_time * 40.0f);
                float squash = m_hitSquash * Mathf.Sin(m_squash * Mathf.PI);
                m_visual.localScale = new Vector3(m_visualScale.x * (swell + squash), m_visualScale.y * (1.0f - squash * 0.5f), m_visualScale.z * (swell + squash));
                float shake = m_maxShake * charge01 * charge01;
                float t = m_time * (charging ? 45.0f : 25.0f);
                m_visual.localPosition = new Vector3(Mathf.PerlinNoise(t, 0.2f) - 0.5f, 0.0f, Mathf.PerlinNoise(0.8f, t) - 0.5f) * (shake * 2.0f);
            }

            // 点滅: 蓄積が増えるほど速く(予兆の黄色)
            Color c = m_baseColor;
            if (m_charge > 0 || charging)
            {
                float rate = Mathf.Lerp(m_blinkRate.x, m_blinkRate.y, charge01);
                float blink = GimmickPalette.Blink(m_time, rate) * Mathf.Lerp(0.25f, 1.0f, charge01);
                c = Color.Lerp(m_baseColor, m_warningColor, blink);
            }
            if (m_blinkRenderers != null)
            {
                foreach (var r in m_blinkRenderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(m_mpb);
                    m_mpb.SetColor(s_baseColorId, c);
                    m_mpb.SetColor(s_emissionId, c == m_baseColor ? Color.black : (c - m_baseColor) * 0.6f);
                    r.SetPropertyBlock(m_mpb);
                }
            }

            // 破裂の予告: 8方向の白い線
            float guideFade = charging ? Mathf.Clamp01(m_phaseTimer / Mathf.Max(0.01f, m_chargeTime * 0.3f)) : 0.0f;
            for (int i = 0; i < m_guides.Length; i++)
            {
                var g = m_guides[i];
                g.enabled = charging;
                if (!charging) continue;
                Vector3 dir = BurstDirection(i);
                g.transform.position = transform.position + dir * (m_radius + m_guideLength * 0.5f) + Vector3.up * 0.03f;
                g.transform.rotation = Quaternion.LookRotation(dir);
                g.transform.localScale = new Vector3(0.12f, 1.0f, m_guideLength * 0.5f);
                float blink = GimmickPalette.Blink(m_time, 10.0f);
                EffectMeshes.SetColor(g, GimmickPalette.WithAlpha(m_guideColor, m_guideColor.a * guideFade * Mathf.Lerp(0.5f, 1.0f, blink)));
            }

            // 復活の予告: 戻る場所に円
            float left = m_returnTime - m_phaseTimer;
            bool returning = m_phase == Phase.Burst && left <= m_returnWarningTime;
            m_returnFill.enabled = m_returnRing.enabled = returning;
            if (returning)
            {
                float t = 1.0f - Mathf.Clamp01(left / Mathf.Max(0.01f, m_returnWarningTime));
                Vector3 p = transform.position + Vector3.up * 0.02f;
                m_returnFill.transform.position = p;
                m_returnRing.transform.position = p + Vector3.up * 0.005f;
                m_returnFill.transform.localScale = new Vector3(m_radius * t, 1.0f, m_radius * t);
                m_returnRing.transform.localScale = new Vector3(m_radius + 0.3f, 1.0f, m_radius + 0.3f);
                float blink = GimmickPalette.Blink(m_time, Mathf.Lerp(3.0f, 10.0f, t));
                EffectMeshes.SetColor(m_returnFill, GimmickPalette.WithAlpha(m_warningColor, 0.35f));
                EffectMeshes.SetColor(m_returnRing, GimmickPalette.WithAlpha(m_warningColor, Mathf.Lerp(0.5f, 1.0f, blink)));
            }
        }

        //========================================
        // ネット対戦用(suehara): 状態をホストから受け取る(積み木はネットワークに乗っているので別に届く)
        //========================================

        public void WriteNetState(GimmickNetState s)
        {
            s.AddInt((int)m_phase);
            s.AddInt(m_charge);
            s.Add(m_phaseTimer);
            s.Add(m_squash);
        }

        public void ReadNetState(GimmickNetState s)
        {
            var phase = (Phase)s.Int();
            int charge = s.Int();
            float timer = s.Float();
            float squash = s.Float();
            if (!s.Ok) return;
            if (phase != m_phase) SetPresent(phase != Phase.Burst);
            m_phase = phase;
            m_charge = charge;
            m_phaseTimer = timer;
            m_squash = Mathf.Max(m_squash, squash);
        }
    }
}
