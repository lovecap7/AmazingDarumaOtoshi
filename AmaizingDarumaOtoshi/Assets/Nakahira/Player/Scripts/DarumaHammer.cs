using System.Collections.Generic;
using UnityEngine;

namespace Nakahira
{
    // ハンマー。このコンポーネントが付いたTransformを回転の支点として振る
    // 子に「前方(+Z)へ伸びる」ハンマーの見た目を置く
    //  ・薙ぎ払い     : 前方を右から左へ水平に振る。ハンマーが対象の位置を通過した瞬間に当たり、その角度へ飛ばす
    //                   飛んできた積み木は、先端で捉える(早振り)と左へ、根元で捉える(振り遅れ)と右へずれる
    //  ・股抜きショット: 後ろから振り下ろし、自分の最下段を向いている方向へ打ち出す(生存キャラクターのみ)
    // 持ち主(IDarumaAttacker)は親から自動で取得する
    // 振る速さ・弾速は HammerParams(キャラクターごとのScriptableObject)から毎回読む
    public class DarumaHammer : MonoBehaviour
    {
        [Header("共通")]
        [SerializeField] HammerParams m_params;
        // 支点からハンマーの頭までの長さ
        [SerializeField] float m_armLength = 1.2f;

        [Header("待機")]
        [SerializeField] Vector3 m_idlePosition = new Vector3(0.6f, 0.6f, 0.0f);
        [SerializeField] Vector3 m_idleEuler = new Vector3(-70.0f, 90.0f, 0.0f);

        [Header("薙ぎ払い")]
        [SerializeField] float m_sweepStartYaw = 110.0f;  // 右
        [SerializeField] float m_sweepEndYaw = -110.0f;   // 左
        [SerializeField] float m_sweepHeight = 0.25f;     // 最下段の中心の高さ
        // ハンマーの頭の半分の太さ(届く距離と、当たりの角度の余裕に使う)
        [SerializeField] float m_hammerHalfWidth = 0.2f;
        // 対象の中心の高さがこれ以上ずれていたら当たらない
        [SerializeField] float m_hitHeightRange = 0.6f;
        // 打ち返す方向の最大角度(正面からの左右)
        [SerializeField] float m_maxReturnAngle = 60.0f;
        // 飛んできた積み木を打ち返すとき、ハンマーの先端(早振り)で左へ、根元(振り遅れ)で右へずらす最大角度
        [SerializeField] float m_returnSpreadAngle = 40.0f;

        [Header("股抜きショット")]
        [SerializeField] float m_shotStartPitch = 210.0f; // 後ろ上
        [SerializeField] float m_shotEndPitch = 30.0f;    // 前下
        [SerializeField] float m_shotHitPitch = 90.0f;    // 真下(最下段に当たる角度)

        enum Action { None, Sweep, Shot }

        IDarumaAttacker m_owner;
        Transform m_root;              // 持ち主のTransform
        DarumaCharacter m_character;   // 生存キャラクターの場合のみ(幽霊はnull)

        Action m_action = Action.None;
        float m_time;
        float m_prevSweepYaw;          // 前のステップでのハンマーの角度
        bool m_shotFired;
        readonly HashSet<Object> m_hitThisSwing = new HashSet<Object>();
        readonly Collider[] m_overlaps = new Collider[32];

        public bool IsBusy => m_action != Action.None;
        // 当たり判定が届く距離(見た目のハンマーの長さ × 倍率)
        float HitReach => (m_armLength + m_hammerHalfWidth) * m_params.HitReachScale;
        public bool IsShooting => m_action == Action.Shot;

        private void Awake()
        {
            m_owner = GetComponentInParent<IDarumaAttacker>();
            m_root = m_owner.transform;
            m_character = m_owner as DarumaCharacter;
        }

        public bool TrySweep()
        {
            if (IsBusy) return false;
            m_action = Action.Sweep;
            m_time = 0.0f;
            m_prevSweepYaw = m_sweepStartYaw;
            m_hitThisSwing.Clear();
            return true;
        }

        public bool TryShot()
        {
            if (IsBusy || m_character == null || m_character.Stack.Count == 0) return false;
            // ジャンプ中(空中)・ジャンプの実行待ちの間は撃てない
            if (!m_character.CanStartShot) return false;
            m_action = Action.Shot;
            m_time = 0.0f;
            m_shotFired = false;
            return true;
        }

