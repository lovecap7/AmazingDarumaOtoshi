using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nakagawa
{
    // ネット対戦用(suehara): ホストだけが動かすギミック(ステージ19 ビリヤード・20 ルーレットなど)の見た目を、クライアントにも見せるための入口
    //  ・INetGimmickState: ホストで今の状態を書き出し、クライアントで受け取って見た目に反映する
    //    (送り届けるのは suehara/Network/NetGimmickSync。クライアントでは前後の状態の間をなめらかにつないで、毎フレーム ReadNetState を呼ぶ)
    //  ・GimmickNet.Relay(): この範囲でホストが出したエフェクト・音・文字・カメラの揺れを、クライアントにも出す
    //  ・GimmickNet.Mute() : この範囲ではエフェクト・音などを出さない
    //    (クライアントでホストの処理をなぞるとき、ホストから送られてくる分と二重にならないように)
    // 通信していないときは何も起きない(ローカルの動きは今までどおり)
    public interface INetGimmickState
    {
        // ホスト: 今の状態を書き出す(書く順番と読む順番をそろえる)
        void WriteNetState(GimmickNetState s);
        // クライアント: 受け取った状態を見た目に反映する(毎フレーム呼ばれる)
        void ReadNetState(GimmickNetState s);
    }

    // ギミック1つ分の状態(数の並び)。番号・フラグは補間しない(Discrete)
    public class GimmickNetState
    {
        public readonly List<float> Values = new List<float>();
        public readonly List<bool> Discrete = new List<bool>();
        int m_read;
        bool m_overrun;

        // 読み終わったときに、書いた数と読んだ数が合っていたか(合っていなければ反映しない)
        public bool Ok => !m_overrun;

        public void Clear()
        {
            Values.Clear();
            Discrete.Clear();
            BeginRead();
        }

        public void BeginRead()
        {
            m_read = 0;
            m_overrun = false;
        }

        //---------- 書く ----------
        public void Add(float v) { Values.Add(v); Discrete.Add(false); }
        public void AddInt(int v) { Values.Add(v); Discrete.Add(true); }
        public void AddBool(bool v) { Values.Add(v ? 1.0f : 0.0f); Discrete.Add(true); }
        public void AddVec3(Vector3 v) { Add(v.x); Add(v.y); Add(v.z); }
        public void AddRot(Quaternion q) { Add(q.x); Add(q.y); Add(q.z); Add(q.w); }

        //---------- 読む ----------
        public float Float()
        {
            if (m_read >= Values.Count)
            {
                m_overrun = true;
                return 0.0f;
            }
            return Values[m_read++];
        }

        public int Int() => Mathf.RoundToInt(Float());
        public bool Bool() => Float() > 0.5f;
        public Vector3 Vec3() => new Vector3(Float(), Float(), Float());

        public Quaternion Rot()
        {
            var q = new Quaternion(Float(), Float(), Float(), Float());
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (m < 0.0001f) return Quaternion.identity;
            return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
        }
    }

    public static class GimmickNet
    {
        // ホストで Relay() の範囲に出たもの(NetGimmickSync がホストでつなぎ、クライアントへ送る)
        public static Action<GimmickBurstSettings, Vector3, Quaternion, float, Color?> BurstSent;
        public static Action<AudioClip, float, float> SoundSent;
        public static Action<float, float> ShakeSent;
        public static Action<string, Color, float, float> MessageSent;

        static int s_relay;
        static int s_mute;

        public static bool Relaying => s_relay > 0 && s_mute == 0;
        public static bool Muted => s_mute > 0;

        // using var _ = GimmickNet.Relay(); の形で使う
        public static Scope Relay()
        {
            s_relay++;
            return new Scope(false);
        }

        public static Scope Mute()
        {
            s_mute++;
            return new Scope(true);
        }

        // クライアントでも動く処理で使う: ホストでは送り、クライアントでは自分では出さない(ホストから送られてくる分だけを出す)
        public static Scope RelayOrMute() => GimmickClock.IsNetClient ? Mute() : Relay();

        public readonly struct Scope : IDisposable
        {
            readonly bool m_mute;
            public Scope(bool mute) { m_mute = mute; }

            public void Dispose()
            {
                if (m_mute) s_mute = Mathf.Max(0, s_mute - 1);
                else s_relay = Mathf.Max(0, s_relay - 1);
            }
        }
    }
}
