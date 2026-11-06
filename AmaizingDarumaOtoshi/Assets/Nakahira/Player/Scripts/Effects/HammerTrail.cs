using UnityEngine;

namespace Nakahira
{
    // ハンマーを振っている間(薙ぎ払い・股抜きショット)、ハンマーの頭から出る線
    //  ・プレハブ(Assets/Nakahira/Player/Effects/HammerTrail*.prefab)に付けて使う
    //    太さ・残る時間・色・マテリアルはプレハブの TrailRenderer をそのまま編集する
    //  ・どのプレハブを使うかは HammerParams の「トレイル」で指定(キャラクターごと。空なら出さない)
    //  ・DarumaHammer が再生時に作り、毎フレーム Tick で頭の位置を渡す
    //  ・持ち主の子にはしない(振り終わってハンマーが待機の位置へ戻っても、線が引っ張られないようにするため)
    [RequireComponent(typeof(TrailRenderer))]
    public class HammerTrail : MonoBehaviour
    {
        TrailRenderer m_trail;
        bool m_emitting;

        private void Awake()
        {
            m_trail = GetComponent<TrailRenderer>();
            m_trail.emitting = false;
            m_trail.Clear();
        }

        // swinging: 振っている間 true / headPosition: ハンマーの頭の中心(ワールド)
        public void Tick(bool swinging, Vector3 headPosition)
        {
            if (swinging)
            {
                transform.position = headPosition;
                if (!m_emitting)
                {
                    // 待機の位置から振り始めの位置へ飛んだ分は線にしない
                    m_trail.Clear();
                    m_trail.emitting = true;
                    m_emitting = true;
                }
            }
            else if (m_emitting)
            {
                // 止めたら動かさない(残った線は消えていく)
                m_trail.emitting = false;
                m_emitting = false;
            }
        }
    }
}
