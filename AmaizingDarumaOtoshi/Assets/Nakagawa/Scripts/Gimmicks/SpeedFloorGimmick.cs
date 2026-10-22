using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 加速床 / 減速床。乗っている間、今の向きのまま速さだけを変える
    //  加速床(オレンジ): 速さを上限まで上げる。乗った瞬間に一瞬スローがかかる。止まっているものは向いている方向へ動き出す
    //                    床から外へ広がる光の輪を流し、乗っているものからオレンジの火花を出す
    //  減速床(青緑)    : 速さを1秒あたり一定の割合で割って下限まで下げる
    //                    光の輪は外から中心へ縮み、乗っているものから小さな泡を出す
    // 積み木の速さは床を出ても残る(弾道の速さが変わる)。プレイヤーは移動入力で速さが決まるので、乗っている間だけ変わる
    // 落ちている積み木が動き出したら、弾と同じく攻撃判定を持ち、最後に殴ったプレイヤーを持ち主として引き継ぐ
    // 床には当たり判定を付けない(床の上に置く)。このTransformの位置が床の表面の中心
    [DefaultExecutionOrder(-10)]
    public class SpeedFloorGimmick : MonoBehaviour
    {
        public enum Mode { Accelerate, Decelerate }

        [SerializeField] Mode m_mode = Mode.Accelerate;
        // 床の大きさ(X, Z)。積み木2×2程度
        [SerializeField] Vector2 m_size = new Vector2(2.0f, 2.0f);
        // 表面からこの高さまでにいるものを乗っているとみなす
        [SerializeField] float m_rideHeight = 0.6f;
        [SerializeField] bool m_affectPlayers = true;
        [SerializeField] bool m_affectBlocks = true;

        [Header("加速床")]
        // 速さを上げる量(1秒あたり)。大きいほどすぐ上限に届く
        [SerializeField] float m_acceleration = 60.0f;
        // 速さの上限(この速さにそろえる)
        [SerializeField] float m_maxSpeed = 14.0f;
        // 止まっているとみなす速さ(これ未満なら向いている方向へ上限の速さで動き出す)
        [SerializeField] float m_stillSpeed = 0.3f;
        // 乗った瞬間のスロー
        [SerializeField] float m_enterSlowTime = 0.08f;
        [SerializeField, Range(0.0f, 1.0f)] float m_enterSlowTimeScale = 0.2f;

        [Header("減速床")]
        // 1秒あたりに速さを割る数(2なら1秒で半分)
        [SerializeField, Min(1.0f)] float m_divideRate = 4.0f;
        // 速さの下限(これより遅くはしない)
        [SerializeField] float m_minSpeed = 2.0f;

        [Header("見た目")]
        // 床の下地(大きさを合わせ、色を付ける)
        [SerializeField] Renderer m_base;
        // 流れる光の輪(この数だけ時間をずらして流す。大きさは床の短い辺に合わせる)
        [SerializeField] Renderer[] m_rings;
        [SerializeField] Color m_accelerateColor = GimmickPalette.Accelerate;
        [SerializeField] Color m_decelerateColor = GimmickPalette.Decelerate;
        [SerializeField, Range(0.0f, 1.0f)] float m_baseAlpha = 0.6f;
        // 輪が端から端まで流れる時間
        [SerializeField] float m_ringCycle = 1.2f;
        [SerializeField] float m_surfaceOffset = 0.01f;

        [Header("乗っているものの演出")]
        [SerializeField] GimmickParticleSettings m_accelerateEffect = new GimmickParticleSettings
        {
            ColorA = GimmickPalette.Accelerate, ColorB = new Color(1.0f, 0.85f, 0.3f, 1.0f),
            Rate = 40.0f, Lifetime = 0.35f, Speed = new Vector2(2.0f, 4.0f), Size = new Vector2(0.05f, 0.12f),
            ConeAngle = 35.0f, Radius = 0.35f, Stretch = true,
        };
        [SerializeField] GimmickParticleSettings m_decelerateEffect = new GimmickParticleSettings
        {
            ColorA = GimmickPalette.Decelerate, ColorB = new Color(0.7f, 1.0f, 1.0f, 1.0f),
            Rate = 18.0f, Lifetime = 0.9f, Speed = new Vector2(0.3f, 0.8f), Size = new Vector2(0.06f, 0.14f),
            Gravity = -0.05f, ConeAngle = 10.0f, Radius = 0.4f,
        };

        [Header("乗った瞬間のエフェクト")]
        // 加速床: 進む向きへ火花が飛び、床に輪が広がる
        [SerializeField] GimmickBurstSettings m_accelerateEnterEffect = new GimmickBurstSettings
        {
            ColorA = GimmickPalette.Accelerate, ColorB = new Color(1.0f, 0.9f, 0.4f, 1.0f),
            Count = 22, Speed = new Vector2(4.0f, 8.0f), Size = new Vector2(0.05f, 0.14f), Lifetime = new Vector2(0.2f, 0.4f),
            Shape = GimmickBurstShape.Cone, ShapeSize = new Vector3(0.3f, 0, 0), ConeAngle = 30.0f, Stretch = true,
            Shockwave = true, ShockwaveColor = GimmickPalette.Accelerate, ShockwaveRadius = 1.2f, ShockwaveTime = 0.25f,
        };
        // 減速床: 泡がふわっと立ち上り、床に輪が広がる
        [SerializeField] GimmickBurstSettings m_decelerateEnterEffect = new GimmickBurstSettings
        {
            ColorA = GimmickPalette.Decelerate, ColorB = new Color(0.75f, 1.0f, 1.0f, 1.0f),
            Count = 18, Speed = new Vector2(0.5f, 1.5f), Size = new Vector2(0.08f, 0.2f), Lifetime = new Vector2(0.5f, 0.9f),
            Gravity = -0.1f, Shape = GimmickBurstShape.Hemisphere, ShapeSize = new Vector3(0.45f, 0, 0),
            Shockwave = true, ShockwaveColor = GimmickPalette.Decelerate, ShockwaveRadius = 1.0f, ShockwaveTime = 0.4f,
        };

        readonly List<IGimmickAffectable> m_found = new List<IGimmickAffectable>();
        // 乗っている相手ごとの、床が決めた今の速さ(プレイヤーの速さは毎ステップ入力から決まるので、床の側で覚えておく)
        readonly Dictionary<IGimmickAffectable, float> m_riders = new Dictionary<IGimmickAffectable, float>();
        readonly List<IGimmickAffectable> m_left = new List<IGimmickAffectable>();
        GimmickTargetEffects m_effects;
        float m_time;

        public Mode FloorMode => m_mode;
        Color MainColor => m_mode == Mode.Accelerate ? m_accelerateColor : m_decelerateColor;

        private void Awake()
        {
            m_effects = new GimmickTargetEffects("SpeedFloorEffect",
                () => m_mode == Mode.Accelerate ? m_accelerateEffect : m_decelerateEffect);
            ApplyShape();
        }

        private void OnDisable()
        {
            m_riders.Clear();
            m_effects?.Clear();
        }

        private void OnValidate()
        {
            m_size = Vector2.Max(m_size, new Vector2(0.1f, 0.1f));
            m_maxSpeed = Mathf.Max(0.0f, m_maxSpeed);
            m_minSpeed = Mathf.Max(0.0f, m_minSpeed);
            ApplyShape();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 half = new Vector3(m_size.x * 0.5f, m_rideHeight * 0.5f, m_size.y * 0.5f);
            GimmickUtil.OverlapBox(transform.position + Vector3.up * (m_rideHeight * 0.5f), half, transform.rotation, m_found);

            m_effects.Begin();
            foreach (var target in m_found)
            {
                bool isBlock = target is TumikiBlock;
                if (isBlock ? !m_affectBlocks : !m_affectPlayers) continue;

                Vector3 h = target.HorizontalVelocity;
                float current = h.magnitude;
                bool entered = !m_riders.TryGetValue(target, out float speed);
                if (entered)
                {
                    speed = current;
                    if (m_mode == Mode.Accelerate && m_enterSlowTime > 0.0f)
                    {
                        target.StartHitStop(m_enterSlowTime, m_enterSlowTimeScale, 0.0f);
                    }
                    PlayEnterEffect(target);
                }

                if (m_mode == Mode.Accelerate)
                {
                    if (current < m_stillSpeed)
                    {
                        // 止まっているものは向いている方向へ動き出す
                        target.AddKnockback(FacingDirection(target) * m_maxSpeed);
                        speed = m_maxSpeed;
                    }
                    else if (current < m_maxSpeed)
                    {
                        speed = Mathf.Min(m_maxSpeed, Mathf.Max(speed, current) + m_acceleration * dt);
                        target.RequestSpeedScale(speed / current);
                    }
                    else speed = current;
                }
                else
                {
                    if (current > m_minSpeed && current > 0.0001f)
                    {
                        speed = Mathf.Max(m_minSpeed, Mathf.Min(speed, current) * Mathf.Pow(m_divideRate, -dt));
                        target.RequestSpeedScale(speed / current);
                    }
                    else speed = Mathf.Min(speed, current);
                }

                m_riders[target] = speed;
                m_effects.Touch(target);
            }
            m_effects.End();
            RemoveLeftRiders();
        }

        // 向いている水平方向(真上・真下を向いていれば床の前方)
        private Vector3 FacingDirection(IGimmickAffectable target)
        {
            Vector3 f = target.Rigidbody.rotation * Vector3.forward;
            f.y = 0.0f;
            if (f.sqrMagnitude < 0.0001f) f = transform.forward;
            f.y = 0.0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }

        // 乗った瞬間のエフェクト(加速床は進む向きへ火花、減速床は泡)
        private void PlayEnterEffect(IGimmickAffectable target)
        {
            Vector3 pos = target.Rigidbody.position + Vector3.up * 0.1f;
            Vector3 dir = target.HorizontalVelocity;
            if (dir.sqrMagnitude < 0.0001f) dir = FacingDirection(target);
            GimmickBurst.Play(m_mode == Mode.Accelerate ? m_accelerateEnterEffect : m_decelerateEnterEffect,
                pos, Quaternion.LookRotation(dir.normalized));
        }

        // 床から降りた・消えた相手を忘れる
        private void RemoveLeftRiders()
        {
            m_left.Clear();
            foreach (var key in m_riders.Keys)
            {
                if (!m_found.Contains(key)) m_left.Add(key);
            }
            foreach (var key in m_left) m_riders.Remove(key);
        }

        private void Update()
        {
            m_time += Time.deltaTime;
            UpdateRings();
        }

        // 光の輪: 加速床は中心から外へ広がり、減速床は外から中心へ縮む。端に近いほど薄い
        private void UpdateRings()
        {
            if (m_rings == null || m_rings.Length == 0) return;
            float maxRadius = Mathf.Min(m_size.x, m_size.y) * 0.5f;
            float cycle = Mathf.Max(0.05f, m_ringCycle);
            for (int i = 0; i < m_rings.Length; i++)
            {
                var r = m_rings[i];
                if (r == null) continue;
                float t = Mathf.Repeat(m_time / cycle + (float)i / m_rings.Length, 1.0f);
                if (m_mode == Mode.Decelerate) t = 1.0f - t;
                float radius = Mathf.Lerp(0.1f, maxRadius, t);
                r.transform.localScale = new Vector3(radius, 1.0f, radius);
                float alpha = Mathf.Sin(t * Mathf.PI) * 0.9f;
                EffectMeshes.SetColor(r, GimmickPalette.WithAlpha(EffectMeshes.Brighten(MainColor, 0.3f), alpha));
            }
        }

        // 大きさ・色を合わせる(エディタから設定を変えたときにも呼ぶ)
        public void ApplyShape()
        {
            if (m_base != null)
            {
                m_base.transform.localScale = new Vector3(m_size.x, 1.0f, m_size.y);
                m_base.transform.localPosition = Vector3.up * m_surfaceOffset;
                EffectMeshes.SetColor(m_base, GimmickPalette.WithAlpha(MainColor, m_baseAlpha));
            }
            if (m_rings == null) return;
            float radius = Mathf.Min(m_size.x, m_size.y) * 0.5f;
            for (int i = 0; i < m_rings.Length; i++)
            {
                if (m_rings[i] == null) continue;
                m_rings[i].transform.localPosition = Vector3.up * (m_surfaceOffset * 1.5f + i * 0.001f);
                m_rings[i].transform.localScale = new Vector3(radius, 1.0f, radius);
                EffectMeshes.SetColor(m_rings[i], GimmickPalette.WithAlpha(MainColor, 0.5f));
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = MainColor;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0, m_rideHeight * 0.5f, 0), new Vector3(m_size.x, m_rideHeight, m_size.y));
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
