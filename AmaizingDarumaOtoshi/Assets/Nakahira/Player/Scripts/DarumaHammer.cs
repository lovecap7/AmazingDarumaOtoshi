using System.Collections.Generic;
using UnityEngine;

namespace Nakahira
{
    // ハンマーの薙ぎ払いが当たったときの情報(DarumaHammer.Hit)
    public readonly struct HammerHitInfo
    {
        public readonly DarumaHammer Hammer;
        // ハンマーの持ち主
        public readonly IDarumaAttacker Owner;
        // 当たった相手(TumikiBlock または DarumaCharacter)
        public readonly Component Target;
        public readonly Vector3 Point;
        // 相手にダメージを与えたか(キャラクターのみ。無敵中はfalse)
        public readonly bool Damaged;
        // 飛んできた積み木を打ち返した回数(打ち返しでなければ0)
        public readonly int ReturnCount;

        public HammerHitInfo(DarumaHammer hammer, IDarumaAttacker owner, Component target, Vector3 point, bool damaged, int returnCount)
        {
            Hammer = hammer;
            Owner = owner;
            Target = target;
            Point = point;
            Damaged = damaged;
            ReturnCount = returnCount;
        }
    }

    // ハンマーの薙ぎ払いが終わったときの情報(DarumaHammer.SweepFinished)
    public readonly struct HammerSweepInfo
    {
        public readonly DarumaHammer Hammer;
        public readonly IDarumaAttacker Owner;
        // この一振りで当たった対象の数(0 なら空振り)
        public readonly int HitCount;

        public HammerSweepInfo(DarumaHammer hammer, IDarumaAttacker owner, int hitCount)
        {
            Hammer = hammer;
            Owner = owner;
            HitCount = hitCount;
        }
    }

    // ハンマー。このコンポーネントが付いたTransformを回転の支点として振る
    // 子に「前方(+Z)へ伸びる」ハンマーの見た目を置く
    //  ・薙ぎ払い     : 前方を右から左へ水平に振る。ハンマーが対象の位置を通過した瞬間に当たり、その角度へ飛ばす
    //                   出だしはゆっくり(タメ)で、振り抜くほど速くなる(HammerParams.SweepEasePower)
    //                   振り始めから SweepComboWindow 秒の間に押し直すと、振り終わったあと左から右へ切り返す(何回でも往復する)
    //                   押さなければ振り終わりの位置で待ち、受付が終わると待機の位置へ戻る(待っている間は動ける)
    //                   はじき返しアシスト(HammerParams.DeflectAssist): 振っている間、攻撃判定のある積み木が振る軌道のどこかに入ったら
    //                   すぐにはじき返し、ハンマーをその角度へ移してそこから振り抜く(幽霊のハンマーはオフ)
    //                   飛んできた積み木は、既定では向いている向きへ打ち返す(HammerParams.ReturnAimMode で
    //                   「先端で捉える(早振り)と左へ、根元で捉える(振り遅れ)と右へずれる」従来の打ち方に切り替えられる)
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
        // 薙ぎ払いの間、柄を軸にハンマーをひねる角度(頭の長い向きを振る向きにそろえる。見た目だけ)
        [SerializeField] float m_sweepRoll = 90.0f;
        // ハンマーの頭の半分の太さ(届く距離と、当たりの角度の余裕に使う)
        [SerializeField] float m_hammerHalfWidth = 0.2f;
        // 対象の中心の高さがこれ以上ずれていたら当たらない
        [SerializeField] float m_hitHeightRange = 0.6f;
        // 打ち返す方向の最大角度(正面からの左右)
        [SerializeField] float m_maxReturnAngle = 60.0f;
        // 飛んできた積み木を打ち返すとき、ハンマーの先端(早振り)で左へ、根元(振り遅れ)で右へずらす最大角度(ReturnAimMode が HammerAngle のとき)
        [SerializeField] float m_returnSpreadAngle = 40.0f;

        [Header("股抜きショット")]
        [SerializeField] float m_shotStartPitch = 210.0f; // 後ろ上
        [SerializeField] float m_shotEndPitch = 30.0f;    // 前下
        [SerializeField] float m_shotHitPitch = 90.0f;    // 真下(最下段に当たる角度)

        // SweepHold: 振り終わりの位置で切り返しを待つ / SweepReturn: 待機の位置へ戻る(どちらも動作中ではない)
        enum Action { None, Sweep, Shot, SweepHold, SweepReturn }

