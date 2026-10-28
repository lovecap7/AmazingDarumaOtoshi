using Nakahira;
using UnityEngine;

namespace Yurube
{
    // いわだるのパッシブで付く「岩の積み木」の処理。
    // ・他の積み木に当たっても自分は壊れず、相手を壊す
    // ・壁に当たると壊れる
    // 積み木同士の処理は TumikiBlock.ImpactBehavior を差し替えて行い、
    // 壁は TumikiBlock が反射するより前に自前の衝突判定で壊す。
    [RequireComponent(typeof(TumikiBlock))]
    public class IwaBlock : MonoBehaviour
    {
        [Tooltip("岩化・壁での破壊をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logIwa = true;

        [Header("岩の見た目")]
        [Tooltip("積み木に付ける岩の破片の数")]
        [SerializeField, Min(0)] int m_chunkCount = 5;
        [Tooltip("岩の破片1個の大きさ(積み木は 1 x 0.5 x 1)")]
        [SerializeField, Min(0.01f)] float m_chunkSize = 0.28f;
        [Tooltip("破片の回転速度(度/秒)")]
        [SerializeField] float m_chunkSpin = 120.0f;

        static Mesh s_cubeMesh;
        static Material s_chunkMaterial;

        TumikiBlock m_block;
        bool m_active;
        Transform m_chunkRoot;

        // この発射で岩化したか(同じ積み木を何度も拾い直さないためのフラグ)
        public bool ConsumedLaunch { get; private set; }

        private void Awake()
        {
            m_block = GetComponent<TumikiBlock>();
            if (m_block != null) m_block.StateChanged += OnBlockStateChanged;
            enabled = false;
        }

        // いわだるのパッシブから、飛び始めた積み木に対して呼ばれる
        public void Begin(bool log)
        {
            m_logIwa = log;
            m_active = true;
            ConsumedLaunch = true;
            if (m_block != null) m_block.ImpactBehavior = IwaImpact.Instance;
            BuildChunks();
            enabled = true;
            if (m_logIwa) Debug.Log("[いわだる] 積み木が岩化：積み木を壊し、壁で壊れる", this);
        }

        private void OnBlockStateChanged(TumikiBlock block)
        {
            if (block.CurrentState == TumikiBlock.State.Flying) return;

            // 落ちた・積まれたら岩の効果を外す(次に誰が投げても普通の積み木に戻る)
            ConsumedLaunch = false;
            Restore();
        }

        private void Restore()
        {
            m_active = false;
            DestroyChunks();
            if (m_block != null && m_block.ImpactBehavior == IwaImpact.Instance) m_block.ImpactBehavior = null;
            enabled = false;
        }

        //========================================
        // 見た目: 岩の破片を積み木にまとわせる
        //========================================

        // 積み木の見た目(Visual)の子に小さな灰色キューブを数個付ける。
        // コライダーは付けないので当たり判定は変わらない。
        private void BuildChunks()
        {
            if (m_chunkRoot != null || m_chunkCount <= 0) return;

            var visual = GetComponentInChildren<MeshRenderer>();
            if (visual == null) return;

            m_chunkRoot = new GameObject("IwaChunks").transform;
            m_chunkRoot.SetParent(visual.transform, false);
            // 積み木が拡大縮小されていても破片の大きさが変わらないように打ち消す
            Vector3 scale = visual.transform.lossyScale;
            m_chunkRoot.localScale = new Vector3(
                Mathf.Abs(scale.x) > 0.0001f ? 1.0f / scale.x : 1.0f,
                Mathf.Abs(scale.y) > 0.0001f ? 1.0f / scale.y : 1.0f,
                Mathf.Abs(scale.z) > 0.0001f ? 1.0f / scale.z : 1.0f);

            if (s_cubeMesh == null) s_cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var material = GetChunkMaterial(visual.sharedMaterial);

            for (int i = 0; i < m_chunkCount; i++)
            {
                var chunk = new GameObject($"IwaChunk{i}");
                chunk.transform.SetParent(m_chunkRoot, false);

                // 積み木(1 x 0.5 x 1 の箱)の外周に沿って配置する。
                // 円ではなく四角の外周に合わせないと、角の破片が箱の中に埋もれてしまう。
                float angle = (i / (float)m_chunkCount) * Mathf.PI * 2.0f + Random.Range(-0.25f, 0.25f);
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
                float toEdge = 0.5f / Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.z));
                float radius = toEdge + Random.Range(-0.03f, 0.02f);
                chunk.transform.localPosition = new Vector3(
                    direction.x * radius,
                    Random.Range(-0.10f, 0.12f),
                    direction.z * radius);
                chunk.transform.localRotation = Random.rotation;

                float size = m_chunkSize * Random.Range(0.8f, 1.3f);
                chunk.transform.localScale = new Vector3(size, size * Random.Range(0.7f, 1.1f), size);

                chunk.AddComponent<MeshFilter>().sharedMesh = s_cubeMesh;
                var renderer = chunk.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private void DestroyChunks()
        {
            if (m_chunkRoot == null) return;
            Destroy(m_chunkRoot.gameObject);
            m_chunkRoot = null;
        }

        // 岩の破片のマテリアル。最初の1個だけ作って全積み木で共有する
        private static Material GetChunkMaterial(Material source)
        {
            if (s_chunkMaterial != null) return s_chunkMaterial;

            s_chunkMaterial = source != null
                ? new Material(source)
                : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            s_chunkMaterial.name = "IwaChunk (Runtime)";
            if (s_chunkMaterial.HasProperty("_BaseColor"))
            {
                s_chunkMaterial.SetColor("_BaseColor", new Color(0.5f, 0.47f, 0.42f, 1.0f));
            }
            return s_chunkMaterial;
        }

        // 破片をゆっくり回して「岩がまとわりついている」感じを出す
        private void Update()
        {
            if (m_chunkRoot == null) return;
            float delta = m_chunkSpin * Time.deltaTime;
            for (int i = 0; i < m_chunkRoot.childCount; i++)
            {
                m_chunkRoot.GetChild(i).Rotate(delta * 0.5f, delta, 0.0f, Space.Self);
            }
        }

        // 壁に当たったら壊れる(積み木・キャラクター・動くものは対象外)
        private void OnCollisionEnter(Collision collision) => TryBreakOnWall(collision);
        private void OnCollisionStay(Collision collision) => TryBreakOnWall(collision);

        private void TryBreakOnWall(Collision collision)
        {
            if (!m_active || m_block == null) return;
            if (m_block.CurrentState != TumikiBlock.State.Flying) return;
            if (!IsWall(collision)) return;

            m_active = false;
            enabled = false;
            if (m_logIwa) Debug.Log("[いわだる] 岩の積み木が壁に当たって壊れた", this);
            m_block.Break(m_block.Direction * m_block.Speed, 0.0f, 0.0f);
        }

        // TumikiBlock.IsReflectSurface と同じ考え方(横向きの固定した面を壁とみなす)
        private static bool IsWall(Collision collision)
        {
            Collider col = collision.collider;
            if (col == null) return false;
            if (col.GetComponentInParent<TumikiBlock>() != null) return false;
            if (col.GetComponentInParent<DarumaCharacter>() != null) return false;
            if (col.GetComponentInParent<ITumikiReflector>() != null) return true;
            if (col.attachedRigidbody != null) return false;
            if (collision.contactCount == 0) return false;

            ContactPoint contact = collision.GetContact(0);
            if (Mathf.Abs(contact.normal.y) >= 0.5f) return false;        // 床・天井は壁扱いしない
            if (contact.point.y > col.bounds.max.y - 0.1f) return false;  // 低い壁の上面
            return true;
        }
    }

    // 岩の積み木の「他の積み木に当たったとき」の処理。
    // 相手を壊し、自分は壊れずに飛び続ける。
    public sealed class IwaImpact : ITumikiImpactBehavior
    {
        public static readonly IwaImpact Instance = new IwaImpact();

        private IwaImpact()
        {
        }

        public void OnHitBlock(TumikiBlock self, TumikiBlock other)
        {
            if (self == null || other == null) return;

            self.ComputeImpact(other.transform.position, out Vector3 dir, out float speed);
            self.GetImpactHitStop(out float duration, out float timeScale, out float shake);

            other.StartHitStop(duration, timeScale, shake);
            other.Break(dir * speed, duration, shake);

            // 相手を砕いた分だけ少し減速しつつ、そのまま突き進む
            self.SetFlyingVelocity(self.Direction * (self.Speed * 0.9f));
        }
    }
}
