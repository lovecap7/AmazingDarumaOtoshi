using UnityEngine;

namespace Nakahira
{
    // CPU(コンピュータープレイヤー)の考え方・腕前。強さの違いはこのアセットを分けて作る
    // 値は使うたびに直接読むので、再生中に変更しても即座に反映される
    // 考え方は Assets/Nakahira/Cpu/CPU設計メモ.md
    [CreateAssetMenu(fileName = "CpuParams", menuName = "Nakahira/Cpu Params")]
    public class CpuParams : ScriptableObject
    {
        [Header("決める")]
        [Tooltip("状態・狙う積み木を選び直す間隔(秒)。短いほど状況の変化にすぐ反応する")]
        [Min(0.02f)] public float ThinkInterval = 0.2f;
        [Tooltip("今の状態のやりたさに足す点(ころころ切り替えないように)")]
        [Min(0.0f)] public float StateStickiness = 0.1f;
        [Tooltip("状態を選んでから、この時間(秒)は別の状態に変えない(やりたさが UrgentScore 以上なら変える)")]
        [Min(0.0f)] public float MinStateTime = 0.5f;
        [Tooltip("このやりたさ以上の状態には、選んだばかりでもすぐ切り替える(逃げるなど)")]
        [Range(0.0f, 1.0f)] public float UrgentScore = 0.75f;

        [Header("集中度(認識の疲れ。出来事で足され、時間で減る。判定には 疲れ÷容量 の割合を使う)")]
        [Tooltip("容量。大きいほど疲れにくい(うまい CPU)")]
        [Min(0.01f)] public float AttentionCapacity = 1.0f;
        [Tooltip("1秒あたりに減る疲れ")]
        [Min(0.0f)] public float AttentionDecay = 0.25f;
        [Tooltip("自分が相手を 1段 → 0段(首だけ)にした瞬間に足す(あと一撃に夢中になる)")]
        [Min(0.0f)] public float AttentionOnKnockToZero = 0.95f;
        [Tooltip("相手の薙ぎ払いをかわしたときに足す")]
        [Min(0.0f)] public float AttentionOnDodge = 0.2f;
        [Tooltip("攻撃を受けて段が減ったときに足す(焦る)")]
        [Min(0.0f)] public float AttentionOnHit = 0.3f;
        [Tooltip("飛んでくる弾に反応したときに足す(とっさの反応で使う)")]
        [Min(0.0f)] public float AttentionOnProjectile = 0.3f;

        [Header("しきい値(集中度の割合がこれより高いと、その状態へは移れない。積むはいつでも移れる)")]
        [Min(0.0f)] public float MeleeThreshold = 0.9f;
        [Min(0.0f)] public float FleeThreshold = 0.6f;
        [Tooltip("幽霊から逃げるとき(一番の脅威なので、疲れていても気づきやすい)")]
        [Min(0.0f)] public float GhostFleeThreshold = 0.9f;

        [Header("攻め:狙う相手(点数が一番高い相手を狙う)")]
        [Tooltip("相手の段数が少ないほど足す点の重み")]
        [Min(0.0f)] public float TargetStackWeight = 1.0f;
        [Tooltip("相手が近いほど足す点の重み")]
        [Min(0.0f)] public float TargetDistanceWeight = 1.0f;
        [Tooltip("この距離(m)で、近さの点が 0 になる")]
        [Min(0.1f)] public float TargetDistanceRange = 10.0f;
        [Tooltip("ルールで目立つ相手(ストック・持ち点が多い)ほど足す点の重み")]
        [Min(0.0f)] public float TargetRuleWeight = 0.5f;

        [Header("殴る")]
        [Tooltip("殴るのやりたさ(逃げるより低く、積むより高く)")]
        [Range(0.0f, 1.0f)] public float MeleeScore = 0.65f;
        [Tooltip("生存者が道の距離でこの距離(m)以内に来たら、殴り合うか決める")]
        [Min(0.0f)] public float MeleeEnterDistance = 3.0f;
        [Tooltip("殴り合いの相手が道の距離でこの距離(m)より離れたら追うのをやめる")]
        [Min(0.0f)] public float MeleeChaseDistance = 6.0f;
        [Tooltip("この時間(秒)追っても間合いに入れなければやめる")]
        [Min(0.0f)] public float MeleeChaseTime = 3.0f;
        [Tooltip("間合い(届く距離に対する割合)。この距離まで近づいてから振る")]
        [Range(0.3f, 1.0f)] public float MeleeRangeRatio = 0.85f;
        [Tooltip("振ったあと、次に振るまでの間(秒)の最小・最大(この間でばらつく)")]
        [Min(0.0f)] public float SwingIntervalMin = 0.2f;
        [Min(0.0f)] public float SwingIntervalMax = 0.6f;
        [Tooltip("相手の薙ぎ払いをジャンプでかわす確率(疲れていないとき。集中度の割合が上がるほど下がる)")]
        [Range(0.0f, 1.0f)] public float DodgeChance = 0.8f;
        [Tooltip("相手が振り始めてから、かわすジャンプをするまでの遅れ(秒)")]
        [Min(0.0f)] public float DodgeReaction = 0.05f;
        [Tooltip("かわすジャンプの最中に、相手のハンマーの届かない背中側へ回り込む確率")]
        [Range(0.0f, 1.0f)] public float CircleChance = 0.6f;
        [Tooltip("振る間隔を待つあいだ、相手を右側に置くように横へ動き続ける確率(振るたびに決める)")]
        [Range(0.0f, 1.0f)] public float StrafeChance = 0.6f;
        [Tooltip("殴り合いの最中に、この距離(m)以内で相手の間合いの外にある積み木は拾う")]
        [Min(0.0f)] public float MeleePickupRadius = 1.5f;

