using System;
using Nakahira;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Nakagawa
{
    // 王冠の持ち主(前の試合の勝者)を、試合とセレクト画面をまたいで覚えておく
    //  ・試合で勝者が決まったら、その人を持ち主にする(引き分けなら前の持ち主のまま)
    //  ・持ち主がセレクト画面で抜けた(B で抜けた・CPU に切り替えた・コントローラーが抜かれた)ら、持ち主はいなくなる
    //  ・メニューからセレクト画面を開いた(試合から戻ってきたのではない)ときも、持ち主はいなくなる
    //  ・セレクトから始めた試合に持ち主が出ていなければ、持ち主はいなくなる
    // 人は入力機器(deviceId)で、CPU は枠の番号で見分ける
    public static class CrownKeeper
    {
        public struct Holder
        {
            public bool isCpu;
            public int deviceId;
            public int playerIndex;
            public string label;
        }

        static bool s_has;
        static Holder s_holder;

        public static bool HasHolder => s_has;
        public static Holder Current => s_holder;
        // 持ち主が変わった・いなくなった
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_has = false;
            s_holder = default;
            Changed = null;
        }

        // セレクト画面のお知らせを受け取る(MatchSelect のイベントは SubsystemRegistration で空になるので、そのあとに登録する)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            MatchSelect.Opened -= OnSelectOpened;
            MatchSelect.Opened += OnSelectOpened;
            MatchSelect.PlayerLeft -= OnSelectPlayerLeft;
            MatchSelect.PlayerLeft += OnSelectPlayerLeft;
        }

        static void OnSelectOpened(bool fromMatch)
        {
            if (!fromMatch) Clear();
        }

        static void OnSelectPlayerLeft(int index, InputDevice device)
        {
            if (!s_has) return;
            bool same = device != null ? !s_holder.isCpu && s_holder.deviceId == device.deviceId : s_holder.isCpu && s_holder.playerIndex == index;
            if (same) Clear();
        }

        // この人を持ち主にする(null なら変えない)
        public static void SetHolder(MatchPlayer player)
        {
            if (player == null || player.Entry == null) return;
            s_holder = new Holder
            {
                isCpu = player.IsCpu,
                deviceId = player.Entry.deviceId,
                playerIndex = player.Index,
                label = player.Label,
            };
            s_has = true;
            Debug.Log($"[CrownKeeper] 王冠の持ち主: {player.Label}");
            Changed?.Invoke();
        }

        public static void Clear()
        {
            if (!s_has) return;
            s_has = false;
            s_holder = default;
            Debug.Log("[CrownKeeper] 王冠の持ち主がいなくなりました");
            Changed?.Invoke();
        }

        // この人が持ち主か
        public static bool IsHolder(MatchPlayer player)
        {
            if (!s_has || player == null || player.Entry == null) return false;
            if (s_holder.isCpu != player.IsCpu) return false;
            return player.IsCpu ? s_holder.playerIndex == player.Index : s_holder.deviceId == player.Entry.deviceId;
        }
    }
}
