using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 加速床 / 減速床。乗っている間、今の向きのまま速さだけを変える
    //  加速床(オレンジ): 速さを上限まで上げる。乗った瞬間に一瞬スローがかかる。止まっているものは向いている方向へ動き出す
    //                    45度回したひし形が、黄色と加速床の色で交互に中央から広がり続け、乗っているものからオレンジの火花を出す
    //  減速床(青緑)    : 速さを1秒あたり一定の割合で割って下限まで下げる
    //                    上下にギザギザの光の輪が上から床へ降りてきて、乗っているものから小さな泡を出す
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
        // 加速床は濃く明るいオレンジ(床の下地は通常の半透明で塗り、明るい床の上でも色が抜けないようにする)
        [SerializeField] Color m_accelerateColor = new Color(1.0f, 0.42f, 0.0f, 1.0f);
        [SerializeField] Color m_decelerateColor = GimmickPalette.Decelerate;
        // 下地の不透明度(減速床)
        [SerializeField, Range(0.0f, 1.0f)] float m_baseAlpha = 0.6f;
        // 下地の不透明度(加速床)
        [SerializeField, Range(0.0f, 1.0f)] float m_accelerateBaseAlpha = 0.9f;
        // 輪が下から上まで(減速床は上から下まで)動く時間
        [SerializeField] float m_ringCycle = 1.2f;
        // 輪が上下に動く高さ。加速床は床からこの高さまで昇り、減速床はこの高さから床へ降りる(輪の大きさは変えない)
        [SerializeField, Min(0.0f)] float m_ringRiseHeight = 0.8f;
        // 輪の半径(床の短い辺の半分に対する割合)
        [SerializeField, Range(0.1f, 1.0f)] float m_ringRadiusRate = 0.85f;
        // 輪のギザギザ: 山の数と、上下の振れ幅(m)
        [SerializeField, Min(3)] int m_ringTeeth = 10;
        [SerializeField, Min(0.0f)] float m_ringZigzagHeight = 0.12f;
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

        [Header("加速床の模様")]
        // 45度回したひし形(正方形)を中央から広げる。頂点が床の辺に触れたら、次のひし形を中央から広げ始める(色は交互)
        // 広がりきったひし形は床の形で切り取る。加速床では上の光の輪は出さない
        [SerializeField] Color m_patternColorA = new Color(1.0f, 0.88f, 0.1f, 1.0f);   // 黄色
        // 次の色(未設定=透明なら加速床の色)
        [SerializeField] Color m_patternColorB = new Color(0.0f, 0.0f, 0.0f, 0.0f);
        // 1つのひし形が中央から床の辺に触れるまでの時間(秒)
        [SerializeField, Min(0.05f)] float m_patternCycle = 0.5f;
        [SerializeField, Range(0.0f, 1.0f)] float m_patternAlpha = 1.0f;

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
            UseZigzagRings();
            CreatePattern();
            ApplyShape();
        }

        // ---------------- 加速床の模様(ひし形) ----------------
        MeshRenderer m_pattern;
        Mesh m_patternMesh;
        readonly List<Vector3> m_pVerts = new List<Vector3>();
        readonly List<Color> m_pColors = new List<Color>();
        readonly List<int> m_pTris = new List<int>();
        readonly List<Vector2> m_poly = new List<Vector2>();
        readonly List<Vector2> m_polyTmp = new List<Vector2>();

        bool UsesPattern => m_mode == Mode.Accelerate && Application.isPlaying;

        // 実行中だけ模様を作り、加速床の光の輪は消す(プレハブ・シーンは変えない)
        private void CreatePattern()
        {
            if (!UsesPattern) return;
            if (m_rings != null)
            {
                foreach (var r in m_rings) if (r != null) r.enabled = false;
            }
            m_patternMesh = new Mesh { name = "SpeedFloorPattern", hideFlags = HideFlags.DontSave };
            m_patternMesh.MarkDynamic();
            m_pattern = EffectMeshes.Create("Pattern", transform, m_patternMesh, false);
            m_pattern.transform.localPosition = Vector3.up * (m_surfaceOffset * 2.0f);
        }

        private void OnDestroy()
        {
            if (m_patternMesh != null) Destroy(m_patternMesh);
        }

        // ひし形 k は k * 周期 に中央で生まれ、一定の速さで広がる(周期の時間で頂点が床の短い方の辺に触れる)
        // 床を覆い切ったひし形より下(古いもの)は見えないので描かない。古い順に描き、新しいものが上に重なる
        private void UpdatePattern()
        {
            if (m_pattern == null) return;
            float hx = m_size.x * 0.5f, hz = m_size.y * 0.5f;
            float touch = Mathf.Min(hx, hz);
            float cycle = Mathf.Max(0.05f, m_patternCycle);
            float speed = touch / cycle;
            // 床をすべて覆うのに必要な広さ(ひし形の |x| + |z| が角に届く)
            float coverAge = (hx + hz) / speed;
            int newest = Mathf.FloorToInt(m_time / cycle);
            int oldest = Mathf.Max(0, Mathf.FloorToInt((m_time - coverAge) / cycle));

            Color a = m_patternColorA;
            Color b = m_patternColorB.a > 0.0f ? m_patternColorB : m_accelerateColor;
            // 重なった下の色が透けて濁らないよう、不透明度はそろえる(既定は不透明)
            a.a = b.a = m_patternAlpha;
            // 頂点の色はリニア色空間では変換されないので、インスペクターの色と同じに見えるよう変換しておく
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                a = a.linear;
                b = b.linear;
            }

            m_pVerts.Clear();
            m_pColors.Clear();
            m_pTris.Clear();
            for (int k = oldest; k <= newest; k++)
            {
                float d = speed * (m_time - k * cycle);
                if (d <= 0.0f) continue;
                BuildClippedDiamond(d, hx, hz);
                AddPolygon(k % 2 == 0 ? a : b);
            }
            m_patternMesh.Clear();
            m_patternMesh.SetVertices(m_pVerts);
            m_patternMesh.SetColors(m_pColors);
            m_patternMesh.SetTriangles(m_pTris, 0);
            m_patternMesh.RecalculateBounds();
            EffectMeshes.SetColor(m_pattern, Color.white);
        }

        // 半径 d のひし形(|x| + |z| <= d)を床の四角(|x| <= hx, |z| <= hz)で切り取った形を m_poly に入れる
        private void BuildClippedDiamond(float d, float hx, float hz)
        {
            m_poly.Clear();
            m_poly.Add(new Vector2(d, 0.0f));
            m_poly.Add(new Vector2(0.0f, d));
            m_poly.Add(new Vector2(-d, 0.0f));
            m_poly.Add(new Vector2(0.0f, -d));
            Clip(new Vector2(1, 0), hx);
            Clip(new Vector2(-1, 0), hx);
            Clip(new Vector2(0, 1), hz);
            Clip(new Vector2(0, -1), hz);
        }

        // m_poly を「dot(p, n) <= limit」の側だけ残して切る
        private void Clip(Vector2 n, float limit)
        {
            m_polyTmp.Clear();
            int count = m_poly.Count;
            for (int i = 0; i < count; i++)
            {
                Vector2 p = m_poly[i], q = m_poly[(i + 1) % count];
                float dp = Vector2.Dot(p, n) - limit, dq = Vector2.Dot(q, n) - limit;
                if (dp <= 0.0f) m_polyTmp.Add(p);
                if ((dp < 0.0f && dq > 0.0f) || (dp > 0.0f && dq < 0.0f)) m_polyTmp.Add(Vector2.Lerp(p, q, dp / (dp - dq)));
            }
            m_poly.Clear();
            m_poly.AddRange(m_polyTmp);
        }

        // 凸な多角形を扇形の三角形で足す(上から見て表になる向き)
        private void AddPolygon(Color color)
        {
            if (m_poly.Count < 3) return;
            int start = m_pVerts.Count;
            foreach (var p in m_poly)
            {
                m_pVerts.Add(new Vector3(p.x, 0.0f, p.y));
                m_pColors.Add(color);
            }
            for (int i = 1; i < m_poly.Count - 1; i++)
            {
                m_pTris.Add(start);
                m_pTris.Add(start + i + 1);
                m_pTris.Add(start + i);
            }
        }

        // 輪の形を上下にギザギザの輪にする(実行中だけ。プレハブ・シーンの形は変えない)
        private void UseZigzagRings()
        {
            if (!Application.isPlaying || m_rings == null) return;
            Mesh mesh = EffectMeshes.ZigzagRing(m_ringTeeth, m_ringZigzagHeight);
            foreach (var r in m_rings)
            {
                if (r != null && r.TryGetComponent(out MeshFilter mf)) mf.sharedMesh = mesh;
            }
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
            if (GimmickClock.IsHeld) return;
            float dt = Time.fixedDeltaTime;
            Vector3 half = new Vector3(m_size.x * 0.5f, m_rideHeight * 0.5f, m_size.y * 0.5f);
            // ネット対戦用(suehara): クライアントは、乗っている間の粒だけを出す(速さはホストが変え、乗った瞬間のエフェクトはホストから届く)
            if (GimmickClock.IsNetClient)
            {
                GimmickUtil.OverlapBoxForView(transform.position + Vector3.up * (m_rideHeight * 0.5f), half, transform.rotation, m_found);
                m_effects.Begin();
                foreach (var target in m_found)
                {
                    if (target is TumikiBlock ? m_affectBlocks : m_affectPlayers) m_effects.Touch(target);
                }
                m_effects.End();
                return;
            }
            using var relay = GimmickNet.Relay();   // ネット対戦用(suehara): 乗った瞬間のエフェクトをクライアントにも出す
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
            if (!GimmickClock.IsHeld) m_time += Time.deltaTime;
            if (UsesPattern) UpdatePattern();
            else UpdateRings();
        }

        // 光の輪: 同じ大きさのまま、加速床は床から昇り、減速床は上から床へ降りる。上下の端に近いほど薄い
        private void UpdateRings()
        {
            if (m_rings == null || m_rings.Length == 0) return;
            float radius = RingRadius;
            float cycle = Mathf.Max(0.05f, m_ringCycle);
            for (int i = 0; i < m_rings.Length; i++)
            {
                var r = m_rings[i];
                if (r == null) continue;
                float t = Mathf.Repeat(m_time / cycle + (float)i / m_rings.Length, 1.0f);
                if (m_mode == Mode.Decelerate) t = 1.0f - t;
                r.transform.localScale = new Vector3(radius, 1.0f, radius);
                // 加速床は t が 0→1 で昇り、減速床は t が 1→0 で降りる
                r.transform.localPosition = Vector3.up * (RingBaseHeight(i) + m_ringRiseHeight * t);
                float alpha = Mathf.Sin(t * Mathf.PI) * 0.9f;
                EffectMeshes.SetColor(r, GimmickPalette.WithAlpha(EffectMeshes.Brighten(MainColor, 0.3f), alpha));
            }
        }

        // 輪の床からの高さ(上下に動く前。ギザギザの谷が床に埋まらない高さ)
        private float RingBaseHeight(int i) => m_surfaceOffset * 1.5f + i * 0.001f + (Application.isPlaying ? m_ringZigzagHeight : 0.0f);
        private float RingRadius => Mathf.Min(m_size.x, m_size.y) * 0.5f * m_ringRadiusRate;

        // 大きさ・色を合わせる(エディタから設定を変えたときにも呼ぶ)
        public void ApplyShape()
        {
            if (m_base != null)
            {
                m_base.transform.localScale = new Vector3(m_size.x, 1.0f, m_size.y);
                m_base.transform.localPosition = Vector3.up * m_surfaceOffset;
                float alpha = m_mode == Mode.Accelerate ? m_accelerateBaseAlpha : m_baseAlpha;
                EffectMeshes.SetColor(m_base, GimmickPalette.WithAlpha(MainColor, alpha));
            }
            if (m_rings == null) return;
            float radius = RingRadius;
            for (int i = 0; i < m_rings.Length; i++)
            {
                if (m_rings[i] == null) continue;
                m_rings[i].transform.localPosition = Vector3.up * RingBaseHeight(i);
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