        IDarumaAttacker m_owner;
        Transform m_root;              // 持ち主のTransform
        DarumaCharacter m_character;   // 生存キャラクターの場合のみ(幽霊はnull)

        Action m_action = Action.None;
        float m_time;
        float m_prevSweepYaw;          // 前のステップでのハンマーの角度
        bool m_shotFired;
        bool m_reverse;                // 今の(最後の)薙ぎ払いが左から右への切り返しか
        bool m_comboQueued;            // 振っている間に次の横振りが押された
        float m_returnTime;            // 待機の位置へ戻り始めてからの時間
        int m_sweepCount;
        // はじき返しアシストでハンマーを移したとき: 移した角度と、そのときの進み具合(このあとはここから振り終わりへ振る)
        bool m_snapped;
        float m_snapYaw;
        float m_snapProgress;
        // はじき返した積み木。ヒットストップの間は、ハンマーがこの積み木を押していくように追いかける
        TumikiBlock m_snapBlock;
        float m_snapTouch;
        readonly HashSet<Object> m_hitThisSwing = new HashSet<Object>();
        // 空中で振ると調べる範囲が縦に伸びるので、床・壁で埋まらないように多めに持つ
        readonly Collider[] m_overlaps = new Collider[64];

        // 薙ぎ払いが積み木・キャラクターに当たった(カメラの揺れ・効果音など演出用)
        public static event System.Action<HammerHitInfo> Hit;
        // 薙ぎ払いが終わった(当たらなかった=空振りの判定用)
        public static event System.Action<HammerSweepInfo> SweepFinished;
        // 股抜きショットで積み木を打ち出した(演出用。Target は打ち出した積み木、Point は打ち出した位置)
        public static event System.Action<HammerHitInfo> ShotFired;
        // ハンマー以外の技(あかだるの必殺技の回るハンマーなど)が当たったことを、薙ぎ払いと同じ知らせで出す(演出・音・レベル用。suehara)
        public static void RaiseHit(HammerHitInfo info) => Hit?.Invoke(info);
        // 支点からハンマーの頭までの長さ(見た目。suehara)
        public float ArmLength => m_armLength;

