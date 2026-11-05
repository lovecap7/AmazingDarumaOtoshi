using Nakahira;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nakagawa
{
    // ハンマーで積み木を叩いたときのエフェクト
    //  ・薙ぎ払いで積み木を弾いた / 飛んできた積み木を打ち返した(DarumaHammer.Hit)
    //  ・股抜きショットで積み木を打ち出した(DarumaHammer.ShotFired)
    // 火花の色は叩いた人のプレイヤーの色(PlayerOutlines)を混ぜる。打ち返すほど大きくなる
    // DarumaMatch と同じ GameObject に付ける。シーンに無ければ試合のシーンを読んだときに自動で付く(AutoAttach)
    public class HammerHitEffect : MonoBehaviour
    {
        public static bool AutoAttach = true;

        [Header("薙ぎ払いで積み木を叩いた")]
        [SerializeField] GimmickBurstSettings m_sweepHit = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 0.95f, 0.7f, 1.0f), ColorB = new Color(1.0f, 0.7f, 0.25f, 1.0f),
            Count = 20, Speed = new Vector2(4.0f, 9.0f), Size = new Vector2(0.06f, 0.16f), Lifetime = new Vector2(0.15f, 0.35f),
            Gravity = 0.6f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.15f, 0, 0), Stretch = true,
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.9f, 0.6f, 0.9f), ShockwaveRadius = 1.1f, ShockwaveTime = 0.22f,
        };
        // 打ち返したとき(打ち返した回数ぶん大きくする)
        [SerializeField] GimmickBurstSettings m_returnHit = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 1.0f, 1.0f, 1.0f), ColorB = new Color(1.0f, 0.55f, 0.2f, 1.0f),
            Count = 32, Speed = new Vector2(6.0f, 12.0f), Size = new Vector2(0.08f, 0.2f), Lifetime = new Vector2(0.2f, 0.45f),
            Gravity = 0.4f, Shape = GimmickBurstShape.Sphere, ShapeSize = new Vector3(0.2f, 0, 0), Stretch = true,
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.95f, 0.75f, 1.0f), ShockwaveRadius = 1.6f, ShockwaveTime = 0.28f,
        };
        [SerializeField, Min(0.0f)] float m_returnScalePerCount = 0.15f;
        [SerializeField, Min(1.0f)] float m_returnMaxScale = 1.8f;

        [Header("股抜きショット")]
        // 打ち出す向きへ円錐状に飛ぶ
        [SerializeField] GimmickBurstSettings m_shot = new GimmickBurstSettings
        {
            ColorA = new Color(1.0f, 0.95f, 0.7f, 1.0f), ColorB = new Color(1.0f, 0.75f, 0.3f, 1.0f),
            Count = 16, Speed = new Vector2(4.0f, 8.0f), Size = new Vector2(0.05f, 0.14f), Lifetime = new Vector2(0.15f, 0.3f),
            Shape = GimmickBurstShape.Cone, ShapeSize = new Vector3(0.2f, 0, 0), ConeAngle = 30.0f, Stretch = true,
            Shockwave = true, ShockwaveColor = new Color(1.0f, 0.9f, 0.6f, 0.8f), ShockwaveRadius = 0.8f, ShockwaveTime = 0.2f,
        };

        [Header("色")]
        // 叩いた人のプレイヤーの色を混ぜる割合(0 なら設定の色のまま)
        [SerializeField, Range(0.0f, 1.0f)] float m_playerTint = 0.45f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            AttachToMatch();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachToMatch();

        static void AttachToMatch()
        {
            if (!AutoAttach) return;
            var match = DarumaMatch.Current != null ? DarumaMatch.Current : FindAnyObjectByType<DarumaMatch>();
            if (match == null || match.GetComponent<HammerHitEffect>() != null) return;
            match.gameObject.AddComponent<HammerHitEffect>();
        }

        private void OnEnable()
        {
            DarumaHammer.Hit += OnHammerHit;
            DarumaHammer.ShotFired += OnShotFired;
        }

        private void OnDisable()
        {
            DarumaHammer.Hit -= OnHammerHit;
            DarumaHammer.ShotFired -= OnShotFired;
        }

        private void OnHammerHit(HammerHitInfo info)
        {
            if (!(info.Target is TumikiBlock)) return;
            Color tint = Tint(info.Owner);
            if (info.ReturnCount > 0)
            {
                float scale = Mathf.Min(m_returnMaxScale, 1.0f + m_returnScalePerCount * (info.ReturnCount - 1));
                GimmickBurst.Play(m_returnHit, info.Point, Quaternion.identity, scale, tint);
            }
            else GimmickBurst.Play(m_sweepHit, info.Point, Quaternion.identity, 1.0f, tint);
        }

        private void OnShotFired(HammerHitInfo info)
        {
            var block = info.Target as TumikiBlock;
            Vector3 dir = block != null ? block.Direction : Vector3.zero;
            if (dir.sqrMagnitude < 0.0001f && info.Owner is Component c && c != null) dir = c.transform.forward;
            dir.y = 0.0f;
            Quaternion rot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir.normalized) : Quaternion.identity;
            GimmickBurst.Play(m_shot, info.Point, rot, 1.0f, Tint(info.Owner));
        }

        // 設定の色に掛ける色(白とプレイヤーの色の間)
        private Color Tint(IDarumaAttacker owner)
        {
            var outlines = PlayerOutlines.Current;
            if (outlines == null || m_playerTint <= 0.0f) return Color.white;
            int index = outlines.IndexOfAttacker(owner);
            if (index < 0) return Color.white;
            return Color.Lerp(Color.white, outlines.ColorForIndex(index), m_playerTint);
        }
    }
}
