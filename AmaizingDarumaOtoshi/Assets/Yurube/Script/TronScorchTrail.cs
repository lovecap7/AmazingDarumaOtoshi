using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // トロンのレーザーが通った地面を、一定時間「焼け焦げ」として残す。
    // 円形のデカールを並べるのではなく、レーザーの軌跡に沿った1本の帯メッシュを作り、
    // 自作シェーダー(Yurube/TronScorch)で焦げのムラと残り火を描く。
    public class TronScorchTrail : MonoBehaviour
    {
        [Tooltip("地面に焼き目を付ける間隔(m)")]
        [SerializeField] float m_step = 0.25f;
        [Tooltip("焦げ帯の幅(m)。トレイルより細くして主役を邪魔しない")]
        [SerializeField] float m_width = 0.9f;
        [Tooltip("焦げが消えるまでの時間(秒)")]
        [SerializeField] float m_lifetime = 4.5f;
        [Tooltip("消え始めてから消えるまでの時間(秒)")]
        [SerializeField] float m_fadeTime = 2.5f;
        [Tooltip("残り火が消えるまでの時間(秒)")]
        [SerializeField] float m_emberTime = 0.6f;
        [Tooltip("地面を探すレイの高さ(m)")]
        [SerializeField] float m_rayHeight = 2.0f;
        [Tooltip("地面を探すレイの長さ(m)")]
        [SerializeField] float m_rayDistance = 4.0f;

        struct Point
        {
            public Vector3 position;
            public float time;
        }

        readonly List<Point> m_points = new List<Point>(256);
        readonly Vector3[] m_trailPositions = new Vector3[1024];
        readonly RaycastHit[] m_rayHits = new RaycastHit[16];

        AkadaruShotTrail m_trail;
        Material m_material;
        Mesh m_mesh;
        MeshFilter m_filter;
        MeshRenderer m_renderer;

        Vector3 m_lastTrailPoint;
        bool m_hasLastTrailPoint;
        bool m_stopped;

        static Material s_baseMaterial;

        private void Awake()
        {
            m_filter = gameObject.AddComponent<MeshFilter>();
            m_renderer = gameObject.AddComponent<MeshRenderer>();
            m_renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_renderer.receiveShadows = false;
            m_renderer.enabled = false;

            m_mesh = new Mesh { name = "TronScorchRibbon" };
            m_mesh.MarkDynamic();
            m_filter.sharedMesh = m_mesh;
        }

        // トロン発動時に呼ぶ(演出中のトレイルを渡す)
        public void Begin(AkadaruShotTrail trail)
        {
            m_trail = trail;

            var baseMaterial = ScorchMaterial;
            if (baseMaterial == null)
            {
                enabled = false;
                Destroy(gameObject);
                return;
            }

            // 帯ごとに時間を持つのでマテリアルは複製する
            m_material = new Material(baseMaterial);
            m_material.SetFloat("_FadeStart", Mathf.Max(0.0f, m_lifetime - m_fadeTime));
            m_material.SetFloat("_FadeTime", Mathf.Max(0.01f, m_fadeTime));
            m_material.SetFloat("_EmberTime", Mathf.Max(0.01f, m_emberTime));
            m_material.SetFloat("_Now", Time.time);
            m_renderer.sharedMaterial = m_material;
        }

        static Material ScorchMaterial
        {
            get
            {
                if (s_baseMaterial != null) return s_baseMaterial;

                s_baseMaterial = Resources.Load<Material>("TronScorch");
                if (s_baseMaterial == null)
                {
                    var shader = Shader.Find("Yurube/TronScorch");
                    if (shader != null) s_baseMaterial = new Material(shader);
                }
                return s_baseMaterial;
            }
        }

        private void Update()
        {
            if (!m_stopped)
            {
                // 積み木が消えたらトレイルも消えるので、そこで焼き付けをやめる
                if (m_trail == null) m_stopped = true;
                else SampleTrail();
            }

            if (m_material != null) m_material.SetFloat("_Now", Time.time);

            // 寿命が切れた地点を帯から外す
            if (TrimExpired()) RebuildMesh();

            if (m_stopped && m_points.Count == 0) Destroy(gameObject);
        }

        // トレイルの先端が一定距離進むたびに、その真下の地面を帯に追加する
        void SampleTrail()
        {
            int count = m_trail.CopyTrailPositions(m_trailPositions);
            if (count < 2) return;

            Vector3 newest = m_trailPositions[count - 1];
            if (!m_hasLastTrailPoint)
            {
                m_lastTrailPoint = newest;
                m_hasLastTrailPoint = true;
                return;
            }

            if ((newest - m_lastTrailPoint).sqrMagnitude < m_step * m_step) return;
            m_lastTrailPoint = newest;

            if (!TryGetGround(newest, out Vector3 ground)) return;

            m_points.Add(new Point { position = ground, time = Time.time });
            RebuildMesh();
        }

        // トレイルの真下の地面を探す(自分の積み木は貫通するので候補から除く)
        bool TryGetGround(Vector3 point, out Vector3 ground)
        {
            ground = Vector3.zero;

            Vector3 origin = point + Vector3.up * m_rayHeight;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, m_rayHits,
                m_rayHeight + m_rayDistance, ~0, QueryTriggerInteraction.Ignore);
            if (count <= 0) return false;

            RaycastHit best = default;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = m_rayHits[i];
                if (hit.collider == null) continue;
                if (hit.normal.y < 0.5f) continue;   // 床(ほぼ水平な面)だけ
                if (hit.collider.GetComponentInParent<TumikiBlock>() != null) continue;
                if (hit.collider.GetComponentInParent<DarumaCharacter>() != null) continue;
                if (hit.collider.attachedRigidbody != null) continue;   // 動くものは対象外
                if (hit.distance >= bestDistance) continue;

                bestDistance = hit.distance;
                best = hit;
            }
            if (best.collider == null) return false;

            ground = best.point + best.normal * 0.01f;
            return true;
        }

        bool TrimExpired()
        {
            float cutoff = Time.time - m_lifetime;
            int remove = 0;
            while (remove < m_points.Count && m_points[remove].time < cutoff) remove++;
            if (remove == 0) return false;

            m_points.RemoveRange(0, remove);
            return true;
        }

        // 軌跡の点列から帯メッシュを作り直す
        void RebuildMesh()
        {
            m_mesh.Clear();

            int count = m_points.Count;
            if (count < 2)
            {
                m_renderer.enabled = false;
                return;
            }

            var vertices = new Vector3[count * 2];
            var uv = new Vector2[count * 2];
            var uv2 = new Vector2[count * 2];
            var triangles = new int[(count - 1) * 6];

            float half = m_width * 0.5f;
            float distance = 0.0f;

            for (int i = 0; i < count; i++)
            {
                Vector3 previous = m_points[Mathf.Max(0, i - 1)].position;
                Vector3 next = m_points[Mathf.Min(count - 1, i + 1)].position;
                Vector3 direction = next - previous;
                direction.y = 0.0f;
                if (direction.sqrMagnitude < 0.000001f) direction = Vector3.forward;
                direction.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, direction).normalized;

                if (i > 0) distance += Vector3.Distance(m_points[i - 1].position, m_points[i].position);

                vertices[i * 2] = m_points[i].position - side * half;
                vertices[i * 2 + 1] = m_points[i].position + side * half;
                uv[i * 2] = new Vector2(0.0f, distance);
                uv[i * 2 + 1] = new Vector2(1.0f, distance);
                // uv2.x = その地点を焼いた時刻(シェーダーでフェードに使う)
                uv2[i * 2] = new Vector2(m_points[i].time, 0.0f);
                uv2[i * 2 + 1] = new Vector2(m_points[i].time, 0.0f);
            }

            for (int i = 0; i < count - 1; i++)
            {
                int v = i * 2;
                triangles[i * 6 + 0] = v;
                triangles[i * 6 + 1] = v + 2;
                triangles[i * 6 + 2] = v + 1;
                triangles[i * 6 + 3] = v + 1;
                triangles[i * 6 + 4] = v + 2;
                triangles[i * 6 + 5] = v + 3;
            }

            m_mesh.SetVertices(vertices);
            m_mesh.SetUVs(0, uv);
            m_mesh.SetUVs(1, uv2);
            m_mesh.SetTriangles(triangles, 0);
            m_mesh.RecalculateBounds();
            m_renderer.enabled = true;
        }

        private void OnDestroy()
        {
            if (m_material != null) Destroy(m_material);
            if (m_mesh != null) Destroy(m_mesh);
        }
    }
}