        public bool IsBusy => m_action == Action.Sweep || m_action == Action.Shot;
        // このハンマーの性能。キャラクターのパッシブが、実行時に複製へ差し替えるために使う
        public HammerParams Params { get => m_params; set => m_params = value; }
        // 当たり判定が届く距離(見た目のハンマーの長さ × 倍率)
        float HitReach => (m_armLength + m_hammerHalfWidth) * m_params.HitReachScale;
        // 持ち主が空中にいるとき、当たる高さを足元より下へ広げる深さ(ジャンプの頂点からでも地上の相手に届く)。地上・幽霊は 0
        float AirHitDepth => m_character != null && !m_character.IsGrounded ? m_character.JumpParams.Height : 0.0f;
        public bool IsShooting => m_action == Action.Shot;
        public bool IsSweeping => m_action == Action.Sweep;
        // 今の動作(薙ぎ払い・股抜きショット)を始めてから進んだ時間(秒)。ボタンを押したフレームではまだ 0
        public float ActionTime => m_time;
        // 薙ぎ払いにかかる時間(秒)
        public float SweepDuration => m_params.SweepDuration;
        // 股抜きショットの弾速
        public float ShotSpeed => m_params.ShotSpeed;
        // 薙ぎ払いで落ちている積み木を弾いたときの弾速
        public float SweepLaunchSpeed => m_params.SweepLaunchSpeed;
        // 薙ぎ払いで弾いた積み木を飛ばせる向きの最大角度(度。持ち主の正面から左右)
        public float MaxLaunchAngle => m_maxReturnAngle;
        // 足元からこの高さにある物(中心)に、薙ぎ払いが当たるか
        public bool IsAtSweepHeight(float heightFromFeet) => Mathf.Abs(heightFromFeet - m_sweepHeight) <= m_hitHeightRange;
        // 股抜きショットを振り始めてから弾が出るまでの時間(秒)
        public float ShotFireDelay => m_params.ShotDuration * Mathf.Clamp01((m_shotStartPitch - m_shotHitPitch) / (m_shotStartPitch - m_shotEndPitch));
        // 薙ぎ払いがキャラクターに当たる、中心同士の水平の距離(届く距離 + 体の半径)
        public float CharacterHitDistance => HitReach + DarumaCharacter.kBodyRadius;
        // 薙ぎ払いが積み木に当たる、中心同士の水平の距離(届く距離 + 積み木の半径)
        public float BlockHitDistance => HitReach + TumikiBlock.kRadius;
        // 薙ぎ払いで振り始める角度・振り終わる角度(度。持ち主の正面から、右が +)
        public float SweepStartYaw => m_sweepStartYaw;
        public float SweepEndYaw => m_sweepEndYaw;
        // 今の(最後の)薙ぎ払いが、左から右への切り返しか
        public bool IsReverseSweep => m_reverse;
        // 今、横振りを押したら切り返し(左から右)になるか(振り終わりの位置で待っているとき)
        public bool NextSweepIsReverse => m_action == Action.SweepHold && !m_reverse;
        // 薙ぎ払いを始めた回数(切り返しも 1 回と数える。新しい振りを見分ける用)
        public int SweepCount => m_sweepCount;
        // 今のハンマーの角度(度。持ち主の正面から、右が +。薙ぎ払い中・切り返しを待っている間)
        public float SweepYaw => m_action == Action.SweepHold || m_action == Action.SweepReturn ? SweepToYaw : CurrentSweepYaw();
        // 薙ぎ払いを振り始めてから、ハンマーがこの角度を通るまでの時間(秒)。reverse は切り返し(左から右)
        public float SweepTimeAtYaw(float yaw, bool reverse = false)
        {
            float from = reverse ? m_sweepEndYaw : m_sweepStartYaw;
            float to = reverse ? m_sweepStartYaw : m_sweepEndYaw;
            float f = Mathf.Clamp01((yaw - from) / (to - from));
            return m_params.SweepDuration * Mathf.Pow(f, 1.0f / Mathf.Max(1.0f, m_params.SweepEasePower));
        }
        // 今振っている薙ぎ払いが、この角度を通るとき(振り始めからの秒数。ActionTime と比べる)
        public float CurrentSweepTimeAtYaw(float yaw) => SweepTimeAtYaw(yaw, m_reverse);
        // 股抜きショットを振り始めてから弾が出るまでの間(持ち主は向きを変えて狙いを調整できる)
        public bool IsAimingShot => m_action == Action.Shot && !m_shotFired;

        private void Awake()
        {
            m_owner = GetComponentInParent<IDarumaAttacker>();
            m_root = m_owner.transform;
            m_character = m_owner as DarumaCharacter;
        }

        // ネット対戦のクライアント用: 振る見た目だけ動かし、当たり判定・弾の発射はしない(結果はホストから届く)
        // ローカルプレイでは常に false
        public bool ViewOnly { get; set; }

        // ネット対戦のクライアント用: ホストで始まった動作の見た目を最初から再生する
        // reverse: 左から右への切り返し(IsReverseSweep)
        public void PlayVisual(bool shot, bool reverse = false)
        {
            if (!shot)
            {
                StartSweep(reverse);
                return;
            }
            m_action = Action.Shot;
            m_time = 0.0f;
            m_shotFired = false;
            m_hitThisSwing.Clear();
        }

        // 振っている途中なら、振り終わってから切り返す(振り始めから SweepComboWindow 秒まで受け付ける)
        // 振り終わりの位置で待っているなら、すぐに切り返す
        public bool TrySweep()
        {
            switch (m_action)
            {
                case Action.Shot:
                    return false;
                case Action.Sweep:
                    if (m_time > m_params.SweepComboWindow) return false;
                    m_comboQueued = true;
                    return true;
                case Action.SweepHold:
                    StartSweep(!m_reverse);
                    return true;
                default:
                    StartSweep(false);
                    return true;
            }
        }

