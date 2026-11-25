using UnityEngine;

namespace Nakahira
{
    // 体の見た目の動き(DarumaMotion・DarumaGhostMotion)
    // 積み木の段・頭・ハンマーが、自分の高さを渡して見た目のずれを受け取る(見た目だけ。当たり判定は動かさない)
    // ふだんは段ごとに平行にずらすだけ。ジャンプでのけぞるときだけ、段・頭を傾ける(GetTilt。ハンマーは傾けない)
    public interface IDarumaBodyMotion
    {
        // height: 足元からの高さ(体のローカル座標)。戻り値: 体のローカル座標での位置のずれ
        Vector3 GetOffset(float height);

        // height: 足元からの高さ(体のローカル座標)。戻り値: 体のローカル座標での傾き(本来の回転の外側に掛ける)
        Quaternion GetTilt(float height);
    }
}
