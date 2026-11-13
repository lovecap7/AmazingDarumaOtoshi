using System;
using System.Collections.Generic;
using Nakahira;
using UnityEngine;

namespace Yurube
{
    // ネット対戦用(suehara): 必殺技をネット対戦でもローカルと同じに動かすための入口。
    // 通信そのものは suehara/Network(NetSpecialSync・NetworkCommandSource・NetworkTumiki)がつなぐ。ここは通信を知らない。
    // 通信していないときは何もしない(ローカルの動きは今までどおり)。
    //
    // しくみ:
    //  ・ホスト      : 必殺技はいつもどおり動かす(当たり・倒す・はじく・積み木を壊すのはホストだけ)。
    //                  技の区切り(発動・方向の確定・ジャンプなど)で Send を呼び、決まった値をクライアントへ送る
    //  ・クライアント: 届いた知らせ(INetSpecial.NetReceive)で、同じ必殺技の流れを動かして見た目(カットイン・ガイド・エフェクト)を出す。
    //                  勝負に関わる処理(倒す・はじく・壊す・ゲージを使う)は IsClient を見てしない
    public interface INetSpecial
    {
        // クライアント: ホストで起きた出来事(ev は各必殺技で決める番号)。values / blocks は Send に渡したもの
        void NetReceive(int ev, float[] values, TumikiBlock[] blocks);
    }

    public static class SpecialNet
    {
        // ネット対戦のクライアントか(ここでは勝負に関わる処理をしない)
        public static bool IsClient => Nakagawa.GimmickClock.IsNetClient;

        // ---- suehara/Network がつなぐ ----
        // ホスト: 出来事をクライアントへ送る(キャラクター, 必殺技の型の名前, 番号, 数, 積み木)
        public static Action<DarumaCharacter, string, int, float[], TumikiBlock[]> Sender;
        // ホスト: ネット対戦の人が必殺技ボタン / 何かの行動ボタンを押したか(読んだら消える)
        public static Func<MatchPlayer, bool> NetSpecialPressed;
        public static Func<MatchPlayer, bool> NetAnyActionPressed;
        // クライアント: 積み木が飛んでいるか・速さ(クライアントの積み木は TumikiBlock を止めているので、届いた状態で答える)
        public static Func<TumikiBlock, bool?> NetBlockFlying;
        public static Func<TumikiBlock, float?> NetBlockSpeed;
        // クライアント: キャラクターが積んでいるその色の積み木の数(クライアントは積み木を積んでいないので、ホストから届いた数で答える)
        public static Func<DarumaCharacter, TumikiColor, int?> NetCountColor;
        // ホスト: 積み木をネットワークに乗せて作る(ローカル用のプレハブを渡すと、ネット用の同じ積み木を作ってすぐ配る)
        public static Func<GameObject, Vector3, Quaternion, GameObject> NetInstantiateBlock;

        // パッシブで積み木を作る(ネット対戦のホストではネットワークに乗せる。積む前に配らないと物理が戻ってしまうため、すぐ配る)
        public static GameObject InstantiateBlock(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;
            if (NetInstantiateBlock != null && !IsClient)
            {
                var go = NetInstantiateBlock(prefab, position, rotation);
                if (go != null) return go;
            }
            return UnityEngine.Object.Instantiate(prefab, position, rotation);
        }

        // キャラクターが積んでいるその色の積み木の数(クライアントではホストから届いた数)
        public static int CountColor(DarumaCharacter character, TumikiColor color)
        {
            if (character == null) return 0;
            if (IsClient && NetCountColor != null)
            {
                var n = NetCountColor(character, color);
                if (n.HasValue) return n.Value;
            }
            return character.Stack != null ? character.Stack.CountColor(color) : 0;
        }

