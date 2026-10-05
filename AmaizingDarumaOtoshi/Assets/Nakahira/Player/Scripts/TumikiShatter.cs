using UnityEngine;

namespace Nakahira
{
    // 積み木が割れて飛び散る演出。破片は当たり判定を持たず、見た目だけ
    // 破片全体は渡された速度で移動を続けながら放射状に広がり、縮んで消える
    public class TumikiShatter : MonoBehaviour
    {
        struct Piece
        {
            public Transform transform;
            public Vector3 velocity;
            public Vector3 angularVelocity; // 度/秒
            public Vector3 baseScale;
        }

        const float kGravity = 9.81f;

        Piece[] m_pieces;
        float m_duration;
        float m_time;

        // 破片を生成する
        // velocity: 割れた積み木そのものの移動速度、spread: 放射状に広がる速さ
        public static void Spawn(Vector3 position, Quaternion rotation, Vector3 velocity,
            Material material, Color color, float duration, int pieceCount, float spread)
        {
            var go = new GameObject("TumikiShatter");
            go.transform.SetPositionAndRotation(position, rotation);
            var shatter = go.AddComponent<TumikiShatter>();
            shatter.Init(velocity, material, color, duration, pieceCount, spread);
        }

        private void Init(Vector3 velocity, Material material, Color color, float duration, int pieceCount, float spread)
        {
            m_duration = Mathf.Max(0.01f, duration);
            m_pieces = new Piece[pieceCount];

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);

            for (int i = 0; i < pieceCount; i++)
            {
                var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(piece.GetComponent<Collider>());
                var r = piece.GetComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.SetPropertyBlock(mpb);

                // 積み木(円柱)の中に散らばるように配置
                float angle = (i + Random.value * 0.5f) / pieceCount * Mathf.PI * 2.0f;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
                Vector3 local = outward * Random.Range(0.15f, 0.35f)
                    + Vector3.up * Random.Range(-0.12f, 0.12f);

                Transform t = piece.transform;
                t.SetParent(transform, false);
                t.localPosition = local;
                t.localRotation = Random.rotation;
                Vector3 scale = Vector3.one * Random.Range(0.18f, 0.3f);
                scale.y *= 0.8f;
                t.localScale = scale;

                m_pieces[i] = new Piece
                {
                    transform = t,
                    velocity = velocity
                        + transform.rotation * outward * (spread * Random.Range(0.6f, 1.0f))
                        + Vector3.up * Random.Range(1.0f, 2.5f),
                    angularVelocity = Random.insideUnitSphere * 720.0f,
                    baseScale = scale,
                };
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            m_time += dt;
            float rate = Mathf.Clamp01(m_time / m_duration);
            // 後半ほど速く縮む
            float scale = 1.0f - rate * rate;

            for (int i = 0; i < m_pieces.Length; i++)
            {
                ref Piece p = ref m_pieces[i];
                p.velocity.y -= kGravity * dt;
                p.transform.position += p.velocity * dt;
                p.transform.Rotate(p.angularVelocity * dt, Space.Self);
                p.transform.localScale = p.baseScale * scale;
            }

            if (m_time >= m_duration)
            {
                Destroy(gameObject);
            }
        }
    }
}
