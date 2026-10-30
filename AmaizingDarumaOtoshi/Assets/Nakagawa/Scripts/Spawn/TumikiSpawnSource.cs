using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 積み木の生成命令を受け取るもの(生成エリア・回転砲台など)
    // TumikiSpawner がシーン内の有効なものをすべて探し、最小〜最大排出量に従って生成する数を割り振る
    // 新しい生成のしかたは、これを継承して作る(TumikiSpawner を変更する必要はない)
    public abstract class TumikiSpawnSource : MonoBehaviour
    {
        // 1回の生成命令で割り振られる数の最小・最大
        public abstract int MinAmount { get; }
        public abstract int MaxAmount { get; }

        // 生成命令。colors の色の積み木を生成し(予告・発射待ちも含む)、その数を返す
        // reserved: 同じ命令で他が生成した位置(重ならないよう共有する。床に置くものは生成した位置を追加する)
        public abstract int Spawn(IReadOnlyList<TumikiColor> colors, TumikiBlock prefab, List<Vector3> reserved);

        // 命令を受けたが、まだ盤面に出ていない積み木の数(盤面の積み木の上限の判定に含める)
        // 予告(TumikiSpawnWarning)の分は TumikiSpawner が別に数えるので含めない
        public virtual int PendingCount => 0;

        // 予告・発射なしで、すぐに盤面へ置けるか(試合開始時に最初から積み木を置いておくのに使う)
        public virtual bool CanPlaceImmediately => false;

        // 予告・発射なしで、colors の色の積み木をすぐに置き、その数を返す(CanPlaceImmediately のものだけ呼ばれる)
        public virtual int PlaceImmediately(IReadOnlyList<TumikiColor> colors, TumikiBlock prefab, List<Vector3> reserved) => 0;
    }
}