        [Header("殴る:焦り(集中度の割合が高いと、無意味に連打してしまう)")]
        [Tooltip("集中度の割合がこれを超えると焦り始める(焦りは 0〜1。割合 1 で最大)")]
        [Range(0.0f, 1.0f)] public float PanicStart = 0.4f;
        [Tooltip("いちばん焦ったときの振る間隔(秒)の最大(連打)")]
        [Min(0.0f)] public float PanicSwingInterval = 0.05f;
        [Tooltip("いちばん焦ったとき、届かない距離でも振ってしまう余分な距離(m)")]
        [Min(0.0f)] public float PanicReachSlack = 1.5f;
        [Tooltip("いちばん焦ったとき、意味もなく跳ぶ回数(1秒あたり)")]
        [Min(0.0f)] public float PanicJumpRate = 2.5f;
        [Tooltip("いちばん焦ったとき、かわすジャンプの遅れに足される最大のずれ(秒)")]
        [Min(0.0f)] public float PanicDodgeJitter = 0.2f;

        [Header("積む")]
        [Tooltip("積むのやりたさ(拾える積み木があるとき)")]
        [Range(0.0f, 1.0f)] public float CollectScore = 0.5f;
        [Tooltip("この段数になるまで積み木を集める")]
        [Range(1, 10)] public int CollectUntil = 10;
        [Tooltip("この距離(m)より遠い積み木は探さない")]
        [Min(1.0f)] public float SearchRadius = 25.0f;
        [Tooltip("狙っている積み木を別の積み木に変えるのは、こちらの方がこの距離(m)ぶん以上近いときだけ(迷わないように)")]
        [Min(0.0f)] public float SwitchMargin = 1.5f;
        [Tooltip("積み木に近づけないままこの時間(秒)が経ったら、しばらく諦める(遠回りの道でも、進んでいる間は諦めない)")]
        [Min(0.5f)] public float GiveUpTime = 4.0f;
        [Tooltip("諦めた積み木を狙わない時間(秒)")]
        [Min(0.0f)] public float GiveUpCooldown = 3.0f;
        [Tooltip("積み木までの道が、危険な相手(幽霊・段数が FleeStackGap 以上多い相手)からこの距離(m)以内を通るなら狙わない。FleeEnterDistance より大きくする")]
        [Min(0.0f)] public float CollectThreatClearance = 5.0f;
        [Tooltip("幽霊がこの距離(m)以内にいる積み木は後回しにする")]
        [Min(0.0f)] public float GhostAvoidRadius = 3.0f;
        [Tooltip("幽霊の近くにある積み木のコスト(m。幽霊に近いほど大きくなる)")]
        [Min(0.0f)] public float GhostAvoidPenalty = 6.0f;

        [Header("取り合い・有利")]
        [Tooltip("狙っている積み木からこの距離(m)以内に相手がいたら、段数を比べて取り合うか譲るか決める")]
        [Min(0.0f)] public float ContestRadius = 2.0f;
        [Tooltip("段数の差(自分 − 相手)がこれ以上なら有利とみなす")]
        public int AdvantageMargin = 1;
        [Tooltip("取り合いで譲った積み木を狙わない時間(秒)")]
        [Min(0.0f)] public float YieldCooldown = 3.0f;

        [Header("逃げる")]
        [Tooltip("相手がこの距離(m)以内に来たら、逃げるか判断する(生存者は道の距離、幽霊はまっすぐの距離)")]
        [Min(0.0f)] public float FleeEnterDistance = 4.0f;
        [Tooltip("追っ手がこの距離(m)より遠くなったら逃げ切ったとみなす(入る距離より大きくする)")]
        [Min(0.0f)] public float FleeExitDistance = 7.0f;
        [Tooltip("相手の方がこの段数以上多ければ逃げる")]
        [Min(1)] public int FleeStackGap = 3;
        [Tooltip("段数で負けている相手から逃げるやりたさ")]
        [Range(0.0f, 1.0f)] public float StackThreatUrgency = 0.8f;
        [Tooltip("幽霊から逃げるやりたさ(幽霊は一番の脅威なので高め)")]
        [Range(0.0f, 1.0f)] public float GhostThreatUrgency = 0.95f;
        [Tooltip("逃げながら拾う積み木は、自分が追っ手よりこの距離(m)以上早く着けるものだけ")]
        [Min(0.0f)] public float FleeSafetyMargin = 1.0f;

        [Header("積む:ジャンプ")]
        [Tooltip("ジャンプする距離の誤差(割合)。大きいほど跳ぶのが早すぎたり遅すぎたりする")]
        [Range(0.0f, 1.0f)] public float JumpDistanceError = 0.1f;
        [Tooltip("着地を狙う位置の誤差(m)")]
        [Min(0.0f)] public float AimError = 0.1f;

        [Header("道・足場")]
        [Tooltip("目的地までの道(奈落・壁を迂回する道)を求め直す間隔(秒)")]
        [Min(0.05f)] public float RepathInterval = 0.3f;
        [Tooltip("進む先の足元に地面があるか確かめる距離(m)。移動速度×0.1秒ぶんを足して使う")]
        [Min(0.0f)] public float EdgeLookAhead = 0.6f;
        [Tooltip("足元からこの深さ(m)までに地面がなければ奈落とみなす")]
        [Min(0.1f)] public float GroundProbeDepth = 2.0f;
    }
}
