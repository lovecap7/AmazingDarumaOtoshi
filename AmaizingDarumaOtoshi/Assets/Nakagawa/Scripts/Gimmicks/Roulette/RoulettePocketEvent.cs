using UnityEngine;

namespace Nakagawa
{
    // ルーレットの球が止まったポケットの色(緑・赤・黒)ごとに起きる出来事の元(Stage20)
    // RouletteGame の m_events に、緑・赤・黒の順に並べる
    // 新しい出来事はこれを継承して作り、並べるだけでよい(RouletteGame・RouletteWheel を変える必要はない)
    public abstract class RoulettePocketEvent : MonoBehaviour
    {
        [Tooltip("起きたときに出す名前")]
        [SerializeField] string m_label = "イベント";
        [SerializeField] Color m_color = Color.white;

        public string Label => m_label;
        public Color Color => m_color;

        // 起きる。pocket: 球が止まったポケット(RouletteWheel のポケットの番号順の位置)
        public abstract void Activate(RouletteGame game, int pocket);

        // すぐにやめる(ステージの切り替えなど)
        public virtual void Stop() { }

        protected virtual void OnDisable() => Stop();

        // 盤の上(半径 minR〜maxR、ワールドの角度 centerDeg ± spread)のランダムな点(床の高さ)
        public static Vector3 RandomPoint(RouletteWheel wheel, float centerDeg, float spread, float minR, float maxR)
        {
            float a = centerDeg + Random.Range(-spread, spread);
            // 面積が偏らないよう、半径は2乗で選ぶ
            float r = Mathf.Sqrt(Random.Range(minR * minR, maxR * maxR));
            return wheel.Center + RouletteMeshes.Point(r, a);
        }
    }
}
