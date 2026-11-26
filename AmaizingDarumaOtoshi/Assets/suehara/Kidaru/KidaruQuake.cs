using Nakahira;
using UnityEngine;

namespace Yurube
{
    // きだるの必殺技(着地の衝撃波。suehara)で出す衝撃波
    // ・出た瞬間に、半径 Radius 以内(水平)にいる敵全員の最下段を1段削る(首だけなら脱落)。空中にいても当たる(ジャンプではよけられない)
    // ・見た目は、地面から広がって消える輪
    // 当たり判定はホストだけ(ネット対戦のクライアントには輪も出ない)
    public class KidaruQuake : MonoBehaviour
    {
        const float kExpandTime = 0.3f;
        const int kSegments = 48;
        // 段差の上下で別の階にいる相手に当てないよう、上下はこの範囲まで(m)
        const float kVerticalRange = 3.0f;
        static readonly Color kColor = new Color(1.0f, 0.85f, 0.25f, 0.95f);
        static Material s_material;

        LineRenderer m_line;
        float m_radius;
        float m_age;

        // 衝撃波を出す。当てた人数を返す
        public static int Emit(DarumaCharacter owner, float radius)
        {
            if (owner == null) return 0;
            Vector3 center = owner.transform.position;
            SpawnRing(center, radius);
            if (SpecialNet.IsClient) return 0;   // ネット対戦用(suehara): 当たり判定はホストだけ

            int hits = 0;
            DarumaMatch match = DarumaMatch.Current;
            float launchSpeed = owner.Hammer != null && owner.Hammer.Params != null ? owner.Hammer.Params.SweepLaunchSpeed : 30.0f;
            foreach (var other in FindObjectsByType<DarumaCharacter>(FindObjectsInactive.Exclude))
            {
                if (other == null || other == owner || other.IsEliminated) continue;
                if (match != null && match.IsFriendly(owner, other)) continue;
                Vector3 d = other.transform.position - center;
                if (Mathf.Abs(d.y) > kVerticalRange) continue;
                d.y = 0.0f;
                if (d.magnitude > radius + DarumaCharacter.kBodyRadius) continue;

                Vector3 dir = d.sqrMagnitude > 0.0001f ? d.normalized : owner.transform.forward;
                bool damaged = other.KnockBottom(dir, launchSpeed, owner, out _);
                if (damaged)
                {
                    owner.OnDealtDamage();
                    other.StartHitStop(0.08f, 0.1f, 0.1f);
                    hits++;
                }
                Vector3 point = other.transform.position + Vector3.up * 0.25f;
                DarumaHammer.RaiseHit(new HammerHitInfo(owner.Hammer, owner, other, point, damaged, 0));
            }
            return hits;
        }

        static void SpawnRing(Vector3 center, float radius)
        {
            if (s_material == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) return;
                s_material = new Material(shader);
            }
            var go = new GameObject("KidaruQuake");
            go.transform.position = center + Vector3.up * 0.05f;
            var quake = go.AddComponent<KidaruQuake>();
            quake.m_radius = radius;
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = s_material;
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = kSegments;
            line.alignment = LineAlignment.TransformZ;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            // 地面に寝かせる(LineRenderer の面の向き = Transform の Z)
            go.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            quake.m_line = line;
            quake.Apply(0.0f);
        }

        void Update()
        {
            m_age += Time.deltaTime;
            float t = m_age / kExpandTime;
            if (t >= 1.0f)
            {
                Destroy(gameObject);
                return;
            }
            Apply(t);
        }

        // t: 0(出た瞬間)〜1(消える)。輪が外へ広がりながら細く薄くなる
        void Apply(float t)
        {
            float ease = 1.0f - (1.0f - t) * (1.0f - t);
            float r = Mathf.Max(0.05f, m_radius * ease);
            for (int i = 0; i < kSegments; i++)
            {
                float a = 2.0f * Mathf.PI * i / kSegments;
                // 寝かせたローカルでは x と y が地面の向き
                m_line.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0.0f));
            }
            m_line.widthMultiplier = Mathf.Lerp(0.6f, 0.1f, t);
            Color c = kColor;
            c.a *= 1.0f - t;
            m_line.startColor = c;
            m_line.endColor = c;
        }
    }
}
