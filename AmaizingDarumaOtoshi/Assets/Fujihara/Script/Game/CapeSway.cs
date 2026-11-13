using System.Collections.Generic;
using UnityEngine;

// マント（だるきんぐのマントなど、骨の入ったモデル）を揺らす。物理（Cloth）は使わず、骨の先を「ばね」で追いかけさせる軽い揺れもの。
// ・走る・ジャンプする：体の動きと逆向きに風を受けて、大きくなびく（速いほど大きく、うしろへ持ち上がる）
// ・止まる：勢いで少し揺れ戻ってから垂れる
// ・いつも：風でゆっくりはためく（骨ごとにずらして、波打って見えるように）
// ・必殺技の溜め：Charge（0〜1）を上げると、うしろ上へ強い風が吹いて、ばたばたと大きくはためく
//   例）溜めている間に capeSway.Charge = 溜めの割合;  放ったら capeSway.Charge = 0;（急に 0 にしても、なめらかに落ち着く）
// ・体の中にめり込まないよう、体の形（縦の円柱）の外へ押し出す
// 使い方：マントのモデルを置いたオブジェクト（または親）に付ける。揺らす骨の鎖の根元（Cape_C1・Cape_L1・Cape_R1 など）を Roots に入れる。
// 空なら、子の中から名前が rootPrefix で始まり数字 1 で終わる骨（Cape_C1 など）を自動で探す。
public class CapeSway : MonoBehaviour
{
    [Tooltip("揺らす骨の鎖の根元（その子・孫も順に揺れる）。空なら名前から自動で探す")]
    [SerializeField] List<Transform> roots = new List<Transform>();
    [Tooltip("自動で探すときの名前の始まり（Cape_C1・Cape_L1・Cape_R1 のように、最後が 1 の骨を根元にする）")]
    [SerializeField] string rootPrefix = "Cape_";

    [Header("布の硬さ")]
    [Tooltip("元の形に戻ろうとする強さ（大きいほど硬い布）。根元から先へ少しずつ弱くなる")]
    [SerializeField, Range(0f, 1f)] float stiffness = 0.07f;
    [Tooltip("先へ行くほど柔らかくする割合（0 ならどこも同じ硬さ）")]
    [SerializeField, Range(0f, 1f)] float softerToTip = 0.5f;
    [Tooltip("揺れの止まりやすさ（大きいほど早く止まる）")]
    [SerializeField, Range(0f, 1f)] float damping = 0.1f;
    [Tooltip("重力（下へ引く強さ）")]
    [SerializeField] float gravity = 0.35f;
    [Tooltip("勢いの残りやすさ（大きいほど、止まったあとも揺れ続ける）")]
    [SerializeField, Range(0f, 1f)] float inertia = 0.8f;
    [Tooltip("1本の骨が元の向きから曲がれる最大の角度（ふだん）")]
    [SerializeField, Range(5f, 180f)] float maxAngle = 75f;

    [Header("走る・ジャンプで受ける風")]
    [Tooltip("体の動きと逆向きに受ける風の強さ（大きいほど、走るとうしろへ大きくなびく）")]
    [SerializeField] float airDrag = 0.35f;
    [Tooltip("上下の動き（ジャンプ・落下）で受ける風の割合（横の動きに比べて。大きいと落ちるときにマントが立ちすぎる）")]
    [SerializeField, Range(0f, 1f)] float jumpDrag = 0.35f;
    [Tooltip("走っているときに、うしろへ持ち上がる強さ（速さに比例）")]
    [SerializeField] float runLift = 0.12f;
    [Tooltip("走っているときの、ばたばたするゆらぎ（速さに比例）")]
    [SerializeField] float runFlutter = 0.12f;

    [Header("いつもの風")]
    [Tooltip("風の強さ。体のうしろ向き（-Z）へ吹く")]
    [SerializeField] float windStrength = 0.3f;
    [Tooltip("はためく速さ")]
    [SerializeField] float windSpeed = 1.8f;
    [Tooltip("骨ごとにずらすゆらぎ（波打って見えるように）")]
    [SerializeField] float windWave = 0.6f;

    [Header("必殺技の溜め（Charge を 0〜1 で渡す）")]
    [Tooltip("いまの溜めの強さ（0：ふつう、1：最大）。テストのときは Inspector で動かしてもよい")]
    [SerializeField, Range(0f, 1f)] float charge;
    [Tooltip("溜め最大のときの風の強さ（うしろ上へ吹く）")]
    [SerializeField] float chargeWind = 3f;
    [Tooltip("溜めの風の、上へ向かう割合（0 で真うしろ、1 で斜め上 45°）")]
    [SerializeField, Range(0f, 2f)] float chargeUp = 1f;
    [Tooltip("溜め最大のときの、ばたばたするゆらぎの強さ")]
    [SerializeField] float chargeFlutter = 1.8f;
    [Tooltip("溜め最大のときの、ばたばたする速さ")]
    [SerializeField] float chargeFlutterSpeed = 7f;
    [Tooltip("溜め最大のときに曲がれる最大の角度")]
    [SerializeField, Range(5f, 180f)] float chargeMaxAngle = 140f;
    [Tooltip("溜め最大のときの布の硬さの割合（小さいほど、風で大きくふくらむ）")]
    [SerializeField, Range(0.05f, 1f)] float chargeSoftness = 0.35f;
    [Tooltip("溜めの強さが変わる速さ（急に変えても、なめらかに追いかける）")]
    [SerializeField] float chargeFollow = 6f;

