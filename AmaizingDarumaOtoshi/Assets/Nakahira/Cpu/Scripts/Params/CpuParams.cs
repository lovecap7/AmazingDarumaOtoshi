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
        [Tooltip("ジャンプでかわすときに、跳びながら動く確率(外れたら真上に跳ぶ)。殴り合いで回り込まないとき(横へずれる・少し下がる)と、弾のジャンプ避け(進路から横へずれる)で使う")]
        [Range(0.0f, 1.0f)] public float DodgeMoveChance = 0.8f;
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

        [Header("遊ぶ(満タン近く)")]
        [Tooltip("この段数以上なら、撃って遊ぶ側に切り替える(同じ段数の相手にも攻めに行く・積むより攻めを選ぶ・近くの積み木の固まりにも撃ち込む)")]
        [Min(1)] public int PlayfulStack = 8;
        [Tooltip("遊ぶ側のときの積むのやりたさ(攻めに行ける相手がいなければ積む)")]
        [Range(0.0f, 1.0f)] public float PlayfulCollectScore = 0.25f;
        [Tooltip("遊ぶ側のとき、1秒あたりに積み木の固まりへ撃ち込みたくなる確率(歩いて拾える固まりにも撃つ)")]
        [Range(0.0f, 1.0f)] public float PlayfulScatterChancePerSecond = 0.5f;

        [Header("攻めに行く・待つ(盤面に拾える積み木がない・積み終わったとき)")]
        [Tooltip("攻めに行く(有利な相手へ近づく)のやりたさ。待つより高く、殴る・撃つ・逃げるより低く")]
        [Range(0.0f, 1.0f)] public float ApproachScore = 0.45f;
        [Tooltip("待つ(積み木の供給を待つ)のやりたさ。積み木がないときの積む(0.05 + StateStickiness)より高く、攻めに行くより低く")]
        [Range(0.0f, 1.0f)] public float WaitScore = 0.3f;
        [Tooltip("待つ場所を選ぶとき、この距離(m)以内の相手を数える(近いほど重い)")]
        [Min(0.1f)] public float WaitDangerRadius = 10.0f;
        [Tooltip("待つ場所を選ぶときの相手の重み:幽霊")]
        [Min(0.0f)] public float WaitGhostWeight = 1.5f;
        [Tooltip("待つ場所を選ぶときの相手の重み:自分より段数が多い(自分を倒せる)相手")]
        [Min(0.0f)] public float WaitStrongerWeight = 1.0f;
        [Tooltip("待つ場所を選ぶときの相手の重み:自分と同じ段数の相手")]
        [Min(0.0f)] public float WaitEqualWeight = 0.6f;
        [Tooltip("待つ場所を選ぶときの相手の重み:自分より段数が少ない相手")]
        [Min(0.0f)] public float WaitWeakerWeight = 0.3f;
        [Tooltip("供給エリアの縁から 1m 離れるごとの減点(供給の近くで待つように)")]
        [Min(0.0f)] public float WaitSupplyCost = 0.05f;
        [Tooltip("待つ場所までの道 1m ごとの減点(遠すぎる場所へ行かないように)")]
        [Min(0.0f)] public float WaitTravelCost = 0.02f;
        [Tooltip("待つ場所までの道が供給エリアの中を通るときの減点(通っている間はそのエリアの供給が止まる)")]
        [Min(0.0f)] public float WaitCrossAreaCost = 0.3f;
        [Tooltip("今の待つ場所よりこれだけ点数が良い場所があるときだけ移る(迷わないように)")]
        [Min(0.0f)] public float WaitSwitchMargin = 0.15f;
        [Tooltip("待つ場所に着いたあと、うろうろする範囲の半径(m)")]
        [Min(0.0f)] public float WaitWanderRadius = 1.5f;
        [Tooltip("うろうろしてから次にうろうろするまでの間隔(秒)の最小・最大")]
        [Min(0.0f)] public float WaitWanderIntervalMin = 1.0f;
        [Min(0.0f)] public float WaitWanderIntervalMax = 2.5f;
        [Tooltip("うろうろするときの移動入力の大きさ(ゆっくり歩く)")]
        [Range(0.05f, 1.0f)] public float WaitWanderInput = 0.4f;

        [Header("撃つ(股抜きショット。撃つと自分が 1 段減る)")]
        [Tooltip("撃つのやりたさ(殴るより低く、積む + StateStickiness より高く。遠くにしか積み木がないとき、積むから移れるように)")]
        [Range(0.0f, 1.0f)] public float ShotScore = 0.62f;
        [Tooltip("集中度の割合がこれより高いと、撃つへは移れない")]
        [Min(0.0f)] public float ShotThreshold = 0.8f;
        [Tooltip("残しておきたい段数(序盤の HP)。これより多いときだけ撃つ(熱中していると無視する)")]
        [Min(0)] public int ShotReserve = 4;
        [Tooltip("身の回り:この距離(m)以内に拾える積み木があるうちは、撃たずに積む")]
        [Min(0.0f)] public float ShotNearbyRadius = 5.0f;
        [Tooltip("この距離(m)以内の相手を撃つ(殴る間合いより遠い相手)")]
        [Min(0.0f)] public float ShotRange = 8.0f;
        [Tooltip("撃ったあと、次に撃つまでの間(秒)の最小・最大")]
        [Min(0.0f)] public float ShotIntervalMin = 0.6f;
        [Min(0.0f)] public float ShotIntervalMax = 1.2f;
        [Tooltip("集中度の割合が最大のときの、次に撃つまでの間(秒。0〜この値。ショットの動作が終わればすぐ撃つ連打に近い)。落ち着いているときは ShotIntervalMin〜Max で、集中度の割合が上がるほどこちらに近づく")]
        [Min(0.0f)] public float ShotMashInterval = 0.05f;
        [Tooltip("狙いのぶれ(度。この範囲でずれる)。落ち着いているときの値。強いほど小さい")]
        [Min(0.0f)] public float ShotAimError = 3.0f;
        [Tooltip("集中度の割合が最大のときの狙いのぶれ(度)。集中度の割合が上がるほど ShotAimError からこちらに近づく(焦ると連打で雑になる)")]
        [Min(0.0f)] public float ShotAimErrorMax = 9.0f;
        [Tooltip("撃ったあと、次に撃つまでの間に横へ動く確率(外れたらその場で止まって撃つ)")]
        [Range(0.0f, 1.0f)] public float ShotStrafeChance = 0.8f;
        [Tooltip("撃つボタンを押してから弾が出るまでの間、相手の方へ踏み込みながら狙う確率")]
        [Range(0.0f, 1.0f)] public float ShotStepInChance = 0.4f;
        [Tooltip("ついでに弾く:落ちている積み木を弾けば相手に当たるとき、弾く確率(積み木ごとに決める)")]
        [Range(0.0f, 1.0f)] public float FlickChance = 0.8f;
        [Tooltip("ついでに弾く:弾いた積み木が飛ぶ線から、相手(の予想位置)がこの距離(m)以内なら当たるとみなす")]
        [Min(0.0f)] public float FlickAimWidth = 0.8f;
        [Tooltip("撃った・弾いたときに足す集中度")]
        [Min(0.0f)] public float AttentionOnShot = 0.05f;
        [Tooltip("熱中する確率(撃ち始めるたびに 確率 × 焦り で決める)。熱中すると残しておきたい段数を無視して撃ち続ける")]
        [Range(0.0f, 1.0f)] public float OverheatChance = 0.6f;
        [Tooltip("熱中しているときの、次に撃つまでの間(秒)")]
        [Min(0.0f)] public float OverheatShotInterval = 0.3f;
        [Tooltip("追い撃ち:殴り合いの相手が逃げてから、この時間(秒)のあいだは近い距離からでも撃つ")]
        [Min(0.0f)] public float PursuitWindow = 3.0f;
        [Tooltip("追い撃ちで撃つ最短の距離(m)")]
        [Min(0.0f)] public float PursuitMinDistance = 3.0f;
        [Tooltip("乱反射遊び:歩く道の長さがこれ(m)以上の積み木は、拾いに行くには遠すぎるとみなす(道がない積み木も)")]
        [Min(0.0f)] public float ScatterMinPathLength = 15.0f;
        [Tooltip("乱反射遊び:拾いに行くには遠すぎる積み木の固まりへ撃ち込む確率(1秒あたり)")]
        [Range(0.0f, 1.0f)] public float ScatterChancePerSecond = 0.1f;
        [Tooltip("乱反射遊びで撃つ最大の距離(m)")]
        [Min(0.0f)] public float ScatterRange = 12.0f;
        [Tooltip("この半径(m)に ScatterClusterCount 個以上あれば、積み木の固まりとみなす")]
        [Min(0.0f)] public float ScatterClusterRadius = 2.0f;
        [Min(1)] public int ScatterClusterCount = 3;

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

        [Header("とっさの反応(飛んでくる弾。確率はどれも 余裕(1 − 集中度の割合) を掛けて使う)")]
        [Tooltip("弾に気づいて反応する確率")]
        [Range(0.0f, 1.0f)] public float ReactChance = 1.0f;
        [Tooltip("ジャンプ乗り(落ちてくる途中で弾に触れて積む。一番難しい)を選ぶ確率")]
        [Range(0.0f, 1.0f)] public float CatchChance = 0.7f;
        [Tooltip("打ち返しを選ぶ確率(ジャンプ乗りを選ばなかったとき)")]
        [Range(0.0f, 1.0f)] public float ReturnChance = 0.8f;
        [Tooltip("弾に気づいてから動けるまでの遅れ(秒)")]
        [Min(0.0f)] public float ReflexReaction = 0.05f;
        [Tooltip("跳ぶ・振るタイミングのずれ(秒。この範囲でばらつく)")]
        [Min(0.0f)] public float ReflexTimingError = 0.01f;
        [Tooltip("この時間(秒)先までに当たる弾に反応する")]
        [Min(0.1f)] public float ReflexHorizon = 1.0f;

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
