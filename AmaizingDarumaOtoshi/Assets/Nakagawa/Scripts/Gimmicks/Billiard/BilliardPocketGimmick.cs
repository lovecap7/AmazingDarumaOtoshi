using UnityEngine;

namespace Nakagawa
{
    // ビリヤード台(BilliardTable)の的玉がポケットに入ったときに発動するギミックの元
    // 玉の種類(BilliardBall.Type)ごとに1つ、BilliardTable の m_gimmicks に並べる
    // 新しいギミックはこれを継承して作り、台に並べるだけでよい(BilliardTable・BilliardBall を変える必要はない)
    public abstract class BilliardPocketGimmick : MonoBehaviour
    {
        [Tooltip("発動したときに出す名前")]
        [SerializeField] string m_label = "ギミック";
        [Tooltip("ギミックの色(玉の色と合わせる。文字・エフェクトに使う)")]
        [SerializeField] Color m_color = Color.white;

        public string Label => m_label;
        public Color Color => m_color;
        // 効果が続いている間 true
        public virtual bool IsActive => false;

        // 発動する。pocket: 玉が入ったポケット、ball: 入った玉
        public abstract void Activate(BilliardTable table, Transform pocket, BilliardBall ball);

        // 効果をすぐにやめる(ステージの切り替え・試合のやり直しなど)
        public virtual void Stop() { }

        protected virtual void OnDisable() => Stop();

        // ネット対戦用(suehara): このギミックを並べている台(クライアントでは Activate が呼ばれないので、自分で探す)
        protected BilliardTable FindTable()
        {
            foreach (var t in FindObjectsByType<BilliardTable>())
            {
                foreach (var g in t.Gimmicks) if (g == this) return t;
            }
            return null;
        }
    }
}