    [Header("端のはためき（左右のふち・すそが波打つ）")]
    [Tooltip("止まっているときの端のねじれの大きさ（度）。0 で端は揺れない")]
    [SerializeField, Range(0f, 90f)] float edgeFlutter = 14f;
    [Tooltip("端がはためく速さ")]
    [SerializeField] float edgeFlutterSpeed = 5f;
    [Tooltip("走る速さ 1 あたりに、端のはためきを何倍足すか")]
    [SerializeField] float edgeRunBoost = 0.25f;
    [Tooltip("溜め最大のときに、端のはためきを何倍足すか")]
    [SerializeField] float edgeChargeBoost = 2.5f;

    [Header("体（めり込まないように）")]
    [Tooltip("体の中心（このオブジェクトから見た位置）。縦の円柱として扱う")]
    [SerializeField] Vector3 bodyCenter = new Vector3(0f, 0.47f, 0f);
    [Tooltip("体の半径（マントの元の形が中に入らないよう、だるまの幅より少し小さめ）")]
    [SerializeField] float bodyRadius = 0.45f;
    [Tooltip("体の高さ")]
    [SerializeField] float bodyHeight = 1.0f;

    // 必殺技などから渡す溜めの強さ（0〜1）
    public float Charge { get => charge; set => charge = Mathf.Clamp01(value); }

    class Joint
    {
        public Transform bone;
        public Quaternion restLocalRot;
        public Vector3 tipLocal;        // 骨から見た先の位置（子の位置、子がなければ骨の向きに親と同じ長さ）
        public float length;
        public Vector3 tip, prevTip;    // 先の位置（世界）
        public float k;                 // この骨の硬さ
        public float seed;              // ゆらぎのずれ
        public float depth01;           // 根元 0 〜 先 1
        public float side;              // 左右（-1 右、0 中央、1 左）
    }

    readonly List<List<Joint>> chains = new List<List<Joint>>();
    bool started;
    Vector3 lastPos, bodyVel;
    float chargeNow;

    void Awake()
    {
        if (roots.Count == 0) FindRoots();
        for (int c = 0; c < roots.Count; c++)
        {
            if (roots[c] == null) continue;
            var list = new List<Joint>();
            var b = roots[c];
            float lastLen = 0.2f;
            int depth = 0;
            float side = Mathf.Sign(Mathf.Round(transform.InverseTransformPoint(b.position).x * 10f));
            while (b != null)
            {
                var child = b.childCount > 0 ? b.GetChild(0) : null;
                var j = new Joint { bone = b, restLocalRot = b.localRotation, side = side };
                if (child != null) { j.tipLocal = child.localPosition; lastLen = child.localPosition.magnitude; }
                else j.tipLocal = Vector3.up * lastLen;   // 骨は +Y が先の向き（Blender の骨）
                j.length = Mathf.Max(lastLen, 1e-4f);
                j.seed = c * 17.3f + depth * 3.1f;
                list.Add(j);
                b = child;
                depth++;
            }
            for (int i = 0; i < list.Count; i++)
            {
                float d = list.Count > 1 ? i / (list.Count - 1f) : 0f;
                list[i].depth01 = d;
                list[i].k = stiffness * Mathf.Lerp(1f, 1f - softerToTip, d);
            }
            chains.Add(list);
        }
    }

