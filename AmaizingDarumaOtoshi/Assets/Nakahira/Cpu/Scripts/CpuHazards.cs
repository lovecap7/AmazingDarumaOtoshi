using System.Collections.Generic;
using UnityEngine;
using Yurube;

namespace Nakahira
{
    // 危ない場所の種類(どれも必殺技が作るもの)
    public enum CpuHazardKind
    {
        Meteor,     // いわだるの隕石の予告円(入っていると即死)
        Swamp,      // へびだるの毒沼(入ると遅くなり、移動が反転する。出ようとはするが反転は直さないので、中心へ向かってしまう)
        Beam,       // あかだるのトロンのビーム(触れると1段減る。0段なら即死)
        Sword,      // だるきんぐのエクスカリバーの予定の線(詠唱が終わると線の上は即死)
    }

    // 円の危ない場所(隕石の予告円・毒沼)
    public struct CpuHazardCircle
    {
        public Object Source;
        public CpuHazardKind Kind;
        public DarumaCharacter Owner;   // 出した人(この人には効かない)
        public Vector3 Center;
        public float Radius;
        public float TimeToImpact;      // 隕石が落ちるまでの見込み(秒。毒沼は使わない)
    }

    // 線の危ない場所(ビームの当たり判定の箱1つ分・エクスカリバーの予定の線)
    public struct CpuHazardSegment
    {
        public Object Source;
        public CpuHazardKind Kind;
        public DarumaCharacter Owner;   // 出した人(エクスカリバーはこの人には当たらない。ビームは自分にも当たる)
        public Vector3 A;
        public Vector3 B;
        public float HalfWidth;
    }

    // 盤面にある危ない場所(全部の CPU で共通。1フレームに1回まとめる)
    public static class CpuHazardSources
    {
        // 必殺技のコンポーネントを探し直す間隔(秒。位置は毎フレーム読む)
        const float kScanInterval = 0.1f;

        static readonly List<IwadaruSpecial> s_meteors = new List<IwadaruSpecial>();
        static readonly List<HebiPoisonSwamp> s_swamps = new List<HebiPoisonSwamp>();
        static readonly List<TronProjectile> s_beams = new List<TronProjectile>();
        static readonly List<DaruKingSpecial> s_swords = new List<DaruKingSpecial>();
        static readonly List<CpuHazardCircle> s_circles = new List<CpuHazardCircle>();
        static readonly List<CpuHazardSegment> s_segments = new List<CpuHazardSegment>();
        static readonly List<(Vector3 a, Vector3 b, float width)> s_boxes = new List<(Vector3 a, Vector3 b, float width)>();
        static int s_frame = -1;
        static float s_nextScan;

        public static IReadOnlyList<CpuHazardCircle> Circles => s_circles;
        public static IReadOnlyList<CpuHazardSegment> Segments => s_segments;

        // ドメインの再読み込みをしない設定でも、再生のたびに作り直す
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_frame = -1;
            s_nextScan = 0.0f;
            s_meteors.Clear();
            s_swamps.Clear();
            s_beams.Clear();
            s_swords.Clear();
            s_circles.Clear();
            s_segments.Clear();
        }

