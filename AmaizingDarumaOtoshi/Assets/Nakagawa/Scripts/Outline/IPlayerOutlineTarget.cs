using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Nakagawa
{
    // 積み木以外で、PlayerOutlines の輪郭を付けたい物(新しい弾・攻撃する物など)
    // MonoBehaviour に実装し、OnEnable で PlayerOutlineTargets.Register(this)、OnDisable で Unregister(this) を呼ぶ
    //  ・WantsOutline が true の間、その物と子のレンダラーに輪郭が付く
    //  ・色は OutlineOwner(撃った人)のプレイヤーの色。null・分からなければ だれにも割り振られない色
    // 攻撃判定のある積み木(TumikiBlock)は何もしなくても輪郭が付くので、実装しなくてよい
    public interface IPlayerOutlineTarget
    {
        bool WantsOutline { get; }
        IDarumaAttacker OutlineOwner { get; }
        // 輪郭の太さ(画面の高さ1080でのピクセル)。0 以下なら攻撃判定のある積み木と同じ
        float OutlineWidth { get; }
    }

    public static class PlayerOutlineTargets
    {
        static readonly List<IPlayerOutlineTarget> s_all = new List<IPlayerOutlineTarget>();
        public static IReadOnlyList<IPlayerOutlineTarget> All => s_all;

        public static void Register(IPlayerOutlineTarget target)
        {
            if (target != null && !s_all.Contains(target)) s_all.Add(target);
        }

        public static void Unregister(IPlayerOutlineTarget target) => s_all.Remove(target);

        // 消えた物を外す
        public static void Prune() => s_all.RemoveAll(t => t == null || (t is Object o && o == null));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_all.Clear();
    }
}