    void FindRoots()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith(rootPrefix) && t.name.EndsWith("1") && (t.parent == null || !t.parent.name.EndsWith("1") || !t.parent.name.StartsWith(rootPrefix)))
                roots.Add(t);
    }

    void OnEnable() => started = false;

    void LateUpdate()
    {
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        if (dt <= 0f) return;
        float t = Time.time;

        // 体の速さ（少しならして使う）
        if (!started) lastPos = transform.position;
        var v = (transform.position - lastPos) / dt;
        lastPos = transform.position;
        bodyVel = Vector3.Lerp(bodyVel, v, 1f - Mathf.Exp(-12f * dt));
        float speed = new Vector3(bodyVel.x, 0f, bodyVel.z).magnitude;

        chargeNow = Mathf.MoveTowards(chargeNow, charge, chargeFollow * dt);
        Vector3 back = -transform.forward, up = Vector3.up, right = transform.right;
        float limit = Mathf.Lerp(maxAngle, chargeMaxAngle, chargeNow);

        foreach (var chain in chains)
        {
            foreach (var j in chain)
            {
                // 元の向きに戻してから、その向きでの先（＝戻りたい位置）を出す
                j.bone.localRotation = j.restLocalRot;
                Vector3 rest = j.bone.TransformPoint(j.tipLocal);
                if (!started) { j.tip = j.prevTip = rest; }

                // ゆらぎ（-1〜1）。骨ごとにずらして、先ほど速く大きく揺れる
                float n1 = Mathf.PerlinNoise(t * windSpeed + j.seed, j.depth01 * windWave) * 2f - 1f;
                float n2 = Mathf.PerlinNoise(t * chargeFlutterSpeed + j.seed * 1.7f, 5f + j.depth01 * 2f) * 2f - 1f;
                float n3 = Mathf.PerlinNoise(t * chargeFlutterSpeed * 0.8f + j.seed * 2.3f, 9f) * 2f - 1f;

                // かかる力（1フレームでずれる量）
                Vector3 force = Vector3.down * gravity;
                // いつもの風：うしろへ、強弱をつけて
                force += back * windStrength * (0.6f + 0.6f * n1);
                // 走る・ジャンプ：体の動きと逆向きの風。速いほどうしろへ持ち上がり、ばたばたする
                force += -new Vector3(bodyVel.x, bodyVel.y * jumpDrag, bodyVel.z) * airDrag;
                force += up * runLift * speed;
                force += (up * n2 + right * n3) * runFlutter * speed * (0.2f + j.depth01 * 1.5f);
                // 必殺技の溜め：うしろ上への強い風＋大きなばたつき。左右の鎖は外側にも広がる
                if (chargeNow > 0f)
                {
                    Vector3 gust = (back + up * chargeUp + right * j.side * 0.5f).normalized;   // 左右の鎖は外側へ
                    force += gust * chargeWind * chargeNow * (0.75f + 0.35f * n1);
                    force += (up * n2 + right * n3 + back * n1 * 0.5f) * chargeFlutter * chargeNow * (0.2f + j.depth01 * 1.5f);
                }

                // ばね＋勢い＋力
                Vector3 vel = (j.tip - j.prevTip) * (1f - damping);
                float k = j.k * Mathf.Lerp(1f, chargeSoftness, chargeNow);   // 溜めている間は柔らかく
                Vector3 next = j.tip + vel * inertia + (rest - j.tip) * k + force * dt;

                // 骨の長さを保つ
                Vector3 root = j.bone.position;
                Vector3 dir = next - root;
                if (dir.sqrMagnitude < 1e-8f) dir = rest - root;
                next = root + dir.normalized * j.length;

                // 体の中にめり込んだら外へ押し出す
                next = PushOutOfBody(next);
                next = root + (next - root).normalized * j.length;

                // 元の形から曲がりすぎないようにする
                Vector3 restDir = (rest - root).normalized, nowDir = (next - root).normalized;
                float ang = Vector3.Angle(restDir, nowDir);
                if (ang > limit)
                {
                    nowDir = Vector3.Slerp(restDir, nowDir, limit / ang);
                    next = root + nowDir * j.length;
                }

                j.prevTip = j.tip;
                j.tip = next;

                // 骨を、先がその位置を向くように回す（子の骨もこれについていく）
                Vector3 from = rest - root, to = next - root;
                if (from.sqrMagnitude > 1e-8f && to.sqrMagnitude > 1e-8f)
                    j.bone.rotation = Quaternion.FromToRotation(from, to) * j.bone.rotation;

                // 端のはためき：骨を自分の長さの向きを軸にねじる。骨から離れた布の端（左右のふち・すそ）が
                // めくれたり戻ったりして、波打って見える。先ほど・速いほど・溜めているほど大きく
                if (edgeFlutter > 0f && to.sqrMagnitude > 1e-8f)
                {
                    float n4 = Mathf.PerlinNoise(t * edgeFlutterSpeed * (1f + chargeNow) + j.seed * 3.7f, 13f + j.depth01 * 3f) * 2f - 1f;
                    float amount = edgeFlutter * (0.25f + j.depth01) * (1f + speed * edgeRunBoost + chargeNow * edgeChargeBoost);
                    float side = j.side != 0f ? j.side : 1f;
                    j.bone.rotation = Quaternion.AngleAxis(n4 * amount * side, to.normalized) * j.bone.rotation;
                }
            }
        }
        started = true;
    }

    // 体（縦の円柱）の外へ出す
    Vector3 PushOutOfBody(Vector3 p)
    {
        Vector3 local = transform.InverseTransformPoint(p) - bodyCenter;
        float half = bodyHeight * 0.5f;
        if (local.y < -half || local.y > half) return p;
        var flat = new Vector2(local.x, local.z);
        float r = flat.magnitude;
        if (r >= bodyRadius) return p;
        flat = r < 1e-5f ? new Vector2(0f, -bodyRadius) : flat / r * bodyRadius;
        return transform.TransformPoint(bodyCenter + new Vector3(flat.x, local.y, flat.y));
    }

    // 体の形をシーンに表示（選んだときだけ）
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(bodyCenter, new Vector3(bodyRadius * 2f, bodyHeight, bodyRadius * 2f));
    }
}
