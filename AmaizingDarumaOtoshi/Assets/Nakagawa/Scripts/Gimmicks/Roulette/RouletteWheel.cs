using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // ルーレット盤の回る部分(Stage20)。プレイヤーが戦う床そのもの
    //  ・中心から: 軸(柱。別の動かない当たり判定) / 木の盤面 / 外周のポケット(番号付き)
    //  ・回転演出のときだけ回る(動かない Rigidbody で回し、乗っているプレイヤー・飛んでいる積み木は回転台と同じように運ぶ)
    //  ・ポケットは奈落にできる。奈落になったポケットは床(当たり判定)と見た目が消え、落ちると脱落する
    //  ・当たり判定は1枚のメッシュ(床どうしの継ぎ目がない)。奈落が増えるたびに作り直す
    // このTransformの位置が床の上面の中心
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(Rigidbody), typeof(MeshCollider))]
    public class RouletteWheel : MonoBehaviour, ITumikiReflectFilter
    {
        public enum PocketKind { Green, Red, Black }

        [Header("形")]
        [SerializeField, Min(3)] int m_pocketCount = 17;
        [Tooltip("ポケットの番号(並び順。0 番目は緑の0)。足りなければ並び順の番号")]
        [SerializeField] int[] m_numbers;
        [SerializeField] float m_spindleRadius = 1.6f;
        [Tooltip("ポケットの内側・外側の半径(外側が回る部分の端)")]
        [SerializeField] float m_pocketInner = 9.4f;
        [SerializeField] float m_pocketOuter = 12.6f;
        [SerializeField] float m_thickness = 0.6f;
        [Tooltip("ポケット1つあたりの当たり判定の分割数")]
        [SerializeField, Min(1)] int m_subdivisions = 4;

        [Header("回転")]
        [Tooltip("表面からこの高さまでにいるものを乗っているとみなす")]
        [SerializeField] float m_rideHeight = 1.2f;

        [Header("見た目")]
        [Tooltip("ポケットごとの見た目(奈落になったら隠す)。並び順はポケットの番号順")]
        [SerializeField] GameObject[] m_pocketVisuals;
        [SerializeField] Color m_warningColor = new Color(1.0f, 0.25f, 0.15f, 1.0f);
        [SerializeField] Color m_abyssGlowColor = new Color(0.9f, 0.1f, 0.05f, 0.35f);
        [SerializeField] GimmickBurstSettings m_crumbleEffect = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(0.25f, 0.1f, 0.08f, 1.0f), ColorB = new Color(0.6f, 0.45f, 0.3f, 1.0f),
            Count = 40, Speed = new Vector2(1.0f, 4.0f), Size = new Vector2(0.12f, 0.35f), Lifetime = new Vector2(0.5f, 1.0f),
            Gravity = 1.5f, Shape = GimmickBurstShape.BoxEdge, ShapeSize = new Vector3(3.5f, 0, 2.8f),
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.3f, 0.15f, 0.8f), ShockwaveRadius = 3.0f, ShockwaveTime = 0.35f,
        };

        Rigidbody m_rb;
        MeshCollider m_collider;
        Mesh m_colliderMesh;
        Quaternion m_baseRotation;
        float m_angle;
        float m_speed;
        float m_targetSpeed;
        float m_accel = 30.0f;
        bool[] m_abyss;
        float[] m_warning;   // 奈落になるまでの残り時間(0 以下なら予告なし)
        MeshRenderer[] m_overlays;
        Mesh m_overlayMesh;
        readonly List<IGimmickAffectable> m_targets = new List<IGimmickAffectable>();

        // ポケットが奈落になった(ポケットの番号順の位置)
        public event Action<int> AbyssOpened;
        // 回転が止まった
        public event Action Stopped;

        public int PocketCount => m_pocketCount;
        public float PocketWidth => 360.0f / m_pocketCount;
        public float PocketInner => m_pocketInner;
        public float PocketOuter => m_pocketOuter;
        public float PocketMidRadius => (m_pocketInner + m_pocketOuter) * 0.5f;
        public float SpindleRadius => m_spindleRadius;
        public Vector3 Center => m_rb != null ? m_rb.position : transform.position;
        // 回った角度(度。上から見て時計回りが正)
        public float Angle => m_angle;
        // 今の回転の速さ(度/秒)
        public float Speed => m_speed;
        public bool IsSpinning => Mathf.Abs(m_speed) > 0.01f || Mathf.Abs(m_targetSpeed) > 0.01f;
        public int AbyssCount
        {
            get
            {
                int n = 0;
                if (m_abyss != null) foreach (bool b in m_abyss) if (b) n++;
                return n;
            }
        }

        private void Awake()
        {
            m_rb = GetComponent<Rigidbody>();
            m_rb.isKinematic = true;
            m_rb.interpolation = RigidbodyInterpolation.Interpolate;
            m_collider = GetComponent<MeshCollider>();
            m_collider.convex = false;
            m_baseRotation = transform.rotation;
            m_abyss = new bool[m_pocketCount];
            m_warning = new float[m_pocketCount];
            BuildOverlays();
            RebuildCollider();
        }

        // すべてのポケットを床に戻し、回転を止めて最初の向きにする
        public void ResetBoard()
        {
            if (m_abyss == null) return;
            for (int i = 0; i < m_pocketCount; i++)
            {
                m_abyss[i] = false;
                m_warning[i] = 0.0f;
                if (m_pocketVisuals != null && i < m_pocketVisuals.Length && m_pocketVisuals[i] != null) m_pocketVisuals[i].SetActive(true);
                if (m_overlays[i] != null) m_overlays[i].enabled = false;
            }
            m_speed = 0.0f;
            m_targetSpeed = 0.0f;
            m_angle = 0.0f;
            m_rb.rotation = m_baseRotation;
            transform.rotation = m_baseRotation;
            RebuildCollider();
            CpuNavMesh.MarkDirty();
        }

        //========================================
        // 回転
        //========================================

        // 回転の速さを target(度/秒)へ、accel(度/秒²)で近づける
        public void SetTargetSpeed(float target, float accel)
        {
            m_targetSpeed = target;
            m_accel = Mathf.Max(0.01f, accel);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (GimmickClock.IsHeld || GimmickClock.IsNetClient) return;

            UpdateWarnings(dt);

            bool wasMoving = Mathf.Abs(m_speed) > 0.0001f;
            m_speed = Mathf.MoveTowards(m_speed, m_targetSpeed, m_accel * dt);
            if (Mathf.Abs(m_speed) <= 0.0001f)
            {
                m_speed = 0.0f;
                if (wasMoving && Mathf.Abs(m_targetSpeed) <= 0.0001f)
                {
                    // 止まったら CPU の道を作り直す(穴の位置が変わったので)
                    CpuNavMesh.MarkDirty();
                    Stopped?.Invoke();
                }
                return;
            }
            float delta = m_speed * dt;
            m_angle = Mathf.Repeat(m_angle + delta, 360.0f);
            m_rb.MoveRotation(m_baseRotation * Quaternion.Euler(0.0f, m_angle, 0.0f));
            Carry(delta, dt);
        }

        // 乗っているもの(落ちている積み木は物理の摩擦で一緒に動くので除く)を、このステップで回る分だけ運ぶ
        void Carry(float deltaAngle, float dt)
        {
            Vector3 center = m_rb.position;
            Vector3 half = new Vector3(m_pocketOuter, m_rideHeight * 0.5f, m_pocketOuter);
            GimmickUtil.OverlapBox(center + Vector3.up * (m_rideHeight * 0.5f), half, Quaternion.identity, m_targets);
            Quaternion step = Quaternion.Euler(0.0f, deltaAngle, 0.0f);
            foreach (var target in m_targets)
            {
                if (GimmickUtil.MovesWithSolidPlatform(target)) continue;
                Vector3 offset = target.Rigidbody.position - center;
                offset.y = 0.0f;
                if (offset.sqrMagnitude > m_pocketOuter * m_pocketOuter) continue;
                // 穴の上(奈落のポケット)にいるものは運ばない
                int pocket = PocketAtWorld(target.Rigidbody.position, out float r);
                if (r >= m_pocketInner && pocket >= 0 && m_abyss[pocket]) continue;
                target.AddCarryVelocity((step * offset - offset) / dt);
                target.AddCarryRotation(deltaAngle);
            }
        }

        //========================================
        // 位置・ポケット・エリア
        //========================================

        // 盤の角度(盤に固定した角度)と半径の点をワールドへ
        public Vector3 LocalPoint(float radius, float localDeg, float y = 0.0f)
        {
            return Center + Quaternion.Euler(0.0f, m_angle, 0.0f) * RouletteMeshes.Point(radius, localDeg, y);
        }

        // ワールドの点の、盤に固定した角度(0〜360)と半径
        public float LocalAngleOf(Vector3 world, out float radius)
        {
            Vector3 d = world - Center;
            d.y = 0.0f;
            radius = d.magnitude;
            float deg = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            return Mathf.Repeat(deg - m_angle, 360.0f);
        }

        // ポケット i の中心の、盤に固定した角度
        public float PocketLocalAngle(int i) => i * PocketWidth;

        public Vector3 PocketWorldCenter(int i, float y = 0.0f) => LocalPoint(PocketMidRadius, PocketLocalAngle(i), y);

        // 盤に固定した角度に一番近いポケット
        public int PocketAtLocalAngle(float localDeg)
        {
            return Mod(Mathf.RoundToInt(Mathf.Repeat(localDeg, 360.0f) / PocketWidth), m_pocketCount);
        }

        // ワールドの点がどのポケットの角度にあるか(半径は r に返す。ポケットの範囲かは呼ぶ側で r を見る)
        public int PocketAtWorld(Vector3 world, out float r)
        {
            float a = LocalAngleOf(world, out r);
            return PocketAtLocalAngle(a);
        }

        public PocketKind Kind(int i)
        {
            if (i == 0) return PocketKind.Green;
            return i % 2 == 1 ? PocketKind.Red : PocketKind.Black;
        }

        public int Number(int i)
        {
            if (m_numbers != null && i < m_numbers.Length) return m_numbers[i];
            return i;
        }

        public bool IsAbyss(int i) => m_abyss != null && i >= 0 && i < m_pocketCount && m_abyss[i];
        public bool IsWarning(int i) => m_warning != null && i >= 0 && i < m_pocketCount && m_warning[i] > 0.0f;

        //========================================
        // 奈落
        //========================================

        // ポケット i を、warning 秒の予告(赤く点滅)の後に奈落にする。既に奈落・予告中なら false
        public bool BeginAbyss(int i, float warning)
        {
            if (i < 0 || i >= m_pocketCount || m_abyss[i] || m_warning[i] > 0.0f) return false;
            if (warning <= 0.0f)
            {
                OpenAbyss(i);
                return true;
            }
            m_warning[i] = warning;
            if (m_overlays[i] != null) m_overlays[i].enabled = true;
            return true;
        }

        void UpdateWarnings(float dt)
        {
            for (int i = 0; i < m_pocketCount; i++)
            {
                if (m_warning[i] <= 0.0f) continue;
                m_warning[i] -= dt;
                if (m_warning[i] <= 0.0f)
                {
                    m_warning[i] = 0.0f;
                    OpenAbyss(i);
                }
            }
        }

        void OpenAbyss(int i)
        {
            m_abyss[i] = true;
            if (m_pocketVisuals != null && i < m_pocketVisuals.Length && m_pocketVisuals[i] != null) m_pocketVisuals[i].SetActive(false);
            RebuildCollider();
            CpuNavMesh.MarkDirty();
            Vector3 c = PocketWorldCenter(i, 0.05f);
            var fx = m_crumbleEffect.Clone();
            GimmickBurst.Play(fx, c, Quaternion.Euler(0.0f, m_angle + PocketLocalAngle(i), 0.0f));
            GimmickSound.Play(GimmickSound.Rumble, 0.5f, 1.4f);
            GimmickSound.Play(BilliardFx.Drop, 0.5f, 0.7f);
            BilliardFx.Shake(0.3f, 0.35f);
            AbyssOpened?.Invoke(i);
        }

        private void Update()
        {
            // 予告中: ポケットが赤く点滅(残りが短いほど速く) / 奈落: 穴の底がうっすら赤く光る
            if (m_overlays == null) return;
            for (int i = 0; i < m_pocketCount; i++)
            {
                var r = m_overlays[i];
                if (r == null) continue;
                if (m_warning[i] > 0.0f)
                {
                    r.enabled = true;
                    r.transform.localPosition = new Vector3(0.0f, 0.03f, 0.0f);
                    float blink = GimmickPalette.Blink(Time.time, Mathf.Lerp(10.0f, 3.0f, Mathf.Clamp01(m_warning[i] / 2.0f)));
                    EffectMeshes.SetColor(r, GimmickPalette.WithAlpha(m_warningColor, Mathf.Lerp(0.25f, 0.85f, blink)));
                }
                else if (m_abyss[i])
                {
                    r.enabled = true;
                    r.transform.localPosition = new Vector3(0.0f, -m_thickness - 0.05f, 0.0f);
                    float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 3.0f + i);
                    EffectMeshes.SetColor(r, GimmickPalette.WithAlpha(m_abyssGlowColor, m_abyssGlowColor.a * pulse));
                }
                else r.enabled = false;
            }
        }

        // ポケットの形の光(予告・奈落の印)を、ポケットごとに作る
        void BuildOverlays()
        {
            float w = PocketWidth;
            m_overlayMesh = RouletteMeshes.Sector(m_pocketInner, m_pocketOuter, -w * 0.5f, w * 0.5f, 0.0f, 0.0f, false, false, false, false);
            m_overlays = new MeshRenderer[m_pocketCount];
            for (int i = 0; i < m_pocketCount; i++)
            {
                var pivot = new GameObject($"PocketOverlay_{i}").transform;
                pivot.SetParent(transform, false);
                pivot.localRotation = Quaternion.Euler(0.0f, PocketLocalAngle(i), 0.0f);
                var r = EffectMeshes.Create("Glow", pivot, m_overlayMesh, true);
                r.enabled = false;
                m_overlays[i] = r;
            }
        }

        //========================================
        // 当たり判定
        //========================================

        // 床の当たり判定(1枚のメッシュ)を作り直す。奈落のポケットは抜き、穴の縁には側面を付ける
        // 外周(動かない外のレールとの境目)には側面を付けない(継ぎ目に引っかからないように)
        public void RebuildCollider()
        {
            if (m_abyss == null) return;
            var b = new RouletteMeshes.Builder();
            float top = 0.0f, bottom = -m_thickness;
            float w = PocketWidth;
            int sub = Mathf.Max(1, m_subdivisions);
            float cell = w / sub;
            float[] rings = { 0.0f, m_pocketInner * 0.35f, m_pocketInner * 0.7f, m_pocketInner };
            for (int p = 0; p < m_pocketCount; p++)
            {
                float a0 = PocketLocalAngle(p) - w * 0.5f;
                for (int s = 0; s < sub; s++)
                {
                    float c0 = a0 + cell * s, c1 = c0 + cell;
                    // 内側(いつも床)
                    for (int k = 0; k < rings.Length - 1; k++) b.Cell(rings[k], rings[k + 1], c0, c1, top, bottom);
                    if (m_abyss[p])
                    {
                        // 穴の内側の縁
                        b.ArcSide(m_pocketInner, c0, c1, top, bottom, true);
                    }
                    else
                    {
                        b.Cell(m_pocketInner, m_pocketOuter, c0, c1, top, bottom);
                    }
                }
                if (m_abyss[p]) continue;
                // 隣が穴なら、その境目に側面
                float start = a0, end = a0 + w;
                if (m_abyss[Mod(p - 1, m_pocketCount)]) b.RadialSide(m_pocketInner, m_pocketOuter, start, top, bottom, false);
                if (m_abyss[Mod(p + 1, m_pocketCount)]) b.RadialSide(m_pocketInner, m_pocketOuter, end, top, bottom, true);
            }
            if (m_colliderMesh == null) m_colliderMesh = new Mesh();
            b.ToMesh("RouletteWheelCollider", m_colliderMesh);
            m_collider.sharedMesh = null;
            m_collider.sharedMesh = m_colliderMesh;
        }

        public bool ShouldReflect(TumikiBlock block, Collider collider, ContactPoint contact)
        {
            return TumikiReflectRules.ShouldReflectAtSide(block, collider, contact);
        }

        static int Mod(int a, int n) => ((a % n) + n) % n;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1.0f, 0.8f, 0.2f, 0.8f);
            Vector3 c = transform.position + Vector3.up * 0.05f;
            foreach (float r in new[] { m_spindleRadius, m_pocketInner, m_pocketOuter })
            {
                for (int i = 0; i < 64; i++)
                {
                    Gizmos.DrawLine(c + RouletteMeshes.Point(r, i * 360.0f / 64), c + RouletteMeshes.Point(r, (i + 1) * 360.0f / 64));
                }
            }
        }
    }
}