        public static void Refresh()
        {
            if (s_frame == Time.frameCount) return;
            s_frame = Time.frameCount;

            if (Time.unscaledTime >= s_nextScan)
            {
                s_nextScan = Time.unscaledTime + kScanInterval;
                s_meteors.Clear();
                s_meteors.AddRange(Object.FindObjectsByType<IwadaruSpecial>(FindObjectsInactive.Exclude));
                s_swamps.Clear();
                s_swamps.AddRange(Object.FindObjectsByType<HebiPoisonSwamp>(FindObjectsInactive.Exclude));
                s_beams.Clear();
                s_beams.AddRange(Object.FindObjectsByType<TronProjectile>(FindObjectsInactive.Exclude));
                s_swords.Clear();
                s_swords.AddRange(Object.FindObjectsByType<DaruKingSpecial>(FindObjectsInactive.Exclude));
            }

            s_circles.Clear();
            foreach (var meteor in s_meteors)
            {
                if (meteor == null || !meteor.IsThreatening) continue;
                s_circles.Add(new CpuHazardCircle
                {
                    Source = meteor,
                    Kind = CpuHazardKind.Meteor,
                    Owner = meteor.Caster,
                    Center = meteor.DebugCenter,
                    Radius = meteor.AttackRadius,
                    TimeToImpact = meteor.TimeToImpact,
                });
            }
            foreach (var swamp in s_swamps)
            {
                if (swamp == null || !swamp.IsEffective) continue;
                s_circles.Add(new CpuHazardCircle
                {
                    Source = swamp,
                    Kind = CpuHazardKind.Swamp,
                    Owner = swamp.Owner,
                    Center = swamp.transform.position,
                    Radius = swamp.Radius,
                });
            }

            s_segments.Clear();
            foreach (var beam in s_beams)
            {
                if (beam == null || !beam.IsHitActive) continue;
                s_boxes.Clear();
                beam.GetHitSegments(s_boxes);
                foreach (var box in s_boxes)
                {
                    s_segments.Add(new CpuHazardSegment
                    {
                        Source = beam,
                        Kind = CpuHazardKind.Beam,
                        Owner = beam.Owner,
                        A = box.a,
                        B = box.b,
                        HalfWidth = box.width * 0.5f,
                    });
                }
            }
            foreach (var sword in s_swords)
            {
                if (sword == null || !sword.IsThreatening || sword.Caster == null) continue;
                sword.GetAttackLine(out Vector3 start, out Vector3 end);
                s_segments.Add(new CpuHazardSegment
                {
                    Source = sword,
                    Kind = CpuHazardKind.Sword,
                    Owner = sword.Caster,
                    A = start,
                    B = end,
                    HalfWidth = sword.AttackWidth * 0.5f,
                });
            }
        }
    }

    // CPU 1人が知っている危ない場所。出てから HazardReaction 秒たって気づいたものだけ
    // 自分が出した隕石・毒沼は自分には効かないので含めない(自分のビームは自分にも当たるので含める)
    public class CpuHazards
    {
        // 高さがこれ(m)以上ずれている危ない場所は、別の段の床にあるとみなして気にしない
        const float kMaxHeightGap = 2.5f;

        readonly Dictionary<Object, float> m_firstSeen = new Dictionary<Object, float>();
        readonly List<Object> m_stale = new List<Object>();
        readonly HashSet<Object> m_present = new HashSet<Object>();
        readonly List<CpuHazardCircle> m_circles = new List<CpuHazardCircle>();
        readonly List<CpuHazardSegment> m_segments = new List<CpuHazardSegment>();
        CpuContext m_context;

        public IReadOnlyList<CpuHazardCircle> Circles => m_circles;
        public IReadOnlyList<CpuHazardSegment> Segments => m_segments;
        public bool Any => m_circles.Count > 0 || m_segments.Count > 0;

        public void Update(CpuContext c)
        {
            m_context = c;
            CpuHazardSources.Refresh();
            float now = Time.time;
            float reaction = c.Params.HazardReaction;
            float selfY = c.Position.y;
            float golden = c.SpecialInvincibleRemaining;
            m_present.Clear();
            m_circles.Clear();
            m_segments.Clear();

            foreach (var h in CpuHazardSources.Circles)
            {
                if (h.Owner == c.Self) continue;
                if (!Notice(h.Source, now, reaction)) continue;
                if (Mathf.Abs(h.Center.y - selfY) > kMaxHeightGap) continue;
                // 必殺技で無敵の間(きだるの黄金化・いわだるの隕石)は、落ちるまでに無敵が切れない隕石は気にしない(毒沼は無敵でも効くので避ける)
                if (h.Kind == CpuHazardKind.Meteor && golden > h.TimeToImpact + c.Params.GoldenHazardMargin) continue;
                m_circles.Add(h);
            }
            foreach (var h in CpuHazardSources.Segments)
            {
                if (h.Kind == CpuHazardKind.Sword && h.Owner == c.Self) continue;
                if (!Notice(h.Source, now, reaction)) continue;
                if (Mathf.Abs(Mathf.Min(h.A.y, h.B.y) - selfY) > kMaxHeightGap) continue;
                // 必殺技で無敵の間は、ビーム・エクスカリバーを気にしない(無敵で防げる。切れそうになったら避け始める)
                if (golden > c.Params.GoldenHazardMargin) continue;
                m_segments.Add(h);
            }

            // 消えた危ない場所を忘れる(また出たら気づき直す)
            m_stale.Clear();
            foreach (var pair in m_firstSeen)
            {
                if (pair.Key == null || !m_present.Contains(pair.Key)) m_stale.Add(pair.Key);
            }
            foreach (var key in m_stale) m_firstSeen.Remove(key);
        }

        public void Clear()
        {
            m_firstSeen.Clear();
            m_circles.Clear();
            m_segments.Clear();
        }

        // 初めて見えた時刻を覚え、気づくまでの遅れがたったら true
        private bool Notice(Object source, float now, float reaction)
        {
            m_present.Add(source);
            if (!m_firstSeen.TryGetValue(source, out float first))
            {
                first = now;
                m_firstSeen.Add(source, first);
            }
            return now - first >= reaction;
        }

        // 危ない場所から離れておく距離(m)。体の半径 + HazardMargin(隕石・エクスカリバーはさらに余裕を足す)
        public float Clearance(CpuHazardKind kind)
        {
            CpuParams p = m_context != null ? m_context.Params : null;
            float margin = DarumaCharacter.kBodyRadius + (p != null ? p.HazardMargin : 0.0f);
            if (p != null && kind == CpuHazardKind.Meteor) margin += p.MeteorMargin;
            if (p != null && kind == CpuHazardKind.Sword) margin += p.SwordMargin;
            return margin;
        }

        // 入っていると即死する危ない場所か(移動床の乗り降り中でも出る)
        public static bool IsLethal(CpuHazardKind kind)
        {
            return kind == CpuHazardKind.Meteor || kind == CpuHazardKind.Sword;
        }

        // 点 p が危ない場所の中(離れておく距離 + extra を含む)か
        public bool IsInside(Vector3 p, float extra = 0.0f)
        {
            return TryGetOutward(p, extra, out _, out _);
        }

        // 点 p を含む危ない場所があれば、その外へ向かう向き(水平・正規化)を返す。複数あれば一番深く入っているもの
        public bool TryGetOutward(Vector3 p, float extra, out Vector3 outward, out CpuHazardKind kind)
        {
            outward = Vector3.zero;
            kind = CpuHazardKind.Meteor;
            float bestDepth = 0.0f;
            bool found = false;
            foreach (var h in m_circles)
            {
                Vector3 d = p - h.Center;
                d.y = 0.0f;
                float dist = d.magnitude;
                float depth = h.Radius + Clearance(h.Kind) + extra - dist;
                if (depth <= bestDepth) continue;
                bestDepth = depth;
                found = true;
                kind = h.Kind;
                outward = dist > 0.01f ? d / dist : Vector3.forward;
            }
            foreach (var h in m_segments)
            {
                Vector3 closest = ClosestOnSegment(p, h.A, h.B);
                Vector3 d = p - closest;
                d.y = 0.0f;
                float dist = d.magnitude;
                float depth = h.HalfWidth + Clearance(h.Kind) + extra - dist;
                if (depth <= bestDepth) continue;
                bestDepth = depth;
                found = true;
                kind = h.Kind;
                if (dist > 0.01f)
                {
                    outward = d / dist;
                }
                else
                {
                    // 線の真上:線の向きに垂直な向き
                    Vector3 along = h.B - h.A;
                    along.y = 0.0f;
                    outward = along.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, along.normalized) : Vector3.forward;
                }
            }
            return found;
        }

        // 線分 a-b(道の1区間など)が危ない場所を通るか
        // 自分が今その中にいる危ない場所は数えない(出るのは CpuHazardGuard が行う)
        public bool SegmentCrosses(Vector3 a, Vector3 b)
        {
            Vector3 self = m_context != null ? m_context.Position : a;
            foreach (var h in m_circles)
            {
                float limit = h.Radius + Clearance(h.Kind);
                if (CpuContext.FlatDistance(self, h.Center) < limit) continue;
                if (FlatDistanceToSegment(h.Center, a, b) < limit) return true;
            }
            foreach (var h in m_segments)
            {
                float limit = h.HalfWidth + Clearance(h.Kind);
                if (FlatDistanceToSegment(self, h.A, h.B) < limit) continue;
                if (FlatSegmentDistance(a, b, h.A, h.B) < limit) return true;
            }
            return false;
        }

        public void DrawGizmos()
        {
            Gizmos.color = new Color(1.0f, 0.3f, 0.0f);
            foreach (var h in m_circles)
            {
                const int kSteps = 32;
                float r = h.Radius + Clearance(h.Kind);
                Vector3 prev = h.Center + new Vector3(r, 0.1f, 0.0f);
                for (int i = 1; i <= kSteps; i++)
                {
                    float a = i * Mathf.PI * 2.0f / kSteps;
                    Vector3 next = h.Center + new Vector3(Mathf.Cos(a) * r, 0.1f, Mathf.Sin(a) * r);
                    Gizmos.DrawLine(prev, next);
                    prev = next;
                }
            }
            foreach (var h in m_segments) Gizmos.DrawLine(h.A, h.B);
        }

        //========================================
        // 水平の距離の計算
        //========================================

        private static Vector3 ClosestOnSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            ab.y = 0.0f;
            Vector3 ap = p - a;
            ap.y = 0.0f;
            float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude) : 0.0f;
            return a + (b - a) * t;
        }

        // 点 p から線分 a-b までの水平の距離
        public static float FlatDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            return CpuContext.FlatDistance(p, ClosestOnSegment(p, a, b));
        }

        // 2つの線分の水平の距離(交わっていれば 0)
        public static float FlatSegmentDistance(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (FlatIntersect(a, b, c, d)) return 0.0f;
            return Mathf.Min(
                Mathf.Min(FlatDistanceToSegment(a, c, d), FlatDistanceToSegment(b, c, d)),
                Mathf.Min(FlatDistanceToSegment(c, a, b), FlatDistanceToSegment(d, a, b)));
        }

        private static bool FlatIntersect(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float d1 = Cross(c, d, a);
            float d2 = Cross(c, d, b);
            float d3 = Cross(a, b, c);
            float d4 = Cross(a, b, d);
            return ((d1 > 0.0f && d2 < 0.0f) || (d1 < 0.0f && d2 > 0.0f))
                && ((d3 > 0.0f && d4 < 0.0f) || (d3 < 0.0f && d4 > 0.0f));
        }

        // 線 o→e に対して p が左右どちらにあるか(水平)
        private static float Cross(Vector3 o, Vector3 e, Vector3 p)
        {
            return (e.x - o.x) * (p.z - o.z) - (e.z - o.z) * (p.x - o.x);
        }
    }
}
