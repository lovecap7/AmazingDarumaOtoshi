using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // プレイヤーの足元から、向いている向きへ「撃った弾が飛ぶ道筋」を床の上に線で描くガイド(シーンに1つ置く)
    //  ・股抜きショットも、飛んできた弾の打ち返しも向いている向きへ飛ぶので、どちらの狙いにも使える
    //  ・壁(と床の側面)では弾と同じように反射した先まで描く。バンパーなど動くギミックの手前で止める
    //  ・撃てる(積み木を積んでいる)間は濃く、撃てない間・幽霊の間は薄く描く
    // プレイヤーのプレハブは編集せず、試合(DarumaMatch)に参加しているプレイヤーを毎フレーム見て線を出し入れする
    public class ShotAimGuide : MonoBehaviour
    {
        [Header("見た目")]
        [Tooltip("線のマテリアル(未設定なら Sprites/Default)")]
        [SerializeField] Material m_material;
        [SerializeField, Min(0.01f)] float m_width = 0.22f;
        [Tooltip("線を描く床からの高さ")]
        [SerializeField] float m_lineHeight = 0.06f;
        [Tooltip("撃てるときの線の濃さ")]
        [SerializeField, Range(0.0f, 1.0f)] float m_readyAlpha = 0.85f;
        [Tooltip("撃てないとき(積み木がない・幽霊)の線の濃さ")]
        [SerializeField, Range(0.0f, 1.0f)] float m_idleAlpha = 0.3f;
        [Tooltip("線の先の方を明るくする割合(先端ほど白に寄せる)")]
        [SerializeField, Range(0.0f, 1.0f)] float m_tipWhiten = 0.35f;
        [Tooltip("CPU のガイドも描くか")]
        [SerializeField] bool m_showForCpu = false;

        [Header("道筋")]
        [Tooltip("道筋の長さ(m。反射した分も含む)")]
        [SerializeField, Min(0.5f)] float m_length = 9.0f;
        [Tooltip("壁で反射した先を何回分まで描くか")]
        [SerializeField, Range(0, 4)] int m_maxBounces = 1;
        [Tooltip("弾の通り道を調べる球の高さと半径(床に触れず、壁に当たる大きさ)")]
        [SerializeField] float m_castHeight = 0.5f;
        [SerializeField, Min(0.05f)] float m_castRadius = 0.4f;
        [SerializeField] LayerMask m_mask = Physics.DefaultRaycastLayers;

        class Guide
        {
            public LineRenderer Line;
            public bool Seen;
        }

        readonly Dictionary<Transform, Guide> m_guides = new Dictionary<Transform, Guide>();
        readonly List<Vector3> m_points = new List<Vector3>();
        readonly List<Transform> m_removed = new List<Transform>();
        readonly RaycastHit[] m_hits = new RaycastHit[16];
        Material m_runtimeMaterial;

        private void LateUpdate()
        {
            foreach (var g in m_guides.Values) g.Seen = false;

            var match = DarumaMatch.Current;
            if (match != null && !match.IsFinished)
            {
                foreach (var player in match.Players)
                {
                    if (player == null || (player.IsCpu && !m_showForCpu)) continue;
                    Transform body = player.Body;
                    if (body == null || !body.gameObject.activeInHierarchy) continue;
                    bool ready = player.Character != null && player.Character.Stack.Count > 0;
                    Draw(body, player.Color, ready);
                }
            }

            // 見えなくなった(脱落・退場した)プレイヤーの線を片付ける
            m_removed.Clear();
            foreach (var pair in m_guides)
            {
                if (pair.Value.Seen) continue;
                if (pair.Value.Line != null) Destroy(pair.Value.Line.gameObject);
                m_removed.Add(pair.Key);
            }
            foreach (var key in m_removed) m_guides.Remove(key);
        }

        private void OnDisable()
        {
            foreach (var g in m_guides.Values)
            {
                if (g.Line != null) Destroy(g.Line.gameObject);
            }
            m_guides.Clear();
        }

        private void Draw(Transform body, Color color, bool ready)
        {
            if (!m_guides.TryGetValue(body, out Guide guide) || guide.Line == null)
            {
                guide = new Guide { Line = CreateLine(body.name) };
                m_guides[body] = guide;
            }
            guide.Seen = true;

            BuildPath(body);
            LineRenderer line = guide.Line;
            line.positionCount = m_points.Count;
            for (int i = 0; i < m_points.Count; i++) line.SetPosition(i, m_points[i]);

            // 根元から先へ向かって薄くなり、先端ほど白っぽく
            float a = ready ? m_readyAlpha : m_idleAlpha;
            Color tip = Color.Lerp(color, Color.white, m_tipWhiten);
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0.0f), new GradientColorKey(tip, 1.0f) },
                new[] { new GradientAlphaKey(a, 0.0f), new GradientAlphaKey(a * 0.8f, 0.6f), new GradientAlphaKey(0.0f, 1.0f) });
            line.colorGradient = gradient;
        }

        // 弾と同じ高さ・太さで向いている向きへ進め、壁で反射させた道筋(床の高さの点の列)を m_points に入れる
        private void BuildPath(Transform body)
        {
            m_points.Clear();
            float floorY = FindFloorY(body);
            Vector3 dir = body.forward;
            dir.y = 0.0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();

            // 体の中心から調べ始める(弾が出る位置から調べると、壁に密着しているときに弾の出る位置が壁の中・向こうになり、
            // 壁を素通りした線になる。実際の弾も壁際では体の近くに引き戻されて出る: TumikiPlacement.ResolveEmbed)
            // 弾が出る位置(体の中心からの距離)の分だけ長さを足して、壁が無いときの線の長さは変えない
            float launchOffset = DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.05f;
            Vector3 pos = new Vector3(body.position.x, floorY + m_castHeight, body.position.z);
            AddPoint(body.position, floorY);

            float remaining = m_length + launchOffset;
            for (int bounce = 0; remaining > 0.0f; bounce++)
            {
                if (!Cast(body, pos, dir, remaining, out RaycastHit hit, out bool reflects))
                {
                    AddPoint(pos + dir * remaining, floorY);
                    break;
                }
                Vector3 at = pos + dir * hit.distance;
                AddPoint(at, floorY);
                remaining -= hit.distance;
                if (!reflects || bounce >= m_maxBounces) break;

                Vector3 normal = hit.normal;
                normal.y = 0.0f;
                dir = Vector3.Reflect(dir, normal.normalized);
                dir.y = 0.0f;
                dir.Normalize();
                pos = at;
            }
        }

        // 弾の通り道で最初に当たる物。reflects: 壁(ITumikiReflector)・動かない物の側面なら true(バンパーなど動く物は false)
        private bool Cast(Transform body, Vector3 origin, Vector3 dir, float distance, out RaycastHit nearest, out bool reflects)
        {
            nearest = default;
            reflects = false;
            bool found = false;
            int n = Physics.SphereCastNonAlloc(origin, m_castRadius, dir, m_hits, distance, m_mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                RaycastHit hit = m_hits[i];
                // 始めから重なっている物(足元・反射した壁)は除く
                if (hit.distance <= 0.0f) continue;
                Collider col = hit.collider;
                if (col.transform.IsChildOf(body)) continue;
                // 積み木・キャラクターは動くので道筋を遮らない
                if (col.GetComponentInParent<TumikiBlock>() != null || col.GetComponentInParent<DarumaCharacter>() != null) continue;
                if (found && hit.distance >= nearest.distance) continue;

                bool reflector = col.GetComponentInParent<ITumikiReflector>() != null;
                bool side = Mathf.Abs(hit.normal.y) < 0.5f;
                if (!reflector && col.attachedRigidbody == null && !side) continue; // 床の上面などは道筋を遮らない
                nearest = hit;
                reflects = side && (reflector || col.attachedRigidbody == null);
                found = true;
            }
            return found;
        }

        // 足元の床の高さ(幽霊は浮いているので、真下の床を探す。なければ体の高さ)
        private float FindFloorY(Transform body)
        {
            Vector3 origin = body.position + Vector3.up * 0.5f;
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, m_hits, 4.0f, m_mask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                Collider col = m_hits[i].collider;
                if (col.transform.IsChildOf(body) || (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic)) continue;
                if (col.GetComponentInParent<TumikiBlock>() != null || col.GetComponentInParent<DarumaCharacter>() != null) continue;
                best = Mathf.Max(best, m_hits[i].point.y);
            }
            return float.IsNegativeInfinity(best) ? body.position.y : best;
        }

        private void AddPoint(Vector3 p, float floorY)
        {
            m_points.Add(new Vector3(p.x, floorY + m_lineHeight, p.z));
        }

        private LineRenderer CreateLine(string owner)
        {
            var go = new GameObject($"AimGuide_{owner}");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = m_width;
            line.numCapVertices = 4;
            line.numCornerVertices = 2;
            line.alignment = LineAlignment.TransformZ;
            // 床に寝かせる(上を向いた面に描く)
            go.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = GetMaterial();
            return line;
        }

        private Material GetMaterial()
        {
            if (m_material != null) return m_material;
            if (m_runtimeMaterial == null) m_runtimeMaterial = new Material(Shader.Find("Sprites/Default"));
            return m_runtimeMaterial;
        }

        private void OnDestroy()
        {
            if (m_runtimeMaterial != null) Destroy(m_runtimeMaterial);
        }
    }
}