        // ホスト: 必殺技の出来事をクライアントへ送る(通信していなければ何もしない)
        public static void Send(DarumaCharacter character, MonoBehaviour special, int ev, float[] values = null, TumikiBlock[] blocks = null)
        {
            if (Sender == null || character == null || special == null || IsClient) return;
            Sender(character, special.GetType().Name, ev, values ?? Array.Empty<float>(), blocks ?? Array.Empty<TumikiBlock>());
        }

        // クライアント: 届いた出来事を、そのキャラクターの同じ型の必殺技に渡す
        public static void Receive(DarumaCharacter character, string key, int ev, float[] values, TumikiBlock[] blocks)
        {
            if (character == null) return;
            foreach (var mb in character.GetComponents<MonoBehaviour>())
            {
                if (mb is INetSpecial special && mb.GetType().Name == key)
                {
                    special.NetReceive(ev, values ?? Array.Empty<float>(), blocks ?? Array.Empty<TumikiBlock>());
                    return;
                }
            }
        }

        // クライアント: 発射を知らせてきた積み木(状態が届くまでの少しの間は、飛んでいることにする)
        static readonly Dictionary<TumikiBlock, float> s_launched = new Dictionary<TumikiBlock, float>();
        // クライアント: 発射の知らせで届いた速さ(動きから速さが分かるまでの少しの間に使う)
        static readonly Dictionary<TumikiBlock, float> s_launchSpeed = new Dictionary<TumikiBlock, float>();
        const float kLaunchGrace = 0.3f;

        // クライアント: 発射の知らせを受けた(向きもそろえる。speed が 0 以上なら、少しの間はその速さとする)
        public static void MarkLaunched(TumikiBlock block, Vector3 direction, float speed = -1.0f)
        {
            if (block == null) return;
            s_launched[block] = Time.time + kLaunchGrace;
            if (speed >= 0.0f) s_launchSpeed[block] = speed;
            else s_launchSpeed.Remove(block);
            direction.y = 0.0f;
            if (direction.sqrMagnitude > 0.0001f) block.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            if (s_launched.Count > 64)
            {
                var dead = new List<TumikiBlock>();
                foreach (var pair in s_launched) if (pair.Key == null || pair.Value < Time.time) dead.Add(pair.Key);
                foreach (var key in dead) { s_launched.Remove(key); s_launchSpeed.Remove(key); }
            }
        }

        // 積み木が飛んでいるか(クライアントでは届いた状態)
        public static bool IsFlying(TumikiBlock block)
        {
            if (block == null) return false;
            if (IsClient && NetBlockFlying != null)
            {
                if (s_launched.TryGetValue(block, out float until) && Time.time < until) return true;
                var flying = NetBlockFlying(block);
                if (flying.HasValue) return flying.Value;
            }
            return block.CurrentState == TumikiBlock.State.Flying;
        }

        // 積み木の速さ(クライアントでは見た目の動きから)
        public static float Speed(TumikiBlock block)
        {
            if (block == null) return 0.0f;
            if (IsClient && NetBlockSpeed != null)
            {
                if (s_launchSpeed.TryGetValue(block, out float launched)
                    && s_launched.TryGetValue(block, out float until) && Time.time < until) return launched;
                var speed = NetBlockSpeed(block);
                if (speed.HasValue) return speed.Value;
            }
            return block.Speed;
        }

        // 積み木の進む向き(クライアントでは積み木の向き。発射を知らせたときに向きをそろえる)
        public static Vector3 Direction(TumikiBlock block)
        {
            if (block == null) return Vector3.forward;
            if (IsClient) return block.transform.forward;
            return block.Direction;
        }

        // キャラクターの積んでいる積み木(下から順)。クライアントは積み木を積んでいないので、Send で届いた分を使う
        public static TumikiBlock[] StackBlocks(DarumaCharacter character)
        {
            var list = new List<TumikiBlock>();
            if (character == null || character.Stack == null) return list.ToArray();
            for (int i = 0; i < character.Stack.Count; i++)
            {
                var b = character.Stack.Get(i);
                if (b != null) list.Add(b);
            }
            return list.ToArray();
        }
    }
}
