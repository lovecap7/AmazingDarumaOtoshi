using UnityEngine;

// ネット対戦の試合で、DarumaMatch と同じ GameObject に付ける部品(NetMatchDriver)が持つもの。
// DarumaMatch はこれが付いているとネット対戦の試合になる(付いていなければ今までどおりローカルの試合)。
public interface INetMatchDriver
{
    // ホストか(false ならクライアント。試合を進めず、ホストから届いた内容を見せるだけ)
    bool IsServer { get; }
    // ホストでキャラクター・幽霊を出した(Instantiate した)直後に呼ばれる。ネットワークに乗せて全員に配る
    void OnBodySpawned(GameObject body);
}
