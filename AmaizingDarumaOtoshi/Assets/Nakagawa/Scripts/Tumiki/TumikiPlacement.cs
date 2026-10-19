using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 積み木をキャラクターの近くに出すときの位置の補正(股抜きショットなど)
    // 出す位置が壁などにめり込む(または出す元との間に壁がある)なら、出す元(キャラクターの中心)に向かって引き戻し、
    // めり込まない範囲で一番遠い位置にする。出す元でもめり込むなら、壁と反対へ少しだけずらした位置
    // (見つからなければ出す元。わずかなめり込みは物理で押し出される)
    // 壁とみなすのは、積み木・キャラクター・幽霊以外の当たり判定(トリガーは除く)。床は積み木の底より下なので含まない
    public static class TumikiPlacement
    {
        // 壁際で出した積み木がキャラクターに当たらない時間(秒)
        public const float kEmbedIgnoreTime = 0.5f;
        // 引き戻すときの刻み(m)
        const float kStep = 0.05f;
        // 調べる形を積み木より少し小さくする量(触れているだけの壁・床の継ぎ目には反応しないように)
        const float kSkin = 0.03f;

        static readonly Collider[] s_overlaps = new Collider[16];
        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        // origin: 出す元(キャラクターの中心。高さは pos と同じにする) / pos: 出したい積み木の中心
        // めり込むので位置を変えたらtrue
        public static bool ResolveEmbed(Vector3 origin, ref Vector3 pos, int layerMask = Physics.DefaultRaycastLayers)
        {
            origin.y = pos.y;
            Vector3 delta = pos - origin;
            float distance = delta.magnitude;
            Quaternion rot = distance > 0.0001f ? Quaternion.LookRotation(delta / distance) : Quaternion.identity;

            if (!Overlaps(pos, rot, layerMask) && !HasWallBetween(origin, pos, layerMask)) return false;

            // 出す元から外へ少しずつ進め、めり込む直前の位置を使う(薄い壁の向こうへ抜けないように、元の側から調べる)
            Vector3 best = origin;
            if (!Overlaps(origin, rot, layerMask))
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(distance / kStep));
                for (int i = 1; i <= steps; i++)
                {
                    Vector3 p = origin + delta * ((float)i / steps);
                    if (Overlaps(p, rot, layerMask) || HasWallBetween(origin, p, layerMask)) break;
                    best = p;
                }
            }
            else if (distance > 0.0001f)
            {
                // 出す元でもめり込む(積み木はキャラクターより少し太い)なら、壁と反対へ積み木の半径までずらして、めり込まない位置を探す
                Vector3 back = -delta / distance;
                int steps = Mathf.CeilToInt(TumikiBlock.kRadius / kStep);
                for (int i = 1; i <= steps; i++)
                {
                    Vector3 p = origin + back * (kStep * i);
                    if (HasWallBetween(origin, p, layerMask)) break;
                    if (Overlaps(p, rot, layerMask)) continue;
                    best = p;
                    break;
                }
            }
            pos = best;
            return true;
        }

        // 積み木をしばらく全キャラクターと当たらないようにする(落ちている積み木とは当たる)
        // ResolveEmbed で位置を直した積み木を、置いて Launch したあとに呼ぶ(Launch で当たらない相手がリセットされるため)
        public static void IgnoreCharactersAfterEmbed(TumikiBlock block, float time = kEmbedIgnoreTime)
        {
            if (block == null) return;
            foreach (var character in Object.FindObjectsByType<DarumaCharacter>())
            {
                if (character != null) block.IgnoreFor(character.Body, time);
            }
        }

        // 積み木の形(向きを合わせた箱)が壁と重なるか
        public static bool Overlaps(Vector3 center, Quaternion rot, int layerMask = Physics.DefaultRaycastLayers)
        {
            float r = TumikiBlock.kRadius - kSkin;
            Vector3 half = new Vector3(r, TumikiBlock.kHeight * 0.5f - kSkin * 2.0f, r);
            int n = Physics.OverlapBoxNonAlloc(center, half, s_overlaps, rot, layerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (IsWall(s_overlaps[i])) return true;
            }
            return false;
        }

        // 中心どうしを結ぶ線の上に壁があるか
        static bool HasWallBetween(Vector3 from, Vector3 to, int layerMask)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 0.0001f) return false;
            int n = Physics.RaycastNonAlloc(from, d / length, s_hits, length, layerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (IsWall(s_hits[i].collider)) return true;
            }
            return false;
        }

        static bool IsWall(Collider col)
        {
            if (col == null) return false;
            return col.GetComponentInParent<TumikiBlock>() == null
                && col.GetComponentInParent<DarumaCharacter>() == null
                && col.GetComponentInParent<DarumaGhost>() == null;
        }
    }
}
