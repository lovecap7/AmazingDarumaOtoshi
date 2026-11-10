using UnityEngine;

namespace Nakagawa
{
    // ジャックポット(緑の0): 盤全体に積み木の雨がたくさん降り、重りも少し混ざる。紙吹雪が舞う
    // 積み木・重りの降らせ方は、赤・黒の出来事(RouletteTumikiRain / RouletteWeightRain)をそのまま使う
    public class RouletteJackpot : RoulettePocketEvent
    {
        [SerializeField] RouletteTumikiRain m_tumikiRain;
        [SerializeField] RouletteWeightRain m_weightRain;
        [SerializeField, Min(0)] int m_tumikiCount = 16;
        [SerializeField, Min(0)] int m_weightCount = 2;
        [SerializeField, Min(0)] int m_confettiBursts = 8;
        [SerializeField] GimmickBurstSettings m_confetti = new GimmickBurstSettings
        {
            Additive = false, ColorA = new Color(1.0f, 0.85f, 0.2f, 1.0f), ColorB = new Color(0.2f, 0.85f, 0.4f, 1.0f),
            Count = 30, Speed = new Vector2(3.0f, 8.0f), Size = new Vector2(0.12f, 0.25f), Lifetime = new Vector2(0.8f, 1.4f),
            Gravity = 0.5f, Shape = GimmickBurstShape.Hemisphere, ShapeSize = new Vector3(0.5f, 0, 0),
        };

        public override void Activate(RouletteGame game, int pocket)
        {
            var wheel = game.Wheel;
            if (m_tumikiRain != null) m_tumikiRain.Rain(wheel, 0.0f, 180.0f, m_tumikiCount);
            if (m_weightRain != null) m_weightRain.Rain(wheel, 0.0f, 180.0f, m_weightCount);
            for (int i = 0; i < m_confettiBursts; i++)
            {
                Vector3 p = RandomPoint(wheel, i * 360.0f / Mathf.Max(1, m_confettiBursts), 10.0f, 4.0f, 10.0f);
                GimmickBurst.Play(m_confetti, p + Vector3.up * 0.3f);
            }
            GimmickSound.Play(GimmickSound.Charge, 0.4f, 1.6f);
            BilliardFx.Shake(0.25f, 0.4f);
        }
    }
}