        private void FixedUpdate()
        {
            if (m_action == Action.None) return;

            // 持ち主がヒットストップ中ならハンマーもスローになる
            m_time += Time.fixedDeltaTime * m_owner.LocalTimeScale;

            if (m_action == Action.Sweep)
            {
                DetectSweepHits();
                if (m_time >= m_params.SweepDuration) m_action = Action.None;
            }
            else if (m_action == Action.Shot)
            {
                if (!m_shotFired && CurrentShotPitch() <= m_shotHitPitch)
                {
                    m_shotFired = true;
                    FireShot();
                }
                if (m_time >= m_params.ShotDuration) m_action = Action.None;
            }
        }

        private void Update()
        {
            GetPose(out Vector3 pos, out Quaternion rot);
            transform.localPosition = pos;
            transform.localRotation = rot;
        }

        // 現在の支点の位置と回転(プレイヤーのローカル)
        private void GetPose(out Vector3 pos, out Quaternion rot)
        {
            switch (m_action)
            {
                case Action.Sweep:
                    pos = new Vector3(0.0f, m_sweepHeight, 0.0f);
                    rot = Quaternion.Euler(0.0f, CurrentSweepYaw(), 0.0f);
                    break;
                case Action.Shot:
                    // 頭が真下に来たときに最下段の高さになるよう支点を上げる
                    pos = new Vector3(0.0f, m_sweepHeight + m_armLength, 0.0f);
                    rot = Quaternion.Euler(CurrentShotPitch(), 0.0f, 0.0f);
                    break;
                default:
                    pos = m_idlePosition;
                    rot = Quaternion.Euler(m_idleEuler);
                    break;
            }
        }

        private float CurrentSweepYaw()
        {
            return Mathf.Lerp(m_sweepStartYaw, m_sweepEndYaw, Mathf.Clamp01(m_time / m_params.SweepDuration));
        }

        private float CurrentShotPitch()
        {
            return Mathf.Lerp(m_shotStartPitch, m_shotEndPitch, Mathf.Clamp01(m_time / m_params.ShotDuration));
        }

        //========================================
        // 薙ぎ払い
        //========================================

        // ハンマーがこのステップで通過した角度の範囲 [今の角度, 前の角度] に入った対象だけに当てる
        // (当たり判定を太くすると、ハンマーが届く前に横から触れて方向が狂うため)
        private void DetectSweepHits()
        {
            float yaw = CurrentSweepYaw();
            float prevYaw = m_prevSweepYaw;
            m_prevSweepYaw = yaw;

            Vector3 pivot = m_root.TransformPoint(new Vector3(0.0f, m_sweepHeight, 0.0f));
            float maxReach = HitReach + Mathf.Max(TumikiBlock.kRadius, DarumaCharacter.kBodyRadius);

            int count = Physics.OverlapSphereNonAlloc(pivot, maxReach, m_overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider col = m_overlaps[i];
                if (col.transform.IsChildOf(m_root)) continue;

                var block = col.GetComponentInParent<TumikiBlock>();
                if (block != null)
                {
                    if (block.CurrentState == TumikiBlock.State.Stacked || m_hitThisSwing.Contains(block)) continue;
                    if (!TryGetCrossing(block.transform.position, TumikiBlock.kRadius, yaw, prevYaw, out float bearing, out float dist)) continue;
                    m_hitThisSwing.Add(block);
                    HitBlock(block, bearing, dist);
                    continue;
                }

                var other = col.GetComponentInParent<DarumaCharacter>();
                if (other != null && !m_hitThisSwing.Contains(other))
                {
                    // キャラクターは最下段の中心の高さで判定する(ジャンプで避けられる)
                    Vector3 target = other.transform.position + Vector3.up * m_sweepHeight;
                    if (!TryGetCrossing(target, DarumaCharacter.kBodyRadius, yaw, prevYaw, out float bearing, out _)) continue;
                    m_hitThisSwing.Add(other);
                    HitCharacter(other, bearing);
                }
            }
        }

        // 対象がハンマーの通過範囲に入ったか。bearing: 持ち主から見た対象の角度(右が+)、dist: 水平距離
        private bool TryGetCrossing(Vector3 targetPos, float targetRadius, float yaw, float prevYaw, out float bearing, out float dist)
        {
            Vector3 local = m_root.InverseTransformPoint(targetPos);
            bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            dist = new Vector2(local.x, local.z).magnitude;

            if (Mathf.Abs(local.y - m_sweepHeight) > m_hitHeightRange) return false;
            if (dist > HitReach + targetRadius) return false;

            // ハンマーの太さの分だけ角度に余裕を持たせる
            float pad = Mathf.Atan2(m_hammerHalfWidth, Mathf.Max(dist, 0.01f)) * Mathf.Rad2Deg;
            return bearing >= yaw - pad && bearing <= prevYaw + pad;
        }

