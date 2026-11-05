using System.Collections.Generic;
using Nakahira;
using Nakagawa;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yurube
{
    // いわだるの必殺技「いわだる崩し」。
    // 発動時の積み木数を固定し、3秒のカウントダウン中に攻撃予告円を表示。
    // その後、積み木をすべて消費して巨大隕石へ変化し、上空から落下して着弾地点の
    // 敵・積み木をまとめて破壊する。
    [RequireComponent(typeof(DarumaCharacter))]
    public class IwadaruSpecial : MonoBehaviour, ISpecialAbility
    {
        [Header("必殺技")]
        [Tooltip("発動前のカウントダウン(秒)")]
        [SerializeField, Min(0.1f)] float m_countdown = 3.0f;
        [Tooltip("性能計算に使う最大積み木数")]
        [SerializeField, Min(1)] int m_maxBlocks = 10;
        [Tooltip("積み木1個のときの攻撃半径(m)")]
        [SerializeField, Min(0.1f)] float m_minRadius = 2.5f;
        [Tooltip("最大積み木数のときの攻撃半径(m)")]
        [SerializeField, Min(0.1f)] float m_maxRadius = 8.0f;
        [Tooltip("攻撃半径の全体倍率(予告円・実判定の両方に適用)")]
        [SerializeField, Min(0.1f)] float m_attackRadiusScale = 1.5f;
        [Tooltip("積み木1個のときの上昇高度(m)")]
        [SerializeField, Min(0.1f)] float m_minHeight = 6.0f;
        [Tooltip("最大積み木数のときの上昇高度(m)")]
        [SerializeField, Min(0.1f)] float m_maxHeight = 18.0f;
        [Tooltip("積み木1個のときの上昇時間(秒)")]
        [SerializeField, Min(0.05f)] float m_minRiseTime = 0.45f;
        [Tooltip("最大積み木数のときの上昇時間(秒)")]
        [SerializeField, Min(0.05f)] float m_maxRiseTime = 0.80f;
        [Tooltip("積み木1個のときの落下時間(秒)")]
        [SerializeField, Min(0.05f)] float m_minFallTime = 0.55f;
        [Tooltip("最大積み木数のときの落下時間(秒)")]
        [SerializeField, Min(0.05f)] float m_maxFallTime = 1.40f;
        [Tooltip("着弾後の硬直時間(秒)")]
        [SerializeField, Min(0f)] float m_impactRecovery = 0.45f;
        [Tooltip("積み木1個のときの隕石の大きさ")]
        [SerializeField, Min(0.1f)] float m_minMeteorScale = 1.0f;
        [Tooltip("最大積み木数のときの隕石の大きさ")]
        [SerializeField, Min(0.1f)] float m_maxMeteorScale = 1.8f;
        [Tooltip("着弾時に飛び散る岩の最小数")]
        [SerializeField, Min(1)] int m_minDebris = 12;
        [Tooltip("着弾時に飛び散る岩の最大数")]
        [SerializeField, Min(1)] int m_maxDebris = 40;
        [Tooltip("着弾時の画面揺れの最小振幅")]
        [SerializeField, Min(0.0f)] float m_minShakeAmplitude = 0.35f;
        [Tooltip("着弾時の画面揺れの最大振幅")]
        [SerializeField, Min(0.0f)] float m_maxShakeAmplitude = 1.1f;
        [Tooltip("着弾フラッシュの最小強さ")]
        [SerializeField, Range(0.0f, 1.0f)] float m_minFlashStrength = 0.25f;
        [Tooltip("着弾フラッシュの最大強さ")]
        [SerializeField, Range(0.0f, 1.0f)] float m_maxFlashStrength = 0.7f;
        [Tooltip("クレーター半径のステージ半径に対する比率")]
        [SerializeField, Range(0.2f, 0.4f)] float m_craterRadiusRatio = 0.35f;
        [Tooltip("ステージ半径の上書き(0なら床コライダーから自動取得)")]
        [SerializeField, Min(0.0f)] float m_stageRadiusOverride = 0.0f;
        [Tooltip("クレーター深さの半径に対する比率")]
        [SerializeField, Range(0.05f, 0.2f)] float m_craterDepthRatio = 0.14f;
        [Tooltip("外周の輪郭ノイズ強度")]
        [SerializeField, Range(0.0f, 0.3f)] float m_craterOutlineNoise = 0.18f;
        [Tooltip("中心の発光強度")]
        [SerializeField, Range(0.0f, 4.0f)] float m_craterEmission = 2.2f;
        [Tooltip("主亀裂の本数")]
        [SerializeField, Range(4, 6)] int m_crackCount = 5;
        [Tooltip("亀裂の長さ(クレーター半径に対する倍率)")]
        [SerializeField, Range(1.2f, 1.8f)] float m_crackLengthScale = 1.5f;
        [Tooltip("クレーターが残る時間(秒)")]
        [SerializeField, Min(1.0f)] float m_craterLifetime = 10.0f;
        [Tooltip("クレーターが消えるまでの時間(秒)")]
        [SerializeField, Min(0.1f)] float m_craterFadeTime = 2.5f;
        [Tooltip("岩片の上向き初速")]
        [SerializeField, Min(0.0f)] float m_debrisUpSpeed = 14.0f;
        [Tooltip("岩片の外向き初速")]
        [SerializeField, Min(0.0f)] float m_debrisOutSpeed = 3.5f;
        [Tooltip("岩片が消えるまでの時間(秒)")]
        [SerializeField, Min(0.2f)] float m_debrisLifetime = 3.4f;
        [Tooltip("着弾時のヒットストップ時間(秒)")]
        [SerializeField, Min(0.0f)] float m_hitStopDuration = 0.05f;
        [Tooltip("ヒットストップ中のTime.timeScale")]
        [SerializeField, Range(0.01f, 1.0f)] float m_hitStopTimeScale = 0.05f;
        [Tooltip("隕石キル時にEliminationCinematicのスロー/撃破エフェクトを抑制する")]
        [SerializeField] bool m_skipEliminationCinematic = true;
        [Tooltip("撃破時に通常の破片演出を出す積み木の最大数(残りは即消去)")]
        [SerializeField, Min(0)] int m_maxStackShatter = 2;
        [Tooltip("衝撃波リング開始半径(クレーター半径比)")]
        [SerializeField, Range(0.3f, 1.0f)] float m_shockwaveStartRatio = 0.3f;
        [Tooltip("衝撃波リング終了半径(ステージ半径比)")]
        [SerializeField, Range(0.8f, 1.0f)] float m_shockwaveEndRatio = 0.85f;
        [Tooltip("衝撃波リングの持続時間(秒)")]
        [SerializeField, Range(0.4f, 0.6f)] float m_shockwaveDuration = 0.5f;
        [Tooltip("予告円を投影する地面のレイヤー")]
        [SerializeField] LayerMask m_groundMask = ~0;
        [Tooltip("発動・着弾をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logSpecial = true;

        [Header("カットイン")]
        [SerializeField] string m_cutInName = "いわだる崩し";
        [SerializeField] Color m_cutInColor = new Color(0.62f, 0.50f, 0.38f, 1.0f);

        [Header("予告円")]
        [SerializeField] Color m_warningColor = new Color(1.0f, 0.22f, 0.08f, 0.9f);

        enum State
        {
            Idle,
            Countdown,
            Rising,
            CutIn,
            Falling,
            Impact,
        }

        DarumaCharacter m_character;
        SpecialGauge m_gauge;
        State m_state = State.Idle;
        float m_stateTime;
        CircleHitboxView m_hitboxView;
        int m_snapshotBlocks;
        float m_attackRadius;
        float m_meteorHeight;
        float m_riseTime;
        float m_fallTime;
        float m_meteorScale;
        float m_blockFactor;
        float m_groundY;
        float m_apexY;
        float m_fallGravity;
        Vector3 m_targetPosition;
        bool m_hideCharacter;
        bool m_impactApplied;
        float m_impactFlashUntil;
        float m_impactFlashStrength;
        float m_impactFlashDuration;

        GameObject m_warning;
        Material m_warningMaterial;
        LineRenderer m_warningLine;
        Material m_warningLineMaterial;
        GameObject m_meteor;
        Material m_meteorRockMaterial;
        Material m_meteorTrailMaterial;

        GUIStyle m_countStyle;
        bool m_stylesReady;

        static Texture2D s_warningTexture;

        public bool IsActive => m_state != State.Idle;
        public int LastBlocks => m_snapshotBlocks;
        public float AttackRadius => m_attackRadius;
        public Vector3 DebugCenter => m_character != null
            ? (m_state == State.Countdown ? m_character.transform.position : m_targetPosition)
            : m_targetPosition;
        public float CountdownRemaining => m_state == State.Countdown
            ? Mathf.Max(0.0f, m_countdown - m_stateTime)
            : 0.0f;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            m_gauge = GetComponent<SpecialGauge>();
            m_hitboxView = CircleHitboxView.Create("IwaAttackHitbox", null,
                () => DebugCenter, () => m_attackRadius, () => IsActive,
                new Color(1.0f, 0.15f, 0.10f, 0.95f));

            // 初回発動時のシェーダー・メッシュ生成スパイクを避けるためのウォームアップ
            IwaCraterEffect.Warmup();
            IwaShockwaveRing.Warmup();
        }

        private void Update()
        {
            if (m_state == State.Idle)
            {
                if (m_gauge == null || !IsLocalHuman()) return;

                if (SpecialInput.WasPressed(m_character)) TryActivate();
                return;
            }

            float dt = Time.deltaTime;
            switch (m_state)
            {
                case State.Countdown:
                    m_stateTime += dt;
                    UpdateWarning();
                    if (m_stateTime >= m_countdown) BeginJump();
                    break;

                case State.Rising:
                    m_stateTime += dt;
                    UpdateRising();
                    break;

                case State.CutIn:
                    if (!IwadaruCutIn.IsPlaying) BeginFall();
                    break;

                case State.Falling:
                    m_stateTime += dt;
                    UpdateFalling();
                    break;

                case State.Impact:
                    m_stateTime += dt;
                    if (m_stateTime >= m_impactRecovery) EndSpecial();
                    break;
            }

            if (m_state == State.Rising || m_state == State.Falling || m_state == State.Impact)
                UpdateWarning();
        }

        // 無敵の点滅で隕石や予告が見えなくならないよう、表示を自前で固定する
        private void LateUpdate()
        {
            if (m_state == State.Idle || m_character == null) return;

            bool visible = !m_hideCharacter;
            foreach (var renderer in m_character.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null) renderer.enabled = visible;
            }
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

        public bool TryActivate()
        {
            if (m_state != State.Idle) return false;
            if (m_gauge == null || !m_gauge.IsFull) return false;
            if (m_character == null || m_character.Stack == null || m_character.Stack.Count == 0) return false;
            if (!m_character.CanBeAffected) return false;
            if (m_character.Hammer != null && m_character.Hammer.IsBusy) return false;

            m_snapshotBlocks = Mathf.Clamp(m_character.Stack.Count, 1, m_maxBlocks);
            float t = m_maxBlocks > 1
                ? Mathf.InverseLerp(1.0f, m_maxBlocks, m_snapshotBlocks)
                : 1.0f;

            m_blockFactor = t;
            m_attackRadius = Mathf.Lerp(m_minRadius, m_maxRadius, t)
                * Mathf.Max(0.1f, m_attackRadiusScale);
            m_meteorHeight = Mathf.Lerp(m_minHeight, m_maxHeight, t);
            m_riseTime = Mathf.Lerp(m_minRiseTime, m_maxRiseTime, t);
            m_fallTime = Mathf.Lerp(m_minFallTime, m_maxFallTime, t);
            m_meteorScale = Mathf.Lerp(m_minMeteorScale, m_maxMeteorScale, t);

            m_gauge.Consume();
            m_state = State.Countdown;
            m_stateTime = 0.0f;
            m_impactApplied = false;
            m_hideCharacter = false;
            m_impactFlashUntil = 0.0f;
            m_targetPosition = m_character.transform.position;

            float invincibleTime = m_countdown + m_riseTime + 1.4f + m_fallTime + m_impactRecovery + 1.0f;
            m_character.SetInvincible(invincibleTime);
            m_character.MovementOnly = true;
            CreateWarning();

            if (m_logSpecial)
                Debug.Log($"[いわだる] 必殺技『いわだる崩し』発動: 積み木{m_snapshotBlocks}個 半径{m_attackRadius:F1}m 高度{m_meteorHeight:F1}m", this);
            return true;
        }

        void BeginJump()
        {
            m_targetPosition = m_character.transform.position;
            m_groundY = GetGroundY(m_targetPosition);
            m_apexY = m_groundY + m_meteorHeight;

            // 予告円はカットイン後・落下中も着弾まで表示し続ける
            ConsumeAllBlocks();
            SetCharacterHidden(true);
            m_character.SetSuspended(true);

            m_meteor = CreateMeteor(m_targetPosition);
            if (m_meteor != null)
                m_meteor.transform.position = new Vector3(m_targetPosition.x, m_groundY, m_targetPosition.z);

            m_state = State.Rising;
            m_stateTime = 0.0f;
        }

        void UpdateRising()
        {
            float duration = Mathf.Max(0.05f, m_riseTime);
            float u = Mathf.Clamp01(m_stateTime / duration);
            float eased = 1.0f - Mathf.Pow(1.0f - u, 2.0f);
            float y = Mathf.Lerp(m_groundY, m_apexY, eased);
            if (m_meteor != null)
            {
                m_meteor.transform.position = new Vector3(m_targetPosition.x, y, m_targetPosition.z);
                m_meteor.transform.Rotate(Vector3.up, 260.0f * Time.deltaTime, Space.World);
            }

            if (u >= 1.0f)
            {
                m_state = State.CutIn;
                m_stateTime = 0.0f;
                if (m_meteor != null)
                {
                    IwadaruCutIn.Show(m_cutInName, m_meteor);
                }
                else
                {
                    BeginFall();
                }
            }
        }

        void BeginFall()
        {
            m_state = State.Falling;
            m_stateTime = 0.0f;
            float duration = Mathf.Max(0.05f, m_fallTime);
            m_fallGravity = 2.0f * m_meteorHeight / (duration * duration);
        }

        void UpdateFalling()
        {
            float y = m_apexY - 0.5f * m_fallGravity * m_stateTime * m_stateTime;
            if (m_meteor != null)
                m_meteor.transform.position = new Vector3(m_targetPosition.x, y, m_targetPosition.z);

            if (y <= m_groundY)
            {
                Impact();
            }
        }

        void Impact()
        {
            m_state = State.Impact;
            m_stateTime = 0.0f;

            if (m_meteor != null)
            {
                Destroy(m_meteor);
                m_meteor = null;
            }

            if (!m_impactApplied)
            {
                m_impactApplied = true;
                ApplyImpact();
            }

            // キャラクターを着弾地点へ戻し、通常状態に戻す
            if (m_character != null)
            {
                m_hideCharacter = false;
                m_character.SetSuspended(false);
                m_character.Teleport(new Vector3(m_targetPosition.x, m_groundY, m_targetPosition.z), 0.0f);
                m_character.Rigidbody.linearVelocity = Vector3.zero;
                m_character.SetInvincible(m_impactRecovery + 0.3f);
            }
        }

        void ApplyImpact()
        {
            // 撃破時だけEliminationCinematicがEffect_Eliminationを生成し、
            // Time.timeScaleを0.15へ落とすため、隕石キルでは一時的に止める。
            EliminationCinematic[] cinematics = null;
            bool[] cinematicEnabled = null;
            if (m_skipEliminationCinematic)
            {
                cinematics = FindObjectsByType<EliminationCinematic>(FindObjectsInactive.Exclude);
                cinematicEnabled = new bool[cinematics.Length];
                for (int i = 0; i < cinematics.Length; i++)
                {
                    cinematicEnabled[i] = cinematics[i].enabled;
                    if (cinematics[i].enabled) cinematics[i].enabled = false;
                }
            }

            // 敵キャラクター: 自分以外を即死。既存の無敵は尊重する
            foreach (var character in FindObjectsByType<DarumaCharacter>(FindObjectsInactive.Exclude))
            {
                if (character == null || character == m_character) continue;
                if (character.IsInvincible) continue;

                Vector3 delta = character.transform.position - m_targetPosition;
                delta.y = 0.0f;
                if (delta.magnitude > m_attackRadius) continue;

                // 相手の積み木も必ず消えるよう、死亡前に段を落とす
                DestroyStack(character);
                character.Eliminate(EliminationCause.Other, Vector3.up, character.transform.position);
            }

            if (cinematics != null)
            {
                for (int i = 0; i < cinematics.Length; i++)
                {
                    if (cinematics[i] != null) cinematics[i].enabled = cinematicEnabled[i];
                }
            }

            // 範囲内の積み木を破壊
            foreach (var block in FindObjectsByType<TumikiBlock>(FindObjectsInactive.Exclude))
            {
                if (block == null) continue;

                Vector3 delta = block.transform.position - m_targetPosition;
                delta.y = 0.0f;
                if (delta.magnitude > m_attackRadius) continue;

                RemoveBlockFromStack(block);
                block.Break(Vector3.up * 2.0f, 0.0f, 0.0f);
            }

            // 着弾直後の白〜薄黄色の衝撃波リング。
            // 以前の赤橙のGimmickShockwave複数リングは、クレーター発光と重複するため置き換えた。
            float effectT = m_blockFactor;

            // 画面揺れも積み木数で大きく変える
            var camera = Camera.main;
            if (camera != null)
            {
                var shake = camera.GetComponent<CameraShake>();
                if (shake != null)
                {
                    float amplitude = Mathf.Lerp(m_minShakeAmplitude, m_maxShakeAmplitude, effectT);
                    shake.Shake(new CameraShake.ShakeSettings(amplitude,
                        Mathf.Lerp(0.45f, 0.9f, effectT)));
                }
            }

            // 閃光。大きいほど強く長い
            m_impactFlashDuration = Mathf.Lerp(0.22f, 0.60f, effectT);
            m_impactFlashStrength = Mathf.Lerp(m_minFlashStrength, m_maxFlashStrength, effectT);
            m_impactFlashUntil = Time.unscaledTime + m_impactFlashDuration;

            // 着弾光
            var lightObject = new GameObject("IwadaruImpactLight");
            lightObject.transform.position = m_targetPosition + Vector3.up * 1.0f;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1.0f, 0.55f, 0.2f);
            light.range = Mathf.Lerp(m_attackRadius * 1.3f, m_attackRadius * 2.3f, effectT);
            light.intensity = Mathf.Lerp(8.0f, 30.0f, effectT);
            light.shadows = LightShadows.None;
            Destroy(lightObject, 0.45f);

            Material groundMaterial = GetGroundMaterial(m_targetPosition);
            float stageRadius = GetStageRadius(m_targetPosition);
            float craterRadius = Mathf.Clamp(stageRadius * m_craterRadiusRatio,
                0.8f, stageRadius * 0.45f);
            float craterDepth = craterRadius * m_craterDepthRatio;

            IwaShockwaveRing.Spawn(
                m_targetPosition,
                stageRadius,
                craterRadius * m_shockwaveStartRatio,
                stageRadius * m_shockwaveEndRatio,
                m_shockwaveDuration);

            IwaImpactHitStop.Spawn(m_hitStopDuration, m_hitStopTimeScale);
            SpawnDebris(groundMaterial, craterRadius);

            // あかだると同じ焦げシェーダーで、着弾地点の地面を焼く
            GroundScorch.SpawnCircle(
                new Vector3(m_targetPosition.x, m_groundY, m_targetPosition.z),
                craterRadius, 5.0f, 2.5f,
                new Color(0.06f, 0.04f, 0.03f),
                new Color(0.30f, 0.14f, 0.04f),
                0.56f);

            // 地面が割れてえぐれる演出
            IwaCraterEffect.Spawn(
                new Vector3(m_targetPosition.x, m_groundY, m_targetPosition.z),
                craterRadius,
                craterDepth,
                m_craterLifetime,
                m_craterFadeTime,
                m_craterOutlineNoise,
                m_craterEmission,
                groundMaterial);

            IwaGroundCracks.Spawn(
                new Vector3(m_targetPosition.x, m_groundY, m_targetPosition.z),
                craterRadius, m_crackCount, m_crackLengthScale,
                m_craterLifetime, m_craterFadeTime);

            IwaDustEffect.Spawn(
                new Vector3(m_targetPosition.x, m_groundY, m_targetPosition.z),
                craterRadius);
        }

        float GetStageRadius(Vector3 position)
        {
            if (m_stageRadiusOverride > 0.0f) return m_stageRadiusOverride;

            Vector3 origin = new Vector3(position.x, position.y + 6.0f, position.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 12.0f,
                m_groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            float radius = 0.0f;
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.normal.y < 0.5f) continue;
                if (hit.collider.GetComponentInParent<DarumaCharacter>() != null) continue;
                if (hit.collider.GetComponentInParent<TumikiBlock>() != null) continue;
                if (hit.distance >= best) continue;
                Bounds bounds = hit.collider.bounds;
                best = hit.distance;
                radius = Mathf.Min(bounds.extents.x, bounds.extents.z);
            }
            return radius > 0.1f ? Mathf.Clamp(radius, 1.5f, 30.0f) : 10.0f;
        }

        Material GetGroundMaterial(Vector3 position)
        {
            Vector3 origin = new Vector3(position.x, position.y + 6.0f, position.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 12.0f,
                m_groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Material found = null;
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.normal.y < 0.5f) continue;
                if (hit.collider.GetComponentInParent<DarumaCharacter>() != null) continue;
                if (hit.collider.GetComponentInParent<TumikiBlock>() != null) continue;
                if (hit.distance >= best) continue;
                var renderer = hit.collider.GetComponent<Renderer>();
                if (renderer == null || renderer.sharedMaterial == null) continue;
                best = hit.distance;
                found = renderer.sharedMaterial;
            }
            return found;
        }

        void SpawnDebris(Material groundMaterial, float craterRadius)
        {
            float t = m_blockFactor;
            int total = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(m_minDebris, m_maxDebris, t)),
                m_minDebris, m_maxDebris);
            int largeCount = Mathf.Clamp(total / 20, 1, 2);
            int mediumCount = Mathf.Clamp(total / 5, 2, 8);
            int smallCount = Mathf.Max(0, total - largeCount - mediumCount);

            Material material = groundMaterial != null ? groundMaterial : m_meteorRockMaterial;
            float maxRadius = Mathf.Max(0.5f, craterRadius * 0.9f);

            int[] counts = { largeCount, mediumCount, smallCount };
            for (int sizeClass = 0; sizeClass < counts.Length; sizeClass++)
            {
                for (int i = 0; i < counts[sizeClass]; i++)
                {
                    float angle = Random.Range(0.0f, Mathf.PI * 2.0f);
                    float radial = Mathf.Pow(Random.value, 1.5f) * maxRadius;
                    float centerFactor = 1.0f - radial / maxRadius;

                    float size;
                    if (sizeClass == 0) size = Mathf.Lerp(0.62f, 1.05f, t) * Random.Range(0.8f, 1.2f);
                    else if (sizeClass == 1) size = Mathf.Lerp(0.30f, 0.55f, t) * Random.Range(0.8f, 1.2f);
                    else size = Mathf.Lerp(0.12f, 0.27f, t) * Random.Range(0.75f, 1.25f);

                    Vector3 baseScale = new Vector3(
                        size * Random.Range(0.72f, 1.5f),
                        size * Random.Range(0.55f, 1.35f),
                        size * Random.Range(0.72f, 1.45f));

                    var debris = new GameObject($"IwadaruDebris_{sizeClass}_{i}");
                    debris.transform.position = new Vector3(
                        m_targetPosition.x + Mathf.Cos(angle) * radial,
                        m_groundY + 0.10f + baseScale.y * Random.Range(0.1f, 0.45f),
                        m_targetPosition.z + Mathf.Sin(angle) * radial);
                    debris.transform.rotation = Random.rotation;

                    var filter = debris.AddComponent<MeshFilter>();
                    var mesh = IwaCraterEffect.GetRockMesh(Random.Range(0, 6));
                    filter.sharedMesh = mesh;
                    var renderer = debris.AddComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        if (material != null) renderer.sharedMaterial = material;
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        renderer.receiveShadows = false;

                        // 床と同じ色を基準に、明度だけ±10%ばらす
                        if (material != null)
                        {
                            Color baseColor = material.HasProperty("_BaseColor")
                                ? material.GetColor("_BaseColor")
                                : material.HasProperty("_Color") ? material.GetColor("_Color") : Color.gray;
                            float brightness = Random.Range(0.9f, 1.1f);
                            baseColor.r = Mathf.Clamp01(baseColor.r * brightness);
                            baseColor.g = Mathf.Clamp01(baseColor.g * brightness);
                            baseColor.b = Mathf.Clamp01(baseColor.b * brightness);
                            var block = new MaterialPropertyBlock();
                            block.SetColor("_BaseColor", baseColor);
                            block.SetColor("_Color", baseColor);
                            renderer.SetPropertyBlock(block);
                        }
                    }

                    var body = debris.AddComponent<Rigidbody>();
                    body.mass = Mathf.Max(0.2f, size * 5.0f);
                    Vector3 radialDirection = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
                    float upSpeed = Mathf.Lerp(5.0f, m_debrisUpSpeed, t) * (0.7f + 0.6f * centerFactor);
                    float classOutward = sizeClass == 0 ? 0.45f : sizeClass == 1 ? 0.8f : 1.35f;
                    float outSpeed = Mathf.Lerp(0.8f, m_debrisOutSpeed, t)
                        * Random.Range(0.35f, 0.85f) * classOutward;
                    body.linearVelocity = radialDirection * outSpeed + Vector3.up * upSpeed;
                    body.angularVelocity = Random.insideUnitSphere * Mathf.Lerp(8.0f, 22.0f, t);

                    var piece = debris.AddComponent<IwaDebrisPiece>();
                    piece.Begin(mesh, baseScale,
                        Random.Range(m_debrisLifetime * 0.65f, m_debrisLifetime), 0.65f, false);
                }
            }
        }

        void DestroyStack(DarumaCharacter character)
        {
            if (character == null || character.Stack == null) return;

            int shattered = 0;
            while (character.Stack.Count > 0)
            {
                var block = character.Stack.RemoveAt(0);
                if (block == null) continue;

                // 撃破時に全段のTumikiShatterを出すと重いので、演出する段数を制限する
                if (shattered < m_maxStackShatter)
                {
                    block.Break(Vector3.up * 2.0f, 0.0f, 0.0f);
                    shattered++;
                }
                else
                {
                    block.Consume();
                }
            }
        }

        void RemoveBlockFromStack(TumikiBlock block)
        {
            var owner = block.GetComponentInParent<DarumaCharacter>();
            if (owner == null || owner.Stack == null) return;

            for (int i = 0; i < owner.Stack.Count; i++)
            {
                if (owner.Stack.Get(i) == block)
                {
                    owner.Stack.RemoveAt(i);
                    return;
                }
            }
        }

        void ConsumeAllBlocks()
        {
            if (m_character == null || m_character.Stack == null) return;

            while (m_character.Stack.Count > 0)
            {
                var block = m_character.Stack.RemoveAt(0);
                if (block != null) Destroy(block.gameObject);
            }
        }

        GameObject CreateMeteor(Vector3 position)
        {
            var root = new GameObject("IwadaruMeteor");
            root.transform.position = position;
            root.transform.localScale = Vector3.one * m_meteorScale;

            Transform headSource = FindPart(m_character, "Head");
            if (headSource != null)
            {
                var head = CloneVisual(headSource.gameObject, root.transform, "MeteorHead");
                head.transform.localPosition = Vector3.zero;
                head.transform.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
                head.transform.localScale = Vector3.one * 2.2f;

                var renderer = head.GetComponentInChildren<MeshRenderer>();
                Material sourceMat = renderer != null ? renderer.sharedMaterial : null;
                m_meteorRockMaterial = sourceMat != null
                    ? new Material(sourceMat)
                    : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m_meteorRockMaterial.name = "IwadaruMeteorRock";
                if (m_meteorRockMaterial.HasProperty("_BaseColor"))
                    m_meteorRockMaterial.SetColor("_BaseColor", new Color(0.42f, 0.39f, 0.35f, 1.0f));
            }

            if (m_meteorRockMaterial == null)
            {
                m_meteorRockMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (m_meteorRockMaterial.HasProperty("_BaseColor"))
                    m_meteorRockMaterial.SetColor("_BaseColor", new Color(0.42f, 0.39f, 0.35f, 1.0f));
            }

            // 岩の破片
            var cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            for (int i = 0; i < 16; i++)
            {
                var rock = new GameObject($"MeteorRock{i}");
                rock.transform.SetParent(root.transform, false);
                float angle = (i / 16.0f) * Mathf.PI * 2.0f + Random.Range(-0.2f, 0.2f);
                float radius = Random.Range(1.0f, 1.7f);
                rock.transform.localPosition = new Vector3(
                    Mathf.Cos(angle) * radius,
                    Random.Range(-0.9f, 0.9f),
                    Mathf.Sin(angle) * radius);
                rock.transform.localRotation = Random.rotation;
                float size = Random.Range(0.3f, 0.75f);
                rock.transform.localScale = new Vector3(size, size * Random.Range(0.6f, 1.2f), size);

                rock.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
                var renderer = rock.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = m_meteorRockMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // 火の尾
            var trailObject = new GameObject("MeteorTrail");
            trailObject.transform.SetParent(root.transform, false);
            var trail = trailObject.AddComponent<TrailRenderer>();
            trail.time = 0.55f;
            trail.minVertexDistance = 0.1f;
            trail.startWidth = 4.0f;
            trail.endWidth = 0.0f;
            trail.startColor = new Color(1.0f, 0.35f, 0.05f, 0.95f);
            trail.endColor = new Color(1.0f, 0.1f, 0.0f, 0.0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.alignment = LineAlignment.View;

            Shader trailShader = Shader.Find("Sprites/Default");
            if (trailShader == null) trailShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (trailShader != null)
            {
                m_meteorTrailMaterial = new Material(trailShader);
                m_meteorTrailMaterial.mainTexture = Texture2D.whiteTexture;
                if (m_meteorTrailMaterial.HasProperty("_Color"))
                    m_meteorTrailMaterial.SetColor("_Color", Color.white);
                trail.sharedMaterial = m_meteorTrailMaterial;
            }

            var lightObject = new GameObject("MeteorLight");
            lightObject.transform.SetParent(root.transform, false);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1.0f, 0.45f, 0.12f);
            light.range = 12.0f;
            light.intensity = 7.0f;
            light.shadows = LightShadows.None;

            return root;
        }

        GameObject CloneVisual(GameObject source, Transform parent, string name)
        {
            var clone = Instantiate(source, parent, false);
            clone.name = name;

            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;

            foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true))
            {
                body.detectCollisions = false;
                body.useGravity = false;
                body.isKinematic = true;
            }

            foreach (var particles in clone.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
                if (particleRenderer != null) particleRenderer.enabled = false;
            }

            foreach (var trail in clone.GetComponentsInChildren<TrailRenderer>(true))
                trail.enabled = false;

            foreach (var mesh in clone.GetComponentsInChildren<MeshRenderer>(true))
                mesh.enabled = true;
            foreach (var skinned in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skinned.enabled = true;

            foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = 0;

            return clone;
        }

        static Transform FindPart(DarumaCharacter character, string name)
        {
            foreach (var t in character.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name) return t;
            }
            return null;
        }

        void SetCharacterHidden(bool hidden)
        {
            m_hideCharacter = hidden;
            if (m_character == null) return;

            foreach (var renderer in m_character.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null) renderer.enabled = !hidden;
            }
        }

        void CreateWarning()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "IwadaruWarningCircle";
            var collider = quad.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            quad.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            quad.transform.localScale = new Vector3(m_attackRadius * 2.0f, m_attackRadius * 2.0f, 1.0f);
            quad.layer = 0;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader != null)
            {
                m_warningMaterial = new Material(shader);
                m_warningMaterial.mainTexture = GetWarningTexture();
                if (m_warningMaterial.HasProperty("_Color"))
                    m_warningMaterial.SetColor("_Color", m_warningColor);
                var renderer = quad.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = m_warningMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }

            m_warning = quad;
            CreateWarningOutline();
            UpdateWarning();
        }

        void CreateWarningOutline()
        {
            var lineObject = new GameObject("IwadaruWarningOutline");
            m_warningLine = lineObject.AddComponent<LineRenderer>();
            m_warningLine.useWorldSpace = true;
            m_warningLine.loop = true;
            m_warningLine.positionCount = 64;
            m_warningLine.widthMultiplier = 0.12f;
            m_warningLine.numCornerVertices = 2;
            m_warningLine.numCapVertices = 2;
            m_warningLine.textureMode = LineTextureMode.Stretch;
            m_warningLine.alignment = LineAlignment.View;
            m_warningLine.startColor = m_warningColor;
            m_warningLine.endColor = m_warningColor;
            m_warningLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_warningLine.receiveShadows = false;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                m_warningLineMaterial = new Material(shader);
                if (m_warningLineMaterial.HasProperty("_Color"))
                    m_warningLineMaterial.SetColor("_Color", m_warningColor);
                if (m_warningLineMaterial.HasProperty("_BaseColor"))
                    m_warningLineMaterial.SetColor("_BaseColor", m_warningColor);
                m_warningLine.sharedMaterial = m_warningLineMaterial;
            }
        }

        void UpdateWarning()
        {
            if (m_warning == null) return;

            Vector3 position = m_state == State.Countdown
                ? m_character.transform.position
                : m_targetPosition;
            float ground = m_state == State.Countdown
                ? GetGroundY(position)
                : m_groundY;
            m_warning.transform.position = new Vector3(position.x, ground + 0.04f, position.z);
            // 予告円の直径 = 実際の攻撃半径 × 2。脈動で判定とズレないよう固定する
            m_warning.transform.localScale = new Vector3(
                m_attackRadius * 2.0f,
                m_attackRadius * 2.0f,
                1.0f);

            if (m_warningLine != null)
            {
                Vector3 center = new Vector3(position.x, ground + 0.08f, position.z);
                for (int i = 0; i < 64; i++)
                {
                    float angle = i * Mathf.PI * 2.0f / 64.0f;
                    m_warningLine.SetPosition(i, center + new Vector3(
                        Mathf.Cos(angle) * m_attackRadius,
                        0.0f,
                        Mathf.Sin(angle) * m_attackRadius));
                }
            }
        }

        float GetGroundY(Vector3 position)
        {
            Vector3 origin = new Vector3(position.x, position.y + 6.0f, position.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 30.0f, m_groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            foreach (var hit in hits)
            {
                if (hit.collider == null) continue;
                if (hit.collider.GetComponentInParent<DarumaCharacter>() == m_character) continue;
                if (hit.collider.GetComponentInParent<TumikiBlock>() != null) continue;
                if (hit.point.y > best) best = hit.point.y;
            }
            return best > float.MinValue ? best : position.y;
        }

        void DestroyWarning()
        {
            if (m_warning != null) Destroy(m_warning);
            if (m_warningMaterial != null) Destroy(m_warningMaterial);
            if (m_warningLine != null) Destroy(m_warningLine.gameObject);
            if (m_warningLineMaterial != null) Destroy(m_warningLineMaterial);
            m_warning = null;
            m_warningMaterial = null;
            m_warningLine = null;
            m_warningLineMaterial = null;
        }

        void EndSpecial()
        {
            State previous = m_state;
            m_state = State.Idle;
            m_stateTime = 0.0f;

            DestroyWarning();
            if (m_meteor != null)
            {
                Destroy(m_meteor);
                m_meteor = null;
            }

            if (m_character != null)
            {
                m_hideCharacter = false;
                SetCharacterHidden(false);
                m_character.SetSuspended(false);
                m_character.SetInvincible(0.0f);
                m_character.MovementOnly = false;
                m_character.Teleport(new Vector3(m_targetPosition.x, m_groundY, m_targetPosition.z), 0.0f);
                m_character.Rigidbody.linearVelocity = Vector3.zero;
            }

            if (m_meteorTrailMaterial != null) Destroy(m_meteorTrailMaterial);
            if (m_meteorRockMaterial != null) Destroy(m_meteorRockMaterial, 3.0f);
            m_meteorTrailMaterial = null;
            m_meteorRockMaterial = null;

            if (m_logSpecial && previous != State.Idle)
                Debug.Log("[いわだる] 必殺技終了", this);
        }

        void Abort()
        {
            if (m_state == State.Idle) return;
            m_state = State.Idle;

            DestroyWarning();
            if (m_meteor != null) Destroy(m_meteor);
            m_meteor = null;

            if (m_character != null)
            {
                m_hideCharacter = false;
                SetCharacterHidden(false);
                m_character.SetSuspended(false);
                m_character.SetInvincible(0.0f);
                m_character.MovementOnly = false;
            }

            if (m_meteorTrailMaterial != null) Destroy(m_meteorTrailMaterial);
            if (m_meteorRockMaterial != null) Destroy(m_meteorRockMaterial, 3.0f);
            m_meteorTrailMaterial = null;
            m_meteorRockMaterial = null;
        }

        private void OnDisable()
        {
            Abort();
        }

        private void OnDestroy()
        {
            Abort();
            if (m_hitboxView != null) Destroy(m_hitboxView.gameObject);
            m_hitboxView = null;
        }

        private void OnGUI()
        {
            // 着弾フラッシュ
            float flashRemaining = m_impactFlashUntil - Time.unscaledTime;
            if (flashRemaining > 0.0f && m_impactFlashDuration > 0.001f)
            {
                float flashAlpha = Mathf.Clamp01(flashRemaining / m_impactFlashDuration) * m_impactFlashStrength;
                GUI.color = new Color(1.0f, 0.82f, 0.65f, flashAlpha);
                GUI.DrawTexture(new Rect(0.0f, 0.0f, Screen.width, Screen.height),
                    Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            // 試合中だけゲージを表示(ラボはラボUI側で表示する)
            SpecialGaugeHUD.Draw(m_gauge, m_character,
                m_state == State.Countdown
                    ? $"METEOR {Mathf.Max(1, Mathf.CeilToInt(m_countdown - m_stateTime))}"
                    : null);

            if (m_state != State.Countdown) return;

            EnsureStyles();
            int count = Mathf.Max(1, Mathf.CeilToInt(m_countdown - m_stateTime));
            var countRect = new Rect(0.0f, 24.0f, Screen.width, 160.0f);
            GUI.color = new Color(0.0f, 0.0f, 0.0f, 0.8f);
            GUI.Label(new Rect(countRect.x + 4.0f, countRect.y + 4.0f, countRect.width, countRect.height),
                count.ToString(), m_countStyle);
            GUI.color = new Color(1.0f, 0.85f, 0.75f, 1.0f);
            GUI.Label(countRect, count.ToString(), m_countStyle);
            GUI.color = Color.white;
        }

        void EnsureStyles()
        {
            if (m_stylesReady) return;
            m_stylesReady = true;

            Font font = Font.CreateDynamicFontFromOSFont(
                new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic", "Noto Sans CJK JP" }, 40);
            if (font == null) font = GUI.skin.font;

            m_countStyle = new GUIStyle(GUI.skin.label);
            m_countStyle.font = font;
            m_countStyle.fontSize = 110;
            m_countStyle.fontStyle = FontStyle.Bold;
            m_countStyle.alignment = TextAnchor.MiddleCenter;
            m_countStyle.normal.textColor = Color.white;
        }

        static Texture2D GetWarningTexture()
        {
            if (s_warningTexture != null) return s_warningTexture;

            const int size = 512;
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
            float max = size * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 d = new Vector2(x + 0.5f, y + 0.5f) - center;
                    float r = d.magnitude / max;
                    if (r > 1.0f) continue;

                    float alpha = 0.0f;
                    if (r < 1.0f) alpha = 0.20f;
                    // 外周のリング(クアッドの外周 = 実際の攻撃半径)
                    alpha += Mathf.Clamp01(1.0f - Mathf.Abs(r - 0.98f) / 0.02f) * 0.95f;
                    // 中心の十字
                    if ((Mathf.Abs(d.x) < 2.0f && r < 0.82f) ||
                        (Mathf.Abs(d.y) < 2.0f && r < 0.82f))
                        alpha += 0.35f;

                    pixels[y * size + x] = new Color32(255, 255, 255,
                        (byte)(Mathf.Clamp01(alpha) * 255.0f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.SetPixels32(pixels);
            texture.Apply();
            s_warningTexture = texture;
            return texture;
        }
    }
}
