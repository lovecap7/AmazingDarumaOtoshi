using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 積み木を生成するエリア(矩形 / 円形)。TumikiSpawner からの生成命令どおりに生成する
    // 範囲内のランダムな位置に、積み木の半径を考慮して次の条件を満たすように置く
    //  ・エリアからはみ出さない
    //  ・生成済みの積み木、同じ命令で生成する他の積み木、当たり判定を持つステージギミック等と重ならない
    // プレイヤーが範囲内にいる場合は、このエリアの生成をスキップする
    // 生成は直接行わず、決めた位置に床の予告(TumikiSpawnWarning)を出し、予告の時間が過ぎてから生成する
    // 床の目印(範囲の表示)はデバッグ表示。MarkersVisible がオンのときだけ見える(SpawnPointDebugView が切り替える。エディタでは常に見える)
    // このTransformの位置が床の高さの中心。矩形は向きに合わせて回る
    public class TumikiSpawnArea : MonoBehaviour
    {
        public enum Shape { Rectangle, Circle }

        [SerializeField] Shape m_shape = Shape.Rectangle;
        // 矩形の大きさ(X, Z)
        [SerializeField] Vector2 m_size = new Vector2(6.0f, 6.0f);
        // 円形の半径
        [SerializeField] float m_radius = 3.0f;

        [Header("排出量(1回の生成命令で割り振られる数)")]
        // 生成予定数がすべてのエリアの最小の合計より少ない / 最大の合計より多い場合は、TumikiSpawner が生成予定数を調整する
        [SerializeField, Min(0)] int m_minAmount = 0;
        [SerializeField, Min(0)] int m_maxAmount = 10;

        [Header("配置")]
        // 積み木同士・障害物との最低限の隙間
        [SerializeField] float m_spacing = 0.1f;
        // 1個の位置を探す試行回数(見つからなければその1個は生成しない)
        [SerializeField] int m_maxAttempts = 30;
        // 重なりを調べる対象(床はこの高さより下なので含めてよい)
        [SerializeField] LayerMask m_obstacleMask = Physics.DefaultRaycastLayers;
        // 重なりを調べる高さ(積み木の上にある物とも重ならないように少し高めに見る)
        [SerializeField] float m_checkHeight = 1.0f;
        // 積み木の真下(中心と外周4点)に床があるところにだけ置く(穴の上に生成して落ちないように)
        [SerializeField] bool m_requireFloor = true;
        // 積み木の底面全体が床の高さの床に乗る(完全に接地する)ところにだけ置く
        // (エリアがステージの外へはみ出していたり、床の縁・継ぎ目の隙間にかかっていたりしても、床の上だけに生成される)
        [SerializeField] bool m_requireFullSupport = true;
        // 底面の外周を調べる点の数
        [SerializeField, Min(4)] int m_supportSamples = 12;
        // 床の上面の高さとみなす誤差(m)。これより高い・低い物(ギミックの上面・段差)は床とみなさない
        [SerializeField, Min(0.001f)] float m_supportTolerance = 0.03f;

        [Header("予告")]
        // 予告を出してから生成するまでの時間(0 なら予告なしですぐ生成する)
        [SerializeField, Min(0.0f)] float m_warningTime = 1.0f;
        // 生成する直前に、予告の位置にプレイヤー・積み木などが乗っていたら生成しない
        [SerializeField] bool m_recheckOnSpawn = true;

        [Header("見た目(デバッグ表示)")]
        // 床の目印(形に合わせて片方だけ表示し、大きさを合わせる。当たり判定なし)
        [SerializeField] Transform m_rectMarker;
        [SerializeField] Transform m_circleMarker;

        // 床から浮かせて調べる高さ(床そのものに当たらないように)
        const float kFloorClearance = 0.05f;

        // 再生中に床の目印を見せるか(全エリア共通のデバッグ表示)
        public static bool MarkersVisible
        {
            get => s_markersVisible;
            set
            {
                if (s_markersVisible == value) return;
                s_markersVisible = value;
                foreach (var area in FindObjectsByType<TumikiSpawnArea>()) area.ApplyShape();
            }
        }
        static bool s_markersVisible;

        // ドメインの再読み込みをしない設定でも、再生のたびに非表示に戻す
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_markersVisible = false;

        public Shape AreaShape => m_shape;
        public float FloorY => transform.position.y;
        public int MinAmount => m_minAmount;
        public int MaxAmount => Mathf.Max(m_minAmount, m_maxAmount);
        public float WarningTime => m_warningTime;

        private void Awake()
        {
            ApplyShape();
        }

        // ステージの切り替えなどで無効の間に表示が切り替わっていても合わせる
        private void OnEnable()
        {
            ApplyShape();
        }

        private void OnValidate()
        {
            m_size = Vector2.Max(m_size, new Vector2(0.1f, 0.1f));
            m_radius = Mathf.Max(0.1f, m_radius);
            m_maxAttempts = Mathf.Max(1, m_maxAttempts);
            m_maxAmount = Mathf.Max(m_minAmount, m_maxAmount);
            ApplyShape();
        }

        // 生成命令。colors の色の積み木の位置を決めて予告を出し(予告の時間が0ならすぐ生成し)、その数を返す
        // reserved: 同じ命令で他のエリアが生成した位置(重ならないよう共有する。生成した位置を追加する)
        public int Spawn(IReadOnlyList<TumikiColor> colors, TumikiBlock prefab, List<Vector3> reserved)
        {
            if (colors.Count == 0 || prefab == null) return 0;
            if (HasPlayerInside()) return 0;

            int spawned = 0;
            foreach (var color in colors)
            {
                if (!TryFindPosition(reserved, out Vector3 pos)) continue;
                reserved.Add(pos);

                Quaternion rot = Quaternion.Euler(0.0f, Random.Range(0.0f, 360.0f), 0.0f);
                if (m_warningTime > 0.0f) TumikiSpawnWarning.Create(this, prefab, color, pos, rot, m_warningTime);
                else SpawnBlock(prefab, color, pos, rot);
                spawned++;
            }
            return spawned;
        }

        // 積み木を1個生成する(ポンと出てくる演出つき)。予告(TumikiSpawnWarning)が終わったときにも呼ばれる
        public TumikiBlock SpawnBlock(TumikiBlock prefab, TumikiColor color, Vector3 pos, Quaternion rot)
        {
            if (prefab == null) return null;
            TumikiBlock block = Instantiate(prefab, pos, rot);
            block.SetColor(color);
            block.gameObject.AddComponent<TumikiSpawnPop>();
            return block;
        }

        // 予告していた位置に、今も生成してよいか(プレイヤー・積み木・ギミックなどが乗っていないか)
        public bool CanSpawnAt(Vector3 pos)
        {
            return !m_recheckOnSpawn || !OverlapsObstacle(pos, TumikiBlock.kRadius);
        }

        // プレイヤー(生存キャラクター)の体が少しでも範囲にかかっているか
        public bool HasPlayerInside()
        {
            foreach (var character in FindObjectsByType<DarumaCharacter>())
            {
                if (Contains(character.transform.position, -DarumaCharacter.kBodyRadius)) return true;
            }
            return false;
        }

        // XZ平面で範囲内か。inset > 0 なら内側に縮めた範囲、< 0 なら外側に広げた範囲で判定する
        public bool Contains(Vector3 worldPos, float inset = 0.0f)
        {
            Vector3 local = Quaternion.Inverse(transform.rotation) * (worldPos - transform.position);
            if (m_shape == Shape.Circle)
            {
                float r = m_radius - inset;
                return r > 0.0f && local.x * local.x + local.z * local.z <= r * r;
            }
            float hx = m_size.x * 0.5f - inset;
            float hz = m_size.y * 0.5f - inset;
            return hx > 0.0f && hz > 0.0f && Mathf.Abs(local.x) <= hx && Mathf.Abs(local.z) <= hz;
        }

        private bool TryFindPosition(List<Vector3> reserved, out Vector3 pos)
        {
            float r = TumikiBlock.kRadius;
            float minDistance = r * 2.0f + m_spacing;
            for (int i = 0; i < m_maxAttempts; i++)
            {
                if (!TryRandomPoint(r, out pos)) return false;
                if (OverlapsReserved(pos, reserved, minDistance)) continue;
                if (OverlapsWarning(pos, minDistance)) continue;
                if (m_requireFloor && !HasFloorBelow(pos, r)) continue;
                if (m_requireFullSupport && !IsFullySupported(pos, r)) continue;
                if (OverlapsObstacle(pos, r + m_spacing)) continue;
                return true;
            }
            pos = default;
            return false;
        }

        // はみ出さないよう、積み木の半径だけ内側に縮めた範囲から一様に選ぶ。範囲が小さすぎればfalse
        private bool TryRandomPoint(float blockRadius, out Vector3 pos)
        {
            Vector3 local;
            if (m_shape == Shape.Circle)
            {
                float r = m_radius - blockRadius;
                if (r < 0.0f) { pos = default; return false; }
                Vector2 p = Random.insideUnitCircle * r;
                local = new Vector3(p.x, 0.0f, p.y);
            }
            else
            {
                float hx = m_size.x * 0.5f - blockRadius;
                float hz = m_size.y * 0.5f - blockRadius;
                if (hx < 0.0f || hz < 0.0f) { pos = default; return false; }
                local = new Vector3(Random.Range(-hx, hx), 0.0f, Random.Range(-hz, hz));
            }
            pos = transform.position + transform.rotation * local + Vector3.up * (TumikiBlock.kHeight * 0.5f + kFloorClearance);
            return true;
        }

        private static bool OverlapsReserved(Vector3 pos, List<Vector3> reserved, float minDistance)
        {
            foreach (var p in reserved)
            {
                Vector3 d = p - pos;
                d.y = 0.0f;
                if (d.sqrMagnitude < minDistance * minDistance) return true;
            }
            return false;
        }

        // 予告中(まだ生成していない)の積み木と重なるか
        private static bool OverlapsWarning(Vector3 pos, float minDistance)
        {
            foreach (var w in TumikiSpawnWarning.Active)
            {
                Vector3 d = w.Position - pos;
                d.y = 0.0f;
                if (d.sqrMagnitude < minDistance * minDistance) return true;
            }
            return false;
        }

        private bool HasFloorBelow(Vector3 pos, float radius)
        {
            float top = FloorY + kFloorClearance + 0.2f;
            Vector3[] offsets = { Vector3.zero, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
            foreach (var o in offsets)
            {
                Vector3 origin = new Vector3(pos.x, top, pos.z) + o * radius;
                if (!Physics.Raycast(origin, Vector3.down, 0.5f, m_obstacleMask, QueryTriggerInteraction.Ignore)) return false;
            }
            return true;
        }

        static readonly RaycastHit[] s_hits = new RaycastHit[8];

        // 積み木の底面(中心と外周)のすべての点の真下に、床の高さの動かない床があるか
        public bool IsFullySupported(Vector3 pos, float radius)
        {
            if (!HasSupportAt(pos)) return false;
            int samples = Mathf.Max(4, m_supportSamples);
            // 外周ぎりぎりではなく、ほんの少し内側を調べる(床の縁にぴったり乗るのは許す)
            float r = radius * 0.98f;
            for (int i = 0; i < samples; i++)
            {
                float a = i * Mathf.PI * 2.0f / samples;
                if (!HasSupportAt(pos + new Vector3(Mathf.Cos(a), 0.0f, Mathf.Sin(a)) * r)) return false;
            }
            return true;
        }

        private bool HasSupportAt(Vector3 p)
        {
            float top = FloorY + kFloorClearance + 0.2f;
            Vector3 origin = new Vector3(p.x, top, p.z);
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, s_hits, 0.5f, m_obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var hit = s_hits[i];
                // 動く物(積み木・プレイヤー・ギミック)は床とみなさない
                if (hit.collider.attachedRigidbody != null) continue;
                if (Mathf.Abs(hit.point.y - FloorY) <= m_supportTolerance) return true;
            }
            return false;
        }

        // 積み木の円柱を囲む箱の範囲に、当たり判定(生成済みの積み木・ギミック・壁・プレイヤー等)があるか
        // 箱の底は床より少し上なので、床そのものには反応しない
        private bool OverlapsObstacle(Vector3 pos, float radius)
        {
            float bottom = FloorY + kFloorClearance;
            float height = Mathf.Max(TumikiBlock.kHeight, m_checkHeight);
            Vector3 center = new Vector3(pos.x, bottom + height * 0.5f, pos.z);
            Vector3 half = new Vector3(radius, height * 0.5f, radius);
            return Physics.CheckBox(center, half, Quaternion.identity, m_obstacleMask, QueryTriggerInteraction.Ignore);
        }

        // 形に合わせて床の目印を切り替え、大きさを合わせる(エディタから設定を変えたときにも呼ぶ)
        // 再生中は MarkersVisible(デバッグ表示)がオフなら両方とも隠す
        public void ApplyShape()
        {
            bool show = !Application.isPlaying || s_markersVisible;
            // OnValidateからも呼ぶので、SetActiveではなく描画の有効・無効で切り替える
            if (m_rectMarker != null)
            {
                SetVisible(m_rectMarker, show && m_shape == Shape.Rectangle);
                Vector3 s = m_rectMarker.localScale;
                m_rectMarker.localScale = new Vector3(m_size.x, s.y, m_size.y);
            }
            if (m_circleMarker != null)
            {
                SetVisible(m_circleMarker, show && m_shape == Shape.Circle);
                Vector3 s = m_circleMarker.localScale;
                m_circleMarker.localScale = new Vector3(m_radius * 2.0f, s.y, m_radius * 2.0f);
            }
        }

        private static void SetVisible(Transform t, bool visible)
        {
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1.0f, 0.5f, 0.9f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            if (m_shape == Shape.Circle)
            {
                const int kSegments = 48;
                Vector3 prev = new Vector3(m_radius, 0.0f, 0.0f);
                for (int i = 1; i <= kSegments; i++)
                {
                    float a = i * Mathf.PI * 2.0f / kSegments;
                    Vector3 p = new Vector3(Mathf.Cos(a) * m_radius, 0.0f, Mathf.Sin(a) * m_radius);
                    Gizmos.DrawLine(prev, p);
                    prev = p;
                }
            }
            else
            {
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(m_size.x, 0.02f, m_size.y));
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
