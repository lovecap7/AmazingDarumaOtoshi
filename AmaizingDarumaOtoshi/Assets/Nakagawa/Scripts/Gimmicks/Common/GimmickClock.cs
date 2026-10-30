using System;
using Unity.Netcode;

namespace Nakagawa
{
    // ギミックの時刻を、通信中はホストと共有する。
    // 各端末が自分で時間を足していくと、壁などの動くタイミングが端末ごとにずれる(クライアントの画面だけ壁の位置が違って見える)ため、
    // 通信中は NetworkManager.ServerTime(ホストと共有する時刻)に合わせる。通信していないときは今までどおり自分で足す。
    // 合わせ方: 毎ステップ少しずつ寄せる(急に動かさない。ホスト側で壁に乗っているものを持ち上げる速度がガタつかないように)。
    //          大きくずれているとき(接続直後・ステージ切り替え直後)は一気に合わせる。
    // ギミックは「ある周期でぐるぐる回る」ものなので、ずれは周期で折り返して比べる。
    public static class GimmickClock
    {
        // これ以上ずれていたら、少しずつではなく一気に合わせる(秒)
        const double kSnapThreshold = 0.25;
        // 1ステップで、ずれのうち寄せる割合
        const float kCorrectionPerStep = 0.05f;

        // ネット対戦用(suehara): ネット対戦のクライアントか。ギミックの当たり・押し出しはホストだけが行う
        // (クライアントの積み木・キャラクターはホストから位置を受け取って見せるだけなので、ギミックで動かさない)
        public static bool IsNetClient
        {
            get
            {
                var nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening && nm.IsClient && !nm.IsServer;
            }
        }

        // ギミックを止めておくか(セレクトから始めた試合の、開始のカウントダウン中)
        // 止めている間、ギミックは動かず・押し出さず・発射しない(時間も進めない)。止める条件はここだけで決める
        public static bool IsHeld
        {
            get
            {
                var match = Nakahira.DarumaMatch.Current;
                return match != null && match.IsConfigured && match.IsCountingDown;
            }
        }

        // 通信中なら、ホストと共有する時刻(秒)
        public static bool TryGetSharedTime(out double time)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsServer || nm.IsConnectedClient))
            {
                time = nm.ServerTime.Time;
                return true;
            }
            time = 0.0;
            return false;
        }

        // ギミックが有効になったときの時刻(開始のずれ phaseOffset を含む)
        public static float StartTime(float phaseOffset, float period)
        {
            if (period <= 0.0f || !TryGetSharedTime(out double shared)) return phaseOffset;
            return (float)Wrap(shared + phaseOffset, period);
        }

        // 1ステップ(dt秒)進めた時刻を返す。通信中は共有時刻に寄せる
        public static float Step(float current, float dt, float phaseOffset, float period)
        {
            float next = current + dt;
            if (period <= 0.0f || !TryGetSharedTime(out double shared)) return next;

            double half = period * 0.5;
            // 目標との差を -周期/2 〜 +周期/2 の範囲で求める
            double diff = Wrap(shared + phaseOffset - next + half, period) - half;
            if (Math.Abs(diff) > kSnapThreshold) return next + (float)diff;
            return next + (float)diff * kCorrectionPerStep;
        }

        static double Wrap(double value, double period)
        {
            double r = value % period;
            return r < 0.0 ? r + period : r;
        }
    }
}