        private void StartSweep(bool reverse)
        {
            m_action = Action.Sweep;
            m_time = 0.0f;
            m_reverse = reverse;
            m_comboQueued = false;
            m_prevSweepYaw = SweepFromYaw;
            m_snapped = false;
            m_snapBlock = null;
            m_hitThisSwing.Clear();
            m_sweepCount++;
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
            float dt = Time.fixedDeltaTime * m_owner.LocalTimeScale;
            m_time += dt;

            if (m_action == Action.Sweep)
            {
                FollowSnapBlock();
                if (!ViewOnly) DetectSweepHits();
                if (m_time >= m_params.SweepDuration)
                {
                    // 当たった対象が0なら空振り。見た目だけの再生(ネットのクライアント)では数えない
                    if (!ViewOnly) SweepFinished?.Invoke(new HammerSweepInfo(this, m_owner, m_hitThisSwing.Count));
                    // 振り終わりの位置で切り返しを待つ(押してあった分は次のステップで切り返す。
                    // 1 ステップだけ動作中でなくなるので、振り始めを見ている効果音などが 2 回目以降も気づける)
                    m_action = Action.SweepHold;
                }
            }
            else if (m_action == Action.SweepHold)
            {
                if (m_comboQueued) StartSweep(!m_reverse);
                else if (m_time > m_params.SweepComboWindow)
                {
                    m_action = Action.SweepReturn;
                    m_returnTime = 0.0f;
                }
            }
            else if (m_action == Action.SweepReturn)
            {
                m_returnTime += dt;
                if (m_returnTime >= m_params.SweepReturnTime) m_action = Action.None;
            }
            else if (m_action == Action.Shot)
            {
                if (!m_shotFired && CurrentShotPitch() <= m_shotHitPitch)
                {
                    m_shotFired = true;
                    if (!ViewOnly) FireShot();
                }
                if (m_time >= m_params.ShotDuration) m_action = Action.None;
            }
        }

        // 持ち主の体の見た目の動き(DarumaMotion・DarumaGhostMotion)。支点の位置に体の揺れを足す(傾けない)
        // 見た目だけ(当たり判定は持ち主の位置から計算しているので変わらない)。null なら揺らさない
        public IDarumaBodyMotion BodyMotion { get; set; }

        private void Update()
        {
            GetPose(out Vector3 pos, out Quaternion rot);
            if (BodyMotion != null) pos += BodyMotion.GetOffset(pos.y);
            transform.localPosition = pos;
            transform.localRotation = rot;
            UpdateTrail();
        }

        private void OnDestroy()
        {
            if (m_trail != null) Destroy(m_trail.gameObject);
        }

        // 振っている間、ハンマーの頭からトレイルを出す(プレハブは HammerParams の「トレイル」)
        private void UpdateTrail()
        {
            HammerTrail prefab = m_params != null ? m_params.TrailPrefab : null;
            // 再生中にプレハブを差し替えたら作り直す
            if (prefab != m_trailSource)
            {
                if (m_trail != null) Destroy(m_trail.gameObject);
                m_trail = null;
                m_trailSource = prefab;
                if (prefab != null)
                {
                    m_trail = Instantiate(prefab);
                    m_trail.name = prefab.name + "_" + (m_root != null ? m_root.name : name);
                }
            }
            if (m_trail == null) return;
            m_trail.Tick(IsBusy, transform.TransformPoint(0.0f, 0.0f, m_armLength));
        }

        HammerTrail m_trail;
        HammerTrail m_trailSource;

