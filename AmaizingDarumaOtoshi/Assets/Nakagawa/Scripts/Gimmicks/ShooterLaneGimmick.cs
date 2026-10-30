using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // シューターレーン。正方形に近いベルトコンベアで、乗ったものをレーンの向き(forward)に一定の速さで飛ばす
    //  ・積み木: 乗ったらすぐ、入ってきたときの向き・速さに関係なく、レーンの向きに発射の速さで飛ばす
    //            持ち主は「持ち主の扱い」に従う(既定: 飛んでいた積み木は持ち主を引き継ぎ、落ちていた積み木は持ち主なし)
    //  ・プレイヤー: 運ぶ設定なら、乗っている間はレーンの向きに運び、先端(発射口)に来たら飛ばす
    // 見た目: ローラーが進行方向に回り続け、先端に発射口のふち(ジャンプ台のような反り)、進行方向を示す矢印が順に光る
    // レーンには当たり判定を付けない(床の上に置く)。このTransformの位置が表面の中心
    [DefaultExecutionOrder(-10)]
    public class ShooterLaneGimmick : MonoBehaviour
    {
        public enum OwnerMode
        {
            // 飛んでいた積み木は持ち主を引き継ぎ、落ちていた積み木は持ち主なしにする
            KeepFlyingOwner,
            // 常に持ち主(最後に殴ったプレイヤー)を引き継ぐ
            AlwaysKeep,
            // 常に持ち主なしにする
            AlwaysNone,
        }

        [Header("大きさ")]
        // レーンの大きさ(X = 幅, Z = 長さ)
        [SerializeField] Vector2 m_size = new Vector2(2.5f, 3.0f);
        // 表面からこの高さまでにいるものを乗っているとみなす
        [SerializeField] float m_rideHeight = 0.6f;

        [Header("積み木")]
        [SerializeField] bool m_launchBlocks = true;
        // 発射の速さ
        [SerializeField] float m_launchSpeed = 16.0f;
        [SerializeField] OwnerMode m_ownerMode = OwnerMode.KeepFlyingOwner;

        [Header("プレイヤー")]
        // プレイヤーを運ぶか(運ばない場合はプレイヤーには何もしない)
        [SerializeField] bool m_carryPlayers = true;
        // 運ぶ速さ
        [SerializeField] float m_carrySpeed = 4.0f;
        // 先端からこの長さの範囲(発射口)に来たら飛ばす
        [SerializeField] float m_launchZone = 0.6f;
        // プレイヤーを飛ばす水平の速さ・上向きの速さ
        [SerializeField] float m_playerLaunchSpeed = 14.0f;
        [SerializeField] float m_playerLaunchUp = 4.0f;
        // 同じプレイヤーを続けて飛ばさない時間
        [SerializeField] float m_playerCooldown = 0.5f;

        [Header("見た目")]
        // ベルトの土台(大きさを合わせる)
        [SerializeField] Transform m_belt;
        // ローラー(X軸まわりに回す。長さ方向に等間隔に並べ直す)
        [SerializeField] Transform[] m_rollers;
        // 発射口のふち(先端に置く)
        [SerializeField] Transform m_lip;
        // 進行方向を示す矢印(手前から先端へ順に並べ、順番に光らせる)
        [SerializeField] Renderer[] m_arrows;
        [SerializeField] Color m_arrowColor = GimmickPalette.Accelerate;
        // 矢印の光が手前から先端まで流れる時間
        [SerializeField] float m_arrowCycle = 0.8f;
        // ローラーの回る速さ(度/秒)
        [SerializeField] float m_rollerSpeed = 540.0f;
        [SerializeField] float m_surfaceOffset = 0.02f;

        [Header("エフェクト")]
        // 飛ばした瞬間: 飛ばしたものの後ろへオレンジの火花が流れ、床に輪が広がる
        [SerializeField] GimmickBurstSettings m_launchEffect = new GimmickBurstSettings
        {
            ColorA = GimmickPalette.Accelerate, ColorB = new Color(1.0f, 0.9f, 0.45f, 1.0f),
            Count = 24, Speed = new Vector2(3.0f, 7.0f), Size = new Vector2(0.05f, 0.14f), Lifetime = new Vector2(0.2f, 0.4f),
            Shape = GimmickBurstShape.Cone, ShapeSize = new Vector3(0.3f, 0, 0), ConeAngle = 20.0f, Stretch = true,
            Shockwave = true, ShockwaveColor = GimmickPalette.Accelerate, ShockwaveRadius = 1.3f, ShockwaveTime = 0.25f,
        };

        [Header("音")]
        [SerializeField] AudioClip m_launchClip;
        [SerializeField, Range(0.0f, 1.0f)] float m_launchVolume = 0.4f;

        // レーンの向きに飛んでいる積み木が、発射の速さのこの割合より遅くなっていたら飛ばし直す
        const float kRelaunchSpeedRate = 0.8f;

        readonly List<IGimmickAffectable> m_found = new List<IGimmickAffectable>();
        readonly Dictionary<IGimmickAffectable, float> m_lastLaunch = new Dictionary<IGimmickAffectable, float>();
        readonly List<IGimmickAffectable> m_dead = new List<IGimmickAffectable>();
        float m_rollerAngle;
        float m_time;

        Vector3 Forward
        {
            get
            {
                Vector3 f = transform.forward;
                f.y = 0.0f;
                return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            }
        }

        private void Awake()
        {
            ApplyShape();
        }

        private void OnDisable()
        {
            m_lastLaunch.Clear();
        }

        private void OnValidate()
        {
            m_size = Vector2.Max(m_size, new Vector2(0.2f, 0.2f));
            ApplyShape();
        }

        private void FixedUpdate()
        {
            if (GimmickClock.IsHeld) return;
            Vector3 half = new Vector3(m_size.x * 0.5f, m_rideHeight * 0.5f, m_size.y * 0.5f);
            GimmickUtil.OverlapBox(transform.position + Vector3.up * (m_rideHeight * 0.5f), half, transform.rotation, m_found);
            Vector3 forward = Forward;
            foreach (var target in m_found)
            {
                if (target is TumikiBlock block) LaunchBlock(block, forward);
                else if (m_carryPlayers) CarryPlayer(target, forward);
            }
            CleanUp();
        }

        // 乗った積み木をレーンの向きに飛ばす(既に飛ばした積み木がレーンの上を進んでいる間は何もしない)
        private void LaunchBlock(TumikiBlock block, Vector3 forward)
        {
            if (!m_launchBlocks) return;
            bool flying = block.CurrentState == TumikiBlock.State.Flying;
            if (flying && Vector3.Dot(block.Direction, forward) > 0.99f && block.Speed >= m_launchSpeed * kRelaunchSpeedRate) return;

            IDarumaAttacker owner = null;
            switch (m_ownerMode)
            {
                case OwnerMode.KeepFlyingOwner: owner = flying ? block.Owner : null; break;
                case OwnerMode.AlwaysKeep: owner = block.Owner; break;
            }
            block.Launch(forward, m_launchSpeed, owner, null, owner != null ? block.ReturnCount : 0);
            PlayLaunchEffect(block.Rigidbody.position, forward);
        }

        // 飛ばしたものの後ろへ火花を流す
        private void PlayLaunchEffect(Vector3 position, Vector3 forward)
        {
            GimmickSound.Play(GimmickSound.Or(m_launchClip, GimmickSound.Launch), m_launchVolume);
            GimmickBurst.Play(m_launchEffect, position, Quaternion.LookRotation(-forward));
        }

        // 乗っている間は運び、発射口に来たら飛ばす
        private void CarryPlayer(IGimmickAffectable target, Vector3 forward)
        {
            target.AddCarryVelocity(forward * m_carrySpeed);

            Vector3 local = Quaternion.Inverse(transform.rotation) * (target.Rigidbody.position - transform.position);
            if (local.z < m_size.y * 0.5f - m_launchZone) return;
            if (m_lastLaunch.TryGetValue(target, out float last) && Time.time - last < m_playerCooldown) return;
            m_lastLaunch[target] = Time.time;
            target.AddKnockback(forward * m_playerLaunchSpeed + Vector3.up * m_playerLaunchUp);
            PlayLaunchEffect(target.Rigidbody.position + Vector3.up * 0.3f, forward);
        }

        private void CleanUp()
        {
            if (m_lastLaunch.Count == 0) return;
            m_dead.Clear();
            foreach (var pair in m_lastLaunch)
            {
                if (!GimmickUtil.IsAlive(pair.Key) || Time.time - pair.Value > m_playerCooldown) m_dead.Add(pair.Key);
            }
            foreach (var key in m_dead) m_lastLaunch.Remove(key);
        }

        private void Update()
        {
            float dt = GimmickClock.IsHeld ? 0.0f : Time.deltaTime;
            m_time += dt;
            m_rollerAngle = Mathf.Repeat(m_rollerAngle + m_rollerSpeed * dt, 360.0f);
            if (m_rollers != null)
            {
                foreach (var r in m_rollers)
                {
                    if (r != null) r.localRotation = Quaternion.Euler(m_rollerAngle, 0.0f, 90.0f);
                }
            }
            UpdateArrows();
        }

        // 矢印の光が手前から先端へ流れる
        private void UpdateArrows()
        {
            if (m_arrows == null || m_arrows.Length == 0) return;
            float head = Mathf.Repeat(m_time / Mathf.Max(0.05f, m_arrowCycle), 1.0f) * m_arrows.Length;
            for (int i = 0; i < m_arrows.Length; i++)
            {
                if (m_arrows[i] == null) continue;
                float d = Mathf.Repeat(head - i, m_arrows.Length);
                float light = Mathf.Clamp01(1.0f - d / 1.5f);
                EffectMeshes.SetColor(m_arrows[i], GimmickPalette.WithAlpha(EffectMeshes.Brighten(m_arrowColor, light * 0.5f), Mathf.Lerp(0.25f, 1.0f, light)));
            }
        }

        // 大きさに合わせて土台・ローラー・ふち・矢印を並べ直す(エディタから設定を変えたときにも呼ぶ)
        public void ApplyShape()
        {
            float w = m_size.x, l = m_size.y;
            if (m_belt != null)
            {
                m_belt.localScale = new Vector3(w, 0.1f, l);
                m_belt.localPosition = new Vector3(0.0f, m_surfaceOffset - 0.05f, 0.0f);
            }
            if (m_rollers != null && m_rollers.Length > 0)
            {
                for (int i = 0; i < m_rollers.Length; i++)
                {
                    var r = m_rollers[i];
                    if (r == null) continue;
                    float z = Mathf.Lerp(-l * 0.5f, l * 0.5f, (i + 0.5f) / m_rollers.Length);
                    r.localPosition = new Vector3(0.0f, m_surfaceOffset + 0.005f, z);
                    // Unityの円柱は高さ2なので、長さ(幅方向)はYに半分
                    r.localScale = new Vector3(0.16f, w * 0.46f, 0.16f);
                    r.localRotation = Quaternion.Euler(0.0f, 0.0f, 90.0f);
                }
            }
            if (m_lip != null)
            {
                m_lip.localPosition = new Vector3(0.0f, m_surfaceOffset + 0.06f, l * 0.5f - 0.05f);
                m_lip.localScale = new Vector3(w, 0.1f, 0.35f);
                m_lip.localRotation = Quaternion.Euler(-20.0f, 0.0f, 0.0f);
            }
            if (m_arrows == null) return;
            for (int i = 0; i < m_arrows.Length; i++)
            {
                if (m_arrows[i] == null) continue;
                float z = Mathf.Lerp(-l * 0.5f, l * 0.5f, (i + 0.6f) / (m_arrows.Length + 0.4f));
                m_arrows[i].transform.localPosition = new Vector3(0.0f, m_surfaceOffset + 0.12f, z);
                m_arrows[i].transform.localScale = new Vector3(w * 0.5f, 1.0f, w * 0.5f);
                EffectMeshes.SetColor(m_arrows[i], GimmickPalette.WithAlpha(m_arrowColor, 0.5f));
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = m_arrowColor;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0, m_rideHeight * 0.5f, 0), new Vector3(m_size.x, m_rideHeight, m_size.y));
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * (m_size.y * 0.5f + 1.0f));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
