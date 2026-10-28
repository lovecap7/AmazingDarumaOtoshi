// 志鷹さんの Assets/Shitaka から複製（由留部の作業用）
// 元のクラスと名前が衝突しないよう、名前空間 Yurube.Blocks に入れている
using UnityEngine;

namespace Yurube.Blocks
{
public interface IHomingTargetSelector
{
    // ホーミング対象を1つ選んで返す。対象がいなければnullを返す。
    Transform SelectTarget(BlockController owner);
}
}
