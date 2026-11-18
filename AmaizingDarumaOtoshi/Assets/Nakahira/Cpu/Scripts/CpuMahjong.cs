using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Nakahira
{
    // 牌あつめと洗牌(Nakagawa.MahjongTable。Stage22「こたつの全自動卓」)を CPU が扱うための共通の部品(2026-11-18)
    //  ・盤面の卓を探しておく(0.5 秒ごと)
    //  ・ツモの予兆(ふたの縁が黄色く点滅する 1.5 秒):ふたの上にいれば一番近い辺から降り、外にいれば上に乗らない
    //    (開いた瞬間にふたの上にいると、吸い込みに逆らう前に落ちるため。中平が決めた 2026-11-18)
    //  ・ツモ(洗牌)の吸い込みに逆らう:ふたが開いてから閉じ終わるまで、卓の上にいれば中央と逆へ歩く
    //    (発動した人は流されないので、穴の縁から 1.5m 以内に来たときだけ離れる。吸い込まれる積み木を追って跳び込まないように)
    //    (吸う強さ 14m/s² と減速 5/s がつり合う速さは約 2.8m/s。歩く速さがそれより遅い(積み木 11 段以上)と逆らいきれない)
    //  ・ナビメッシュ:中央のふたは開き具合に関係なく床として焼き込み、開いている間だけ切り抜く。
    //    着地した巨大な牌は焼き込まず、障害物になっている間だけ切り抜く(CpuGimmickObstacle。押されて動いても追う)
    // 設計メモ §7.1
    public static class CpuMahjong
    {
        const float kScanInterval = 0.5f;
        // 卓の上にいるとみなす、床の上面からの高さ(m)
        const float kOnTableHeight = 2.5f;
        // 発動した人(流されない)が、穴の縁からこの距離(m)より近ければ離れる
        const float kImmuneLidMargin = 1.5f;

        // ふたへ入るか調べる、進む先の距離(m。移動速度 × 0.1 秒ぶんを足して使う)
        const float kLidProbe = 0.6f;

        static Nakagawa.MahjongTable s_table;
        static float s_nextScan;
        // ふたの縁が点滅し始めた時刻(点滅していなければ負)
        static float s_warnSince = -1.0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_table = null;
            s_nextScan = 0.0f;
            s_warnSince = -1.0f;
        }

        // 盤面の卓(なければ null)
        public static Nakagawa.MahjongTable Table
        {
            get
            {
                if (Time.unscaledTime >= s_nextScan)
                {
                    s_nextScan = Time.unscaledTime + kScanInterval;
                    s_table = Object.FindFirstObjectByType<Nakagawa.MahjongTable>();
                }
                return s_table;
            }
        }

        // pos が卓の上(遊べる範囲の中で、床の高さの近く)か
        public static bool IsOnTable(Nakagawa.MahjongTable table, Vector3 pos)
        {
            Vector3 local = pos - table.CpuCenter;
            Vector2 half = table.CpuTableSize * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.y && local.y > -0.5f && local.y < kOnTableHeight;
        }

        // pos が中央のふたの範囲(margin m 広げる)か
        public static bool IsInLid(Nakagawa.MahjongTable table, Vector3 pos, float margin = 0.0f)
        {
            Vector3 local = pos - table.CpuCenter;
            Vector2 half = table.CpuLidSize * 0.5f;
            return Mathf.Abs(local.x) < half.x + margin && Mathf.Abs(local.z) < half.y + margin;
        }

        // ふたの縁が点滅している間(このあと開く):ふたの上にいれば一番近い辺から降り、外にいればふたへ入る向きの分を消す
        // 点滅に気づくのは始まってから HazardReaction 秒後(危ない場所と同じ)。降りたら true
        public static bool ApplyLidWarning(CpuContext c, ref DarumaCommand cmd)
        {
            var table = Table;
            if (table == null || !table.CpuLidWarning)
            {
                s_warnSince = -1.0f;
                return false;
            }
            if (s_warnSince < 0.0f) s_warnSince = Time.time;
            if (Time.time - s_warnSince < c.Params.HazardReaction) return false;
            Vector3 pos = c.Position;
            if (!IsOnTable(table, pos)) return false;

            float margin = DarumaCharacter.kBodyRadius + c.Params.HazardMargin;
            Vector3 local = pos - table.CpuCenter;
            Vector2 half = table.CpuLidSize * 0.5f + Vector2.one * margin;
            bool insideX = Mathf.Abs(local.x) < half.x;
            bool insideZ = Mathf.Abs(local.z) < half.y;
            if (insideX && insideZ)
            {
                // 一番近い辺から降りる
                float toX = half.x - Mathf.Abs(local.x);
                float toZ = half.y - Mathf.Abs(local.z);
                Vector2 dir = toX < toZ
                    ? new Vector2(local.x >= 0.0f ? 1.0f : -1.0f, 0.0f)
                    : new Vector2(0.0f, local.z >= 0.0f ? 1.0f : -1.0f);
                cmd.Move = dir;
                cmd.Jump = false;
                return true;
            }
            // 外にいる:ふたへ入る向きの分を消す(縁に沿って動く)
            Vector2 move = cmd.Move;
            if (move.sqrMagnitude < 0.01f) return false;
            Vector3 next = local + new Vector3(move.x, 0.0f, move.y).normalized * (kLidProbe + c.Self.MoveSpeed * 0.1f);
            if (Mathf.Abs(next.x) >= half.x || Mathf.Abs(next.z) >= half.y) return false;
            if (!insideX && move.x * local.x < 0.0f) move.x = 0.0f;
            if (!insideZ && move.y * local.z < 0.0f) move.y = 0.0f;
            cmd.Move = move;
            cmd.Jump = false;
            return false;
        }

        // 吸い込みに逆らう:ふたが開いている間、卓の上にいれば中央と逆へ歩く(どの状態・反応よりあと。移動だけ置き換える)
        // 吸い込みに逆らったら true
        public static bool ApplyResistFlow(CpuContext c, ref DarumaCommand cmd)
        {
            var table = Table;
            if (table == null || !table.CpuLidOpen) return false;
            Vector3 pos = c.Position;
            if (!IsOnTable(table, pos)) return false;
            // 発動した人は流されないが、穴のそばでは離れる(吸い込まれていく積み木を追って、穴へ跳び込まないように)
            if (c.Player != null && table.CpuFlowImmune == c.Player && !IsInLid(table, pos, kImmuneLidMargin)) return false;
            Vector3 away = pos - table.CpuCenter;
            away.y = 0.0f;
            if (away.sqrMagnitude < 0.0001f) away = c.Self.transform.forward;
            away.y = 0.0f;
            away.Normalize();
            cmd.Move = new Vector2(away.x, away.z);
            cmd.Jump = false;
            return true;
        }

        // ナビメッシュに焼き込まない当たり判定か(ふた:開くと沈むので別に床として足す / 巨大な牌:動くので切り抜きで扱う)
        public static bool IsExcludedFromNavMesh(Collider col)
        {
            var table = col.GetComponentInParent<Nakagawa.MahjongTable>();
            if (table == null) return false;
            if (col == table.CpuLidCollider) return true;
            // 巨大な牌は Rigidbody 付き(卓の床・壁には付いていない)
            return col.attachedRigidbody != null;
        }

        // ふたを床として焼き込む(開いていても閉じた位置に。上面は卓の床の高さ)
        public static void AddLidSources(List<NavMeshBuildSource> sources)
        {
            foreach (var table in Object.FindObjectsByType<Nakagawa.MahjongTable>())
            {
                Vector2 lid = table.CpuLidSize;
                const float thickness = 0.2f;
                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = new Vector3(lid.x, thickness, lid.y),
                    transform = Matrix4x4.Translate(table.CpuCenter + Vector3.down * (thickness * 0.5f)),
                    area = 0,
                });
            }
        }

        // ふたの穴と巨大な牌の切り抜きを付ける(ナビメッシュを作るとき)
        public static void AttachObstacles()
        {
            foreach (var table in Object.FindObjectsByType<Nakagawa.MahjongTable>()) CpuMahjongHoles.Attach(table);
        }
    }

    // ふたの穴と、着地した巨大な牌を、CPU のナビメッシュから切り抜く(卓の子として作る。試合中だけの一時的なオブジェクト)
    public class CpuMahjongHoles : MonoBehaviour
    {
        Nakagawa.MahjongTable m_table;
        NavMeshObstacle m_lid;
        readonly List<BoxCollider> m_giants = new List<BoxCollider>();

        public static void Attach(Nakagawa.MahjongTable table)
        {
            foreach (Transform child in table.transform)
            {
                if (child.GetComponent<CpuMahjongHoles>() != null) return;
            }
            var go = new GameObject("CpuMahjongHoles");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(table.transform, false);
            go.AddComponent<CpuMahjongHoles>().Setup(table);
        }

        private void Setup(Nakagawa.MahjongTable table)
        {
            m_table = table;
            var lid = new GameObject("LidHole");
            lid.hideFlags = HideFlags.DontSave;
            lid.transform.SetParent(transform, false);
            lid.transform.SetPositionAndRotation(table.CpuCenter, Quaternion.identity);
            m_lid = lid.AddComponent<NavMeshObstacle>();
            m_lid.shape = NavMeshObstacleShape.Box;
            Vector2 size = table.CpuLidSize;
            // 上はキャラの高さぶん、下はふたの厚みぶん
            m_lid.center = new Vector3(0.0f, 1.0f, 0.0f);
            m_lid.size = new Vector3(size.x, 3.0f, size.y);
            m_lid.carving = true;
            m_lid.carveOnlyStationary = false;
            m_lid.enabled = false;
            Sync();
        }

        private void Update()
        {
            Sync();
        }

        private void Sync()
        {
            if (m_table == null) return;
            bool open = m_table.CpuLidOpen;
            if (m_lid.enabled != open) m_lid.enabled = open;
            // 着地した巨大な牌(作り直されるたびに付け直す。障害物でなくなれば CpuGimmickObstacle が切り抜きをやめる)
            m_table.GetCpuSolidGiants(m_giants);
            foreach (var box in m_giants) CpuGimmickObstacle.Attach(box);
        }
    }
}
