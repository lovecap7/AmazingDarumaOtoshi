using UnityEngine;

public interface IHomingTargetSelector
{
    // ホーミング対象を1つ選んで返す。対象がいなければnullを返す。
    Transform SelectTarget(BlockController owner);
}