        // 現在の支点の位置と回転(プレイヤーのローカル)
        private void GetPose(out Vector3 pos, out Quaternion rot)
        {
            switch (m_action)
            {
                case Action.Sweep:
                    SweepPose(CurrentSweepYaw(), out pos, out rot);
                    break;
                case Action.SweepHold:
                    SweepPose(SweepToYaw, out pos, out rot);
                    break;
                case Action.SweepReturn:
                {
                    // 振り終わりの位置から待機の位置へ、なめらかに戻す
                    SweepPose(SweepToYaw, out Vector3 from, out Quaternion fromRot);
                    float t = m_params.SweepReturnTime > 0.0f ? Mathf.SmoothStep(0.0f, 1.0f, m_returnTime / m_params.SweepReturnTime) : 1.0f;
                    pos = Vector3.Lerp(from, m_idlePosition, t);
                    rot = Quaternion.Slerp(fromRot, Quaternion.Euler(m_idleEuler), t);
                    break;
                }
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

        private void SweepPose(float yaw, out Vector3 pos, out Quaternion rot)
        {
            pos = new Vector3(0.0f, m_sweepHeight, 0.0f);
            // 頭の長い向きを振る面にそろえる(切り返しでも同じひねりのまま。逆にすると切り返しの瞬間に頭がくるっと回る)
            rot = Quaternion.Euler(0.0f, yaw, 0.0f) * Quaternion.Euler(0.0f, 0.0f, m_sweepRoll);
        }

        // 今の薙ぎ払いの振り始め・振り終わりの角度(切り返しは左から右)
        private float SweepFromYaw => m_reverse ? m_sweepEndYaw : m_sweepStartYaw;
        private float SweepToYaw => m_reverse ? m_sweepStartYaw : m_sweepEndYaw;

        // 出だしはゆっくり、振り抜くほど速い(進み具合 = 時間の割合 ^ SweepEasePower)
        // はじき返しアシストでハンマーを移したあとは、移した角度から振り終わりへ、残りの時間で同じ速さの変わり方で振る
        private float CurrentSweepYaw() => SweepYawAt(m_time);

        // 振り始めから time 秒のハンマーの角度(今の振りのまま進んだとき)
        private float SweepYawAt(float time)
        {
            float progress = SweepProgress(time);
            if (!m_snapped) return Mathf.Lerp(SweepFromYaw, SweepToYaw, progress);
            float rest = 1.0f - m_snapProgress;
            float q = rest > 1e-4f ? Mathf.Clamp01((progress - m_snapProgress) / rest) : 1.0f;
            return Mathf.Lerp(m_snapYaw, SweepToYaw, q);
        }

        // 振り始めからの進み具合(0〜1。出だしがゆっくり)
        private float SweepProgress() => SweepProgress(m_time);

        private float SweepProgress(float time)
        {
            float p = Mathf.Clamp01(time / m_params.SweepDuration);
            return Mathf.Pow(p, Mathf.Max(1.0f, m_params.SweepEasePower));
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

            // 空中で振ったときは足元の相手も調べる(地上では球と同じ範囲)
            Vector3 pivotBottom = pivot + Vector3.down * AirHitDepth;
            int count = Physics.OverlapCapsuleNonAlloc(pivot, pivotBottom, maxReach, m_overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider col = m_overlaps[i];
                if (col.transform.IsChildOf(m_root)) continue;

                var block = col.GetComponentInParent<TumikiBlock>();
                if (block != null)
                {
                    if (block.CurrentState == TumikiBlock.State.Stacked || m_hitThisSwing.Contains(block)) continue;
                    // 攻撃判定のある積み木は、軌道のどこに入ってもはじき返す(アシストがオンのとき)
                    if (UseDeflectAssist && block.IsHarmful)
                    {
                        TryDeflect(block);
                        continue;
                    }
                    if (!TryGetCrossing(block.transform.position, TumikiBlock.kRadius, yaw, prevYaw, out float bearing, out float dist)) continue;
                    m_hitThisSwing.Add(block);
                    HitBlock(block, bearing, dist);
                    continue;
                }

                var other = col.GetComponentInParent<DarumaCharacter>();
                if (other != null && !m_hitThisSwing.Contains(other))
                {
                    if (!CanHitCharacter(other)) continue;
                    // キャラクターは最下段の中心の高さで判定する
                    Vector3 target = other.transform.position + Vector3.up * m_sweepHeight;
                    if (!TryGetCrossing(target, DarumaCharacter.kBodyRadius, yaw, prevYaw, out float bearing, out _)) continue;
                    m_hitThisSwing.Add(other);
                    HitCharacter(other, bearing);
                }
            }
        }

        // 持ち主と相手の状態から、この相手に薙ぎ払いが当たるか
        //  空中で振ったとき: 積み木を1個も積んでいない相手には当たらない
        //  地上で振ったとき: 空中にいる相手には当たらない
        //  幽霊が振ったとき: 制限なし
        private bool CanHitCharacter(DarumaCharacter other)
        {
            if (m_character == null) return true;
            return m_character.IsGrounded ? other.IsGrounded : other.Stack.Count > 0;
        }

        // 対象がハンマーの通過範囲に入ったか。bearing: 持ち主から見た対象の角度(右が+)、dist: 水平距離
        private bool TryGetCrossing(Vector3 targetPos, float targetRadius, float yaw, float prevYaw, out float bearing, out float dist)
        {
            Vector3 local = m_root.InverseTransformPoint(targetPos);
            bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            dist = new Vector2(local.x, local.z).magnitude;

            // 下だけ、空中で振ったときに広げる
            float heightDiff = local.y - m_sweepHeight;
            if (heightDiff > m_hitHeightRange || heightDiff < -m_hitHeightRange - AirHitDepth) return false;
            if (dist > HitReach + targetRadius) return false;

            // ハンマーの太さの分だけ角度に余裕を持たせる(切り返しは角度が増える向きに通る)
            float pad = Mathf.Atan2(m_hammerHalfWidth, Mathf.Max(dist, 0.01f)) * Mathf.Rad2Deg;
            float lo = Mathf.Min(yaw, prevYaw);
            float hi = Mathf.Max(yaw, prevYaw);
            return bearing >= lo - pad && bearing <= hi + pad;
        }

        // 薙ぎ払いの最中に、攻撃判定のある積み木が持ち主の体に当たりそうになった(TumikiBlock から呼ぶ)
        // ハンマーがまだ通過していない側にあって、このまま振れば打ち返せていた積み木なら、体に当たる前に打ち返したことにする
        // (ハンマーが届く前に体へ当たって段が抜け、抜けた段を打ち返す「一段壊れながら打ち返す」をなくす)。打ち返したら true
        public bool TryReturnBeforeBodyHit(TumikiBlock block)
        {
            if (ViewOnly || m_action != Action.Sweep || block == null || !block.IsHarmful) return false;
            if (m_hitThisSwing.Contains(block)) return false;
            // アシストがオンなら、軌道のどこにあってもはじき返す
            if (UseDeflectAssist) return TryDeflect(block);

            Vector3 local = m_root.InverseTransformPoint(block.transform.position);
            float bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float dist = new Vector2(local.x, local.z).magnitude;
            float heightDiff = local.y - m_sweepHeight;
            if (heightDiff > m_hitHeightRange || heightDiff < -m_hitHeightRange - AirHitDepth) return false;
            if (dist > HitReach + TumikiBlock.kRadius) return false;

            // もうすぐ通過する角度の範囲(今の角度から BodyHitReturnGrace 秒後の角度まで)に入っているか
            // (ふだんは右から左で角度が減っていく。切り返しは左から右で角度が増えていく)
            // 振り終わりまで全部にすると、出だしがゆっくりな振りでは連打でほぼ必ず打ち返せてしまうので、直前だけにする
            float pad = Mathf.Atan2(m_hammerHalfWidth, Mathf.Max(dist, 0.01f)) * Mathf.Rad2Deg;
            float yaw = CurrentSweepYaw();
            float soon = SweepYawAt(m_time + m_params.BodyHitReturnGrace);
            float lo = Mathf.Min(yaw, soon);
            float hi = Mathf.Max(yaw, soon);
            if (bearing < lo - pad || bearing > hi + pad) return false;

            m_hitThisSwing.Add(block);
            HitBlock(block, bearing, dist);
            return true;
        }

        // はじき返しアシストを使うか(幽霊のハンマーは HammerParams でオフにしてある)
        // 効くのは振り始めから DeflectAssistTime 秒まで(連打で受付が途切れないことがないように)
        private bool UseDeflectAssist => m_params.DeflectAssist && m_time <= m_params.DeflectAssistTime;

        // 攻撃判定のある積み木が、振る軌道の扇形(振り始め〜振り終わり。届く距離・高さは今と同じ)に入っていれば、
        // ハンマーをその角度へ移してはじき返す。はじき返したら true
        private bool TryDeflect(TumikiBlock block)
        {
            Vector3 local = m_root.InverseTransformPoint(block.transform.position);
            float bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float dist = new Vector2(local.x, local.z).magnitude;
            float heightDiff = local.y - m_sweepHeight;
            if (heightDiff > m_hitHeightRange || heightDiff < -m_hitHeightRange - AirHitDepth) return false;
            if (dist > HitReach + TumikiBlock.kRadius) return false;

            float pad = Mathf.Atan2(m_hammerHalfWidth, Mathf.Max(dist, 0.01f)) * Mathf.Rad2Deg;
            float lo = Mathf.Min(m_sweepStartYaw, m_sweepEndYaw);
            float hi = Mathf.Max(m_sweepStartYaw, m_sweepEndYaw);
            if (bearing < lo - pad || bearing > hi + pad) return false;

            // 当たり判定は見た目のハンマーより遠くまで届くので、積み木をハンマーの頭の届く距離まで寄せる
            // (速い積み木ほど判定の外側で止まり、ハンマーから離れた所でヒットストップして見えるため)
            float minDist = DarumaCharacter.kBodyRadius + TumikiBlock.kRadius;
            float contactDist = Mathf.Clamp(dist, minDist, Mathf.Max(minDist, m_armLength));
            if (!Mathf.Approximately(contactDist, dist)) MoveBlockTo(block, bearing, contactDist, local.y);

            // ハンマーを、頭の面が積み木に触れる角度(振ってきた側へ、頭の半分の太さ + 積み木の半径だけずらす)へ移し、
            // そこから振り抜く(ヒットストップ中はほぼこの角度で止まって見える)
            float side = Mathf.Sign(SweepToYaw - SweepFromYaw);
            float touch = Mathf.Atan2(m_hammerHalfWidth + TumikiBlock.kRadius, contactDist) * Mathf.Rad2Deg;
            m_snapYaw = Mathf.Clamp(bearing - side * touch, lo, hi);
            m_snapProgress = SweepProgress();
            m_snapped = true;
            m_prevSweepYaw = m_snapYaw;
            m_snapBlock = block;
            m_snapTouch = touch;

            m_hitThisSwing.Add(block);
            HitBlock(block, bearing, contactDist);
            return true;
        }

        // はじき返した積み木がヒットストップ中なら、頭の面が触れたまま押していく角度へハンマーを進める
        // (積み木はスローで打ち返す向きへ進むので、止まったままだとハンマーにめり込んで見える)
        // 動かしたら、その角度・今の進み具合から振り抜き直す
        private void FollowSnapBlock()
        {
            if (m_snapBlock == null) return;
            if (!m_snapBlock.IsInHitStop || m_snapBlock.CurrentState != TumikiBlock.State.Flying)
            {
                m_snapBlock = null;
                return;
            }
            Vector3 local = m_root.InverseTransformPoint(m_snapBlock.transform.position);
            float bearing = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float side = Mathf.Sign(SweepToYaw - SweepFromYaw);
            float target = bearing - side * m_snapTouch;
            // 打ち返す向きによっては、積み木はハンマーが来た側へ進むので、どちら向きにも付いていく(軌道の外へは出さない)
            float lo = Mathf.Min(m_sweepStartYaw, m_sweepEndYaw);
            float hi = Mathf.Max(m_sweepStartYaw, m_sweepEndYaw);
            m_snapYaw = Mathf.Clamp(target, lo, hi);
            m_snapProgress = SweepProgress();
        }

        // 積み木を、持ち主から見て bearing の向き・水平の距離 dist の所へ移す(高さ localY はそのまま)。壁の中には入れない
        private void MoveBlockTo(TumikiBlock block, float bearing, float dist, float localY)
        {
            Vector3 origin = m_root.TransformPoint(new Vector3(0.0f, localY, 0.0f));
            float rad = bearing * Mathf.Deg2Rad;
            Vector3 pos = m_root.TransformPoint(new Vector3(Mathf.Sin(rad) * dist, localY, Mathf.Cos(rad) * dist));
            Nakagawa.TumikiPlacement.ResolveEmbed(origin, ref pos);
            block.transform.position = pos;
            if (block.Rigidbody != null) block.Rigidbody.position = pos;
        }

        private void HitBlock(TumikiBlock block, float bearing, float dist)
        {
            float angle = bearing;
            float speed = m_params.SweepLaunchSpeed;
            int returnCount = 0;
            int power = 1;
            // 打ち返す前の持ち主(=打ってきた相手)。打ち返した積み木はこの相手を追いかける(suehara)
            IDarumaAttacker shooter = block.Owner;

            // 攻撃判定のある積み木だけ「打ち返し」。攻撃判定のない動いている積み木は落ちている積み木と同じ扱い
            if (block.IsHarmful)
            {
                angle = ReturnAngle(bearing, dist);
                // 打ち返すほど速くなる
                returnCount = block.ReturnCount + 1;
                // 打ち返すたびに攻撃力が 1 上がる(当たった相手の段がその数だけ飛ぶ)
                power = block.AttackPower + 1;
                speed = Mathf.Min(m_params.SweepLaunchSpeed * Mathf.Pow(m_params.ReturnSpeedRate, returnCount), m_params.MaxProjectileSpeed);
            }

            block.Launch(ToWorldDirection(angle), speed, m_owner, null, returnCount);
            block.SetAttackPower(power);
            // 打ち返した積み木はしばらく自分に当たらない(壁や連鎖で跳ね返ってきても安全)
            if (returnCount > 0) block.ExtendIgnore(m_params.ReturnImmunityTime);
            if (returnCount > 0) BeginReturnHoming(block, shooter);
            BeginHitStop(block, null, returnCount);
            // 弾いたステップから、すぐにスローの速さにする(ハンマーに触れた位置で止まって見えるように)
            block.SyncFlyingVelocity();
            Hit?.Invoke(new HammerHitInfo(this, m_owner, block, block.transform.position, false, returnCount));
        }

        // 打ち返した積み木を、打ってきた相手へ追いかけさせる(suehara)
        // 相手が生存者でない(幽霊・ギミックが飛ばした積み木)、自分、味方のときは追いかけない
        private void BeginReturnHoming(TumikiBlock block, IDarumaAttacker shooter)
        {
            if (m_params.ReturnHomingTurnSpeed <= 0.0f || m_params.ReturnHomingDuration <= 0.0f) return;
            var target = shooter as DarumaCharacter;
            if (target == null || target.IsEliminated || ReferenceEquals(shooter, m_owner)) return;
            DarumaMatch match = DarumaMatch.Current;
            if (match != null && match.IsFriendly(m_owner, target)) return;

            var homing = block.GetComponent<Yurube.TumikiHoming>();
            if (homing == null) homing = block.gameObject.AddComponent<Yurube.TumikiHoming>();
            // 打ち返した人が生存者なら、追う相手を見失ったときに自分を狙わないよう撃った人として渡す
            homing.Begin(m_character, m_params.ReturnHomingTurnSpeed, m_params.ReturnHomingDuration, false);
            homing.SetPreferredTarget(target);
        }

        // 飛んできた積み木を打ち返す角度(持ち主の正面から。右が+)。決め方は HammerParams.ReturnAimMode
        private float ReturnAngle(float bearing, float dist)
        {
            switch (m_params.ReturnAimMode)
            {
                case HammerParams.ReturnAim.HammerAngle:
                {
                    // ハンマーの先端で捉える(早振り)ほど振る向きへ引っ張り、根元(振り遅れ)ほど逆へ流す
                    float near = DarumaCharacter.kBodyRadius + TumikiBlock.kRadius;
                    float far = HitReach + TumikiBlock.kRadius;
                    float t = Mathf.InverseLerp(near, far, dist);
                    float spread = m_reverse ? -m_returnSpreadAngle : m_returnSpreadAngle;
                    return bearing + Mathf.Lerp(spread, -spread, t);
                }
                default:
                    // ハンマーのどこで捉えても、向いている向きへ
                    return m_params.ReturnAimOffset;
            }
        }

        private void HitCharacter(DarumaCharacter other, float bearing)
        {
            Vector3 point = other.transform.position + Vector3.up * m_sweepHeight;
            // チーム戦で味方を殴ったときはダメージを与えない(無敵の相手を殴ったときと同じ。弾や必殺技は味方にも当たる)
            DarumaMatch match = DarumaMatch.Current;
            if (match != null && match.IsFriendly(m_owner, other))
            {
                Hit?.Invoke(new HammerHitInfo(this, m_owner, other, point, false, 0));
                return;
            }
            bool damaged = other.KnockBottom(ToWorldDirection(bearing), m_params.SweepLaunchSpeed, m_owner, out TumikiBlock knocked);
            if (damaged)
            {
                // 直接攻撃でダメージを与えた(幽霊なら復活する)
                m_owner.OnDealtDamage();
                BeginHitStop(knocked, other);
            }
            Hit?.Invoke(new HammerHitInfo(this, m_owner, other, point, damaged, 0));
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
            Vector3 center = root.position + Vector3.up * (TumikiBlock.kHeight * 0.5f);
            Vector3 pos = center + root.forward * (DarumaCharacter.kBodyRadius + TumikiBlock.kRadius + 0.05f);
            // 壁に密着して撃つと壁にめり込むので、キャラクターの近くまで引き戻し、しばらくキャラクターと当たらないようにする
            bool embedded = Nakagawa.TumikiPlacement.ResolveEmbed(center, ref pos);
            block.transform.position = pos;
            block.Launch(root.forward, m_params.ShotSpeed, m_owner);
            if (embedded) Nakagawa.TumikiPlacement.IgnoreCharactersAfterEmbed(block);
            BeginHitStop(block, null);
            ShotFired?.Invoke(new HammerHitInfo(this, m_owner, block, pos, false, 0));
        }
    }
}