        private void HitBlock(TumikiBlock block, float bearing, float dist)
        {
            float angle = bearing;
            float speed = m_params.SweepLaunchSpeed;
            int returnCount = 0;

            if (block.CurrentState == TumikiBlock.State.Flying)
            {
                // 飛んできた積み木: ハンマーの先端で捉える(早振り)ほど左へ引っ張り、根元(振り遅れ)ほど右へ流す
                float near = DarumaCharacter.kBodyRadius + TumikiBlock.kRadius;
                float far = HitReach + TumikiBlock.kRadius;
                float t = Mathf.InverseLerp(near, far, dist);
                angle += Mathf.Lerp(m_returnSpreadAngle, -m_returnSpreadAngle, t);
                // 打ち返すほど速くなる
                returnCount = block.ReturnCount + 1;
                speed = Mathf.Min(m_params.SweepLaunchSpeed * Mathf.Pow(m_params.ReturnSpeedRate, returnCount), m_params.MaxProjectileSpeed);
            }

            block.Launch(ToWorldDirection(angle), speed, m_owner, null, returnCount);
            // 打ち返した積み木はしばらく自分に当たらない(壁や連鎖で跳ね返ってきても安全)
            if (returnCount > 0) block.ExtendIgnore(m_params.ReturnImmunityTime);
            BeginHitStop(block, null, returnCount);
        }

        private void HitCharacter(DarumaCharacter other, float bearing)
        {
            if (other.KnockBottom(ToWorldDirection(bearing), m_params.SweepLaunchSpeed, m_owner, out TumikiBlock knocked))
            {
                // 直接攻撃でダメージを与えた(幽霊なら復活する)
                m_owner.OnDealtDamage();
                BeginHitStop(knocked, other);
            }
            // 弾き出した積み木を同じスイングで二重に打たないようにする
            if (knocked != null) m_hitThisSwing.Add(knocked);
        }

        // 手ごたえ: 自分と、弾いた積み木・叩いた相手だけスローになって震える(相手側を強めに)
        // returnCount: 打ち返し回数。打ち返すたびにヒットストップが倍々で長くなる
        // 長いほど時間の倍率を小さくして、スロー中に進む量は通常のヒットストップと同じにする(長い間ほぼ止まって見える)
        private void BeginHitStop(TumikiBlock block, DarumaCharacter victim, int returnCount = 0)
        {
            float baseDuration = m_params.HitStopDuration;
            float duration = baseDuration;
            if (returnCount > 0)
            {
                duration = Mathf.Min(baseDuration * Mathf.Pow(m_params.ReturnHitStopGrowth, returnCount),
                    Mathf.Max(baseDuration, m_params.ReturnHitStopMax));
            }
            float scale = duration > 0.0f ? m_params.HitStopTimeScale * baseDuration / duration : m_params.HitStopTimeScale;
            m_owner.StartHitStop(duration, scale, m_params.SelfShake);
            if (block != null) block.StartHitStop(duration, scale, m_params.TargetShake);
            if (victim != null) victim.StartHitStop(duration, scale, m_params.TargetShake);
        }

        // 持ち主の正面からの角度 → ワールド方向(最大角度で制限)
        private Vector3 ToWorldDirection(float angle)
        {
            angle = Mathf.Clamp(angle, -m_maxReturnAngle, m_maxReturnAngle);
            return m_root.rotation * Quaternion.Euler(0.0f, angle, 0.0f) * Vector3.forward;
        }

        //========================================
        // 股抜きショット
        //========================================

        private void FireShot()
        {
            if (m_character == null || m_character.Stack.Count == 0) return;

            Transform root = m_root;
            TumikiBlock block = m_character.Stack.RemoveAt(0);
            block.transform.position = root.position
                + root.forward * (DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.05f)
                + Vector3.up * (TumikiBlock.kHeight * 0.5f);
            block.Launch(root.forward, m_params.ShotSpeed, m_owner);
            BeginHitStop(block, null);
        }
    }
}
