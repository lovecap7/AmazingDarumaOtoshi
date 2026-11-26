using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Yurube
{
    // あかだるの必殺技「トロン」。
    // ゲージが満タンのとき L キー / ゲームパッドの上(Y / △)で発動する。
    // 自分の積み木を1個飛ばし、発射地点から伸びる残像ビームに一定時間当たり判定を付ける。
    // ビームが相手に当たると、相手と自分が即死し、積み木は破壊される。
    [RequireComponent(typeof(DarumaCharacter))]
    public class AkadaruSpecial : MonoBehaviour, ISpecialAbility, INetSpecial
    {
        [Header("弾")]
        [Tooltip("必殺技の積み木の速さ(m/s)")]
        [SerializeField] float m_speed = 20.0f;
        [Tooltip("残像ビームが残る時間(秒)。弾が止まった後もこの時間までレーザーが残る")]
        [SerializeField] float m_trailDuration = 15.0f;
        [Tooltip("通常の射撃エフェクトの速さの下限")]
        [SerializeField] float m_effectMinSpeed = 30.0f;
        [Tooltip("通常の射撃エフェクトの速さの上限")]
        [SerializeField] float m_effectMaxSpeed = 60.0f;
        [Tooltip("発動直後、自分が自分の残像に当たってもダメージを受けない時間(秒)")]
        [SerializeField] float m_ownerGrace = 0.6f;
        [Tooltip("同じ相手に連続で当たったとき、次の積み木を壊すまでの間隔(秒)")]
        [SerializeField] float m_hitInterval = 0.4f;
        [Tooltip("発動・命中をConsoleに出す(テスト用)")]
        [SerializeField] bool m_logSpecial = true;

        [Header("カットイン")]
        [SerializeField] string m_cutInSpecialName = "終焉の軌跡";
        [SerializeField] Color m_cutInColor = new Color(0.91f, 0.29f, 0.25f);

        [Header("ハンマーの嵐(suehara)。オンなら上の「終焉の軌跡」の代わりにこちらを出す")]
        [Tooltip("オン: 大きなハンマーを体の周りで回す必殺技 / オフ: 前の「終焉の軌跡」")]
        [SerializeField] bool m_useHammerStorm = true;
        [Tooltip("技名(カットインに出す)")]
        [SerializeField] string m_stormName = "紅蓮乱舞";
        [Tooltip("効果時間(秒)")]
        [SerializeField] float m_stormDuration = 5.0f;
        [Tooltip("同じ敵を殴れる間隔(秒)")]
        [SerializeField] float m_stormHitInterval = 0.5f;
        [Tooltip("飛んでくる弾をはじき返す速さ(m/s)")]
        [SerializeField] float m_stormKnockSpeed = 24.0f;
        [Tooltip("回るハンマーの大きさ(手持ちのハンマーに対する倍率)")]
        [SerializeField] float m_stormHammerScale = 2.0f;
        [Tooltip("回る速さ(度/秒)。全部のハンマーが等間隔のまま同じ向きに回る(衛星)。マイナスで反時計回り")]
        [SerializeField] float m_stormOrbitSpeed = 468.0f;

        DarumaCharacter m_character;
        SpecialGauge m_gauge;
        Material m_effectMaterial;
        AkadaruHammerStorm m_storm;
        float m_stormActiveUntil = -1.0f;

        // CPU 用(中平): 必殺技の積み木の速さ(m/s。狙いの先読みに使う)
        public float Speed => m_speed;
        // CPU 用(suehara): ハンマーの嵐を使うか / 回るハンマーが届く距離
        public bool UsesHammerStorm => m_useHammerStorm;
        public float StormReach => AkadaruHammerStorm.ReachFor(m_character, m_stormHammerScale);
        // ラボ表示用: 紅蓮乱舞の残り時間・ハンマー倍率・インスタンス
        public float StormHammerScale => m_stormHammerScale;
        public float StormDuration => m_stormDuration;
        public AkadaruHammerStorm ActiveStorm => m_storm;
        public float RemainingSeconds => m_storm != null && m_stormActiveUntil > 0.0f
            ? Mathf.Max(0.0f, m_stormActiveUntil - Time.time)
            : (Time.time < m_activeUntil ? Mathf.Max(0.0f, m_activeUntil - Time.time) : 0.0f);

        // 必殺技ゲージ用(中平): 発動してから残像ビームが残っている間(この間はゲージが溜まらない)
        float m_activeUntil = -1.0f;
        public bool IsActive => Time.time < m_activeUntil || m_storm != null;

        private void Awake()
        {
            m_character = GetComponent<DarumaCharacter>();
            m_gauge = GetComponent<SpecialGauge>();
            // 通常の射撃エフェクトと同じマテリアルを使う
            var passive = GetComponent<AkadaruPassive>();
            m_effectMaterial = passive != null ? passive.EffectMaterialForSpecial : null;
        }

        private void Update()
        {
            if (m_gauge == null || !IsLocalHuman()) return;

            var keyboard = Keyboard.current;
            // F3で当たり判定のデバッグ表示を切り替える
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame)
                SpecialHitboxDebug.Toggle();

            bool pressed = SpecialInput.WasPressed(m_character);
            if (pressed) TryActivate();
        }

        // CPUのあかだるが、人間のキー入力で必殺技を出さないようにする
        bool IsLocalHuman()
        {
            if (DarumaMatch.Current == null) return true;   // ラボ
            foreach (var p in DarumaMatch.Current.Players)
            {
                if (p.Character == m_character) return !p.IsCpu;
            }
            return false;
        }

        // ラボのボタンからも呼べる。発動できたら true
        public bool TryActivate()
        {
            if (m_useHammerStorm) return TryActivateStorm();
            if (m_gauge == null || !m_gauge.IsFull) return false;
            if (m_character == null || m_character.Stack == null || m_character.Stack.Count == 0) return false;
            if (!m_character.CanBeAffected) return false;
            if (m_character.Hammer != null && m_character.Hammer.IsBusy) return false;

            int cutInBlocks = m_character.Stack.Count;
            var block = m_character.Stack.RemoveAt(0);
            if (block == null) return false;
            m_gauge.Consume();
            m_activeUntil = Time.time + m_trailDuration;   // 必殺技ゲージ用(中平)

            Vector3 direction = m_character.transform.forward;
            Vector3 center = m_character.transform.position + Vector3.up * (TumikiBlock.kHeight * 0.5f);
            Vector3 position = center + direction * (DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.05f);
            bool embedded = Nakagawa.TumikiPlacement.ResolveEmbed(center, ref position);
            block.transform.position = position;
            block.Launch(direction, m_speed, m_character);
            if (embedded) Nakagawa.TumikiPlacement.IgnoreCharactersAfterEmbed(block);

            var tron = block.gameObject.AddComponent<TronProjectile>();
            tron.Begin(m_character, m_trailDuration, m_ownerGrace, m_hitInterval, m_logSpecial);

            // ネット対戦用(suehara): クライアントでも同じ見た目を出す(当たり判定の TronProjectile はホストだけ)
            SpecialNet.Send(m_character, this, kNetActivate,
                new[] { position.x, position.y, position.z, direction.x, direction.z, cutInBlocks }, new[] { block });

            PlayActivateVisuals(block, position, cutInBlocks);

            if (m_logSpecial) Debug.Log("[あかだる] 必殺技『終焉の軌跡』発動", this);
            return true;
        }

        // 発動の見た目(ビーム・地面の焦げ・カットイン)。ホストとクライアントで同じものを出す
        void PlayActivateVisuals(TumikiBlock block, Vector3 position, int cutInBlocks)
        {
            // 見た目は通常の射撃と同じ AkadaruShotTrail をそのまま使う
            var shotTrail = block.GetComponent<AkadaruShotTrail>();
            if (shotTrail == null) shotTrail = block.gameObject.AddComponent<AkadaruShotTrail>();
            // 必殺技は通常より太く・白く・長く残るレーザーにする
            shotTrail.BeginSpecial(m_effectMaterial, m_effectMinSpeed, m_effectMaxSpeed, position);

            // 通った地面を一定時間焼け焦げさせる
            var scorchObject = new GameObject("TronScorch");
            scorchObject.AddComponent<TronScorchTrail>().Begin(shotTrail);

            // 発動カットイン(ゲームを一時停止、実モデルの3D演出)
            SpecialCutIn.Show(m_cutInSpecialName, m_cutInColor, m_effectMaterial,
                m_character, block, cutInBlocks);
        }

        //========================================
        // ハンマーの嵐(suehara)
        //========================================

        bool TryActivateStorm()
        {
            if (m_gauge == null || !m_gauge.IsFull) return false;
            if (m_character == null || !m_character.CanBeAffected) return false;
            if (m_storm != null) return false;   // 発動中は重ねられない
            if (m_character.Hammer != null && m_character.Hammer.IsBusy) return false;

            m_gauge.Consume();
            // ネット対戦用(suehara): クライアントでも同じ見た目を出す(当たり判定はホストだけ)
            SpecialNet.Send(m_character, this, kNetStorm, new[] { 0.0f });
            PlayStorm();

            if (m_logSpecial) Debug.Log($"[あかだる] 必殺技『{m_stormName}』発動", this);
            return true;
        }

        void PlayStorm()
        {
            var root = new GameObject("AkadaruHammerStorm");
            m_storm = root.AddComponent<AkadaruHammerStorm>();
            m_storm.Initialize(m_character, m_stormDuration, m_stormHitInterval, m_stormKnockSpeed, m_stormHammerScale, m_stormOrbitSpeed);
            m_stormActiveUntil = Time.time + 0.25f + Mathf.Max(0.1f, m_stormDuration);
            SpecialCutIn.Show(m_stormName, m_cutInColor, m_effectMaterial, m_character, null, m_character.Stack != null ? m_character.Stack.Count : 0);
        }

        private void OnDestroy()
        {
            if (m_storm != null) Destroy(m_storm.gameObject);
        }

        //========================================
        // ネット対戦用(suehara): クライアントで、ホストの発動と同じ見た目を出す
        //========================================

        const int kNetActivate = 0;
        const int kNetStorm = 1;

        public void NetReceive(int ev, float[] v, TumikiBlock[] blocks)
        {
            if (ev == kNetStorm)
            {
                if (m_storm == null) PlayStorm();
                return;
            }
            if (ev != kNetActivate || v.Length < 6 || blocks.Length < 1 || blocks[0] == null) return;
            var block = blocks[0];
            var position = new Vector3(v[0], v[1], v[2]);
            SpecialNet.MarkLaunched(block, new Vector3(v[3], 0.0f, v[4]));
            PlayActivateVisuals(block, position, Mathf.RoundToInt(v[5]));
        }

        // 試合中だけ、画面左下にゲージを出す(ラボはラボUI側で表示する)
        private void OnGUI()
        {
            SpecialGaugeHUD.Draw(m_gauge, m_character);
        }
    }
}
