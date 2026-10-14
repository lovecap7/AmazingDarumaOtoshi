using System;
using System.Collections.Generic;
using UnityEngine;

// 仮の音をプログラムで作る（音のファイルが無くても鳴らせるように）。一度作った音は使い回す。
// 本番の音ができたら、MatchSounds（Resources/MatchSounds.asset）に AudioClip を入れれば、そちらが優先される。
//  ・効果音：短い音を波形（サイン波・三角波・矩形波・ノイズ）から作る
//  ・BGM　 ：132 BPM・16小節のループ（ヨナ抜き音階のメロディ ＋ ベース ＋ 裏打ちの和音 ＋ ドラム）
public static class SoundSynth
{
    public const int Rate = 44100;

    public enum Wave { Sine, Triangle, Square, Pulse25, Pulse12 }
    enum Filter { Band, High, Low }

    static readonly Dictionary<string, AudioClip> s_cache = new Dictionary<string, AudioClip>();
    static readonly System.Random s_rng = new System.Random(20261014);

    // ================= 効果音 =================
    // ハンマーを振る（シュッ）
    public static AudioClip Swing => Cached("swing", () =>
    {
        var b = Buf(0.22f);
        NoiseBand(b, 0, 0.22f, 700f, 3500f, 1f, u => { float s = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.6f)); return s * s; }, Filter.Band);
        return b;
    });

    // 股抜きショット（ブンッ → ポン）
    public static AudioClip ShotSwing => Cached("shotSwing", () =>
    {
        var b = Buf(0.34f);
        NoiseBand(b, 0, 0.26f, 350f, 2200f, 1f, u => { float s = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.6f)); return s * s; }, Filter.Band);
        Tone(b, At(0.2f), 0.12f, 520, 240, Wave.Sine, 0.8f, decay: 25f);
        return b;
    });

    // ハンマーで積み木をはじく（カキーン！）
    public static AudioClip HitBlock => Cached("hitBlock", () => HomeRun(1f, 0.6f, false), 0.95f);

    // 飛んできた積み木を打ち返す（もっと高く響くカキーン！ ＋ 観客のワーッ）。打ち返した回数でさらに高くする
    public static AudioClip HitReturn => Cached("hitReturn", () => HomeRun(1.12f, 0.8f, true), 0.95f);

    // ハンマーでキャラクターの積み木をはじき出す（カキーン！ ＋ 当たった手ごたえ）
    public static AudioClip HitCharacter => Cached("hitCharacter", () => HomeRun(0.94f, 1f, false), 0.95f);

    // ホームランの打球音「カキーン！」（金属バットで芯をとらえた音）
    //  ・カッ　：芯に当たった瞬間の、極短く明るい音
    //  ・打撃感：短い胴鳴り と ごく短い低音（重くしすぎると爆発のようになるので控えめ）
    //  ・キーン：金属バットの響き。叩いた衝撃で鋭く共鳴させ、鐘のように長く響かせる（少しずらした2つで金属らしいうなり）
    //  ・こだま：スタジアムで少し遅れて返ってくる響き
    //  ・観客　：（crowd のときだけ）ワーッという歓声
    // pitch：全体の高さ　weight：打撃の重さ
    static float[] HomeRun(float pitch, float weight, bool crowd)
    {
        var b = Buf(crowd ? 1.7f : 1.1f);
        // カッ
        NoiseBand(b, 0, 0.0015f, 12000f, 12000f, 2f, u => 1f - u, Filter.High);
        NoiseBand(b, 0, 0.012f, 3500f, 6000f, 0.8f, u => 1f - u, Filter.Band);
        // 打撃感
        Ring(b, 0, 950f * pitch, 0.05f, 0.7f * weight);
        Ring(b, 0, 1900f * pitch, 0.06f, 0.5f * weight);
        Tone(b, 0, 0.05f, 150, 75, Wave.Sine, 0.45f * weight, 0.0005f, 45f);
        // キーン
        Ring(b, 0, 2150f * pitch, 0.85f, 1.9f);
        Ring(b, 0, 2183f * pitch, 0.8f, 1.5f);
        Ring(b, 0, 3420f * pitch, 0.5f, 1.0f);
        Ring(b, 0, 5310f * pitch, 0.3f, 0.6f);
        Ring(b, 0, 7660f * pitch, 0.18f, 0.35f);
        Saturate(b, 1.3f);
        // こだま
        Echo(b, 0.12f, 0.3f, 3);
        Reverb(b, 0.12f, 0.78f);
        // 観客
        if (crowd) Crowd(b, At(0.06f), 1.5f, 0.35f);
        return b;
    }

    // 撃墜（ドォォン…キラーン）。スマブラで場外へ吹っ飛んだときのような、重くて派手な音
    //  ・吹き飛ぶ直前の「ゴッ」と吸い込むような風
    //  ・爆発（低い帯域へ下がっていくノイズ ＋ ひずませた重低音）
    //  ・光がはじける「キラーン」（高い音をわずかにずらして重ね、細かく揺らす）
    //  ・大きな残響
    static float[] Blast(float pitch, bool fromFall)
    {
        var b = Buf(2.2f);
        int hit = At(fromFall ? 0.02f : 0.09f);
        if (!fromFall) NoiseBand(b, 0, 0.1f, 500f, 5000f, 0.6f, u => u * u, Filter.Band);
        NoiseBand(b, hit, 0.005f, 9000f, 9000f, 2f, u => 1f - u, Filter.High);
        NoiseBand(b, hit, 1.4f, 7000f * pitch, 140f, 1.6f, u => Mathf.Exp(-u * 3.2f), Filter.Low);
        Tone(b, hit, 1.2f, 95 * pitch, 28, Wave.Sine, 1.3f, 0.002f, 2.8f, 0.1f);
        Tone(b, hit, 0.25f, 210 * pitch, 70, Wave.Triangle, 0.6f, 0.001f, 12f);
        double[] stars = { 2637, 3136, 3951, 5274 };
        foreach (var s in stars)
        {
            double f = s * pitch;
            Tone(b, hit + At(0.05f), 1.3f, f, f * 1.002, Wave.Sine, 0.07f, 0.004f, 2.6f, 0.2f, vibRate: 17f, vibDepth: 0.004f);
            Tone(b, hit + At(0.05f), 1.3f, f * 1.004, f * 1.006, Wave.Sine, 0.06f, 0.004f, 2.6f, 0.2f, vibRate: 13f, vibDepth: 0.004f);
        }
        Saturate(b, 2.6f);
        Reverb(b, 0.45f, 0.88f);
        return b;
    }

    // 飛んできた積み木がキャラクターに当たる（ドスッ）
    public static AudioClip BlockHitCharacter => Cached("blockHitCharacter", () =>
    {
        var b = Buf(0.25f);
        Tone(b, 0, 0.24f, 170, 55, Wave.Sine, 1f, 0.001f, 13f);
        NoiseBand(b, 0, 0.1f, 200f, 800f, 0.5f, u => Mathf.Exp(-u * 4f), Filter.Low);
        return b;
    });

    // 積み木どうしがぶつかる（カチッ）
    public static AudioClip BlockHitBlock => Cached("blockHitBlock", () =>
    {
        var b = Buf(0.1f);
        Tone(b, 0, 0.1f, 1500, 1480, Wave.Sine, 1f, 0.001f, 70f);
        Tone(b, 0, 0.06f, 3600, 3550, Wave.Sine, 0.4f, 0.001f, 120f);
        NoiseBand(b, 0, 0.015f, 3000f, 7000f, 0.5f, u => 1f - u, Filter.Band);
        return b;
    });

    // 積み木を拾う（ポン）。積んだ数で高くする
    public static AudioClip Pickup => Cached("pickup", () =>
    {
        var b = Buf(0.14f);
        Tone(b, 0, 0.14f, 480, 1150, Wave.Triangle, 0.8f, 0.002f, 18f);
        Tone(b, 0, 0.12f, 960, 2300, Wave.Sine, 0.3f, 0.002f, 25f);
        return b;
    });

    // ジャンプ（ピョン）
    public static AudioClip Jump => Cached("jump", () =>
    {
        var b = Buf(0.2f);
        Tone(b, 0, 0.2f, 230, 560, Wave.Triangle, 0.7f, 0.003f, 9f);
        Tone(b, 0, 0.2f, 460, 1120, Wave.Sine, 0.25f, 0.003f, 12f);
        return b;
    });

    // 顔を飛ばされた（撃墜：ゴッ → ドォォン…キラーン）
    public static AudioClip KO => Cached("ko", () => Blast(1f, false), 0.95f);

    // 場外に落ちた（撃墜：ドォォン…キラーン。少し低く、溜めなし）
    public static AudioClip Fall => Cached("fall", () => Blast(0.85f, true), 0.95f);

    // 幽霊になった（ヒュ～ドロドロ）
    public static AudioClip Ghost => Cached("ghost", () =>
    {
        var b = Buf(0.9f);
        Tone(b, 0, 0.9f, 560, 380, Wave.Sine, 0.7f, 0.12f, 0f, 0.3f, vibRate: 6f, vibDepth: 0.06f);
        Tone(b, 0, 0.9f, 840, 570, Wave.Sine, 0.3f, 0.12f, 0f, 0.3f, vibRate: 6.5f, vibDepth: 0.07f);
        return b;
    });

    // 復活した（キラリン）
    public static AudioClip Respawn => Cached("respawn", () =>
    {
        var b = Buf(0.55f);
        int[] notes = { 84, 88, 91, 96 };
        for (int i = 0; i < notes.Length; i++)
        {
            double f = Midi(notes[i]);
            Tone(b, At(i * 0.06f), 0.32f, f, f, Wave.Triangle, 0.5f, 0.002f, 9f);
            Tone(b, At(i * 0.06f), 0.3f, f * 2, f * 2, Wave.Sine, 0.2f, 0.002f, 12f);
        }
        return b;
    });

    // 脱落した（最後のストックが無くなった）。撃墜の音のあとに鳴る、遠くで響く重い「ゴォーン」
    public static AudioClip Out => Cached("out", () =>
    {
        var b = Buf(2f);
        Resonate(b, 0, 110f, 30f, 1.2f);
        Resonate(b, 0, 165f, 30f, 0.7f);
        Tone(b, 0, 1.4f, 55, 40, Wave.Sine, 0.9f, 0.01f, 2.2f, 0.2f);
        NoiseBand(b, 0, 1.2f, 900f, 120f, 0.5f, u => Mathf.Exp(-u * 3f), Filter.Low);
        Saturate(b, 1.8f);
        Reverb(b, 0.5f, 0.9f);
        return b;
    }, 0.8f);

    // 点が増えた（チャリン）
    public static AudioClip ScoreUp => Cached("scoreUp", () =>
    {
        var b = Buf(0.32f);
        Tone(b, 0, 0.07f, Midi(83), Midi(83), Wave.Pulse25, 0.5f);
        Tone(b, At(0.07f), 0.25f, Midi(88), Midi(88), Wave.Pulse25, 0.5f, 0.002f, 10f);
        return b;
    });

    // 点が減った（ブッ）
    public static AudioClip ScoreDown => Cached("scoreDown", () =>
    {
        var b = Buf(0.24f);
        Tone(b, 0, 0.22f, 190, 150, Wave.Square, 0.5f, 0.003f, 6f);
        return b;
    });

    // 試合開始の笛（ピーッ）
    public static AudioClip StartWhistle => Cached("startWhistle", () =>
    {
        var b = Buf(0.6f);
        Whistle(b, 0, 0.55f, 2900);
        return b;
    });

    // 試合終了の笛（ピーーッ）
    public static AudioClip FinishWhistle => Cached("finishWhistle", () =>
    {
        var b = Buf(1.15f);
        Whistle(b, 0, 1.1f, 2800);
        return b;
    });

    // タイムアップの笛（ピッ ピッ ピーーッ）
    public static AudioClip TimeUpWhistle => Cached("timeUpWhistle", () =>
    {
        var b = Buf(1.15f);
        Whistle(b, 0, 0.13f, 2900);
        Whistle(b, At(0.2f), 0.13f, 2900);
        Whistle(b, At(0.4f), 0.7f, 2900);
        return b;
    });

    // 残り10秒〜（ピッ）と、残り3秒〜（高いピッ）
    public static AudioClip Tick => Cached("tick", () =>
    {
        var b = Buf(0.07f);
        Tone(b, 0, 0.07f, 1000, 1000, Wave.Square, 0.6f, 0.002f, 45f);
        return b;
    });

    public static AudioClip TickLast => Cached("tickLast", () =>
    {
        var b = Buf(0.12f);
        Tone(b, 0, 0.12f, 1480, 1480, Wave.Square, 0.6f, 0.002f, 30f);
        return b;
    });

    // 勝者が決まった（パパパパーン）
    public static AudioClip Victory => Cached("victory", () =>
    {
        var b = Buf(1.8f);
        float[] times = { 0f, 0.13f, 0.26f };
        int[] notes = { 72, 76, 79 };
        for (int i = 0; i < 3; i++)
        {
            double f = Midi(notes[i]);
            Tone(b, At(times[i]), 0.12f, f, f, Wave.Pulse25, 0.45f, 0.003f, 4f);
            Tone(b, At(times[i]), 0.12f, f, f, Wave.Triangle, 0.3f, 0.003f, 4f);
        }
        double top = Midi(84);
        Tone(b, At(0.39f), 1.3f, top, top, Wave.Pulse25, 0.45f, 0.005f, 1.6f, 0.2f, vibRate: 5.5f, vibDepth: 0.006f);
        Tone(b, At(0.39f), 1.3f, top, top, Wave.Triangle, 0.3f, 0.005f, 1.6f, 0.2f);
        foreach (int n in new[] { 60, 64, 67 })
            Tone(b, At(0.39f), 1.35f, Midi(n), Midi(n), Wave.Triangle, 0.22f, 0.01f, 1.8f, 0.2f);
        NoiseBand(b, At(0.39f), 1.2f, 6000f, 6000f, 0.25f, u => Mathf.Exp(-u * 3f), Filter.High);
        return b;
    });

    // 引き分け（ポン ポン ポン ポーン）
    public static AudioClip Draw => Cached("draw", () =>
    {
        var b = Buf(1.3f);
        float[] times = { 0f, 0.2f, 0.4f, 0.6f };
        int[] notes = { 76, 74, 72, 67 };
        float[] lens = { 0.18f, 0.18f, 0.18f, 0.65f };
        for (int i = 0; i < 4; i++)
        {
            double f = Midi(notes[i]);
            Tone(b, At(times[i]), lens[i], f, f, Wave.Triangle, 0.6f, 0.003f, 3f, 0.08f);
            Tone(b, At(times[i]), lens[i], f, f, Wave.Pulse25, 0.18f, 0.003f, 3f, 0.08f);
        }
        return b;
    });

    // ================= BGM =================
    // 試合の BGM（約29秒でループ）。前半8小節はピコピコ、後半8小節はメロディを1オクターブ上げて明るく
    public static AudioClip Bgm => Cached("bgm", BuildBgm, 0.8f);

    static float[] BuildBgm()
    {
        const double bpm = 132.0;
        double eighth = 60.0 / bpm / 2.0;
        const int bars = 16;
        var b = new float[(int)Math.Round(eighth * 8 * bars * Rate)];
        Func<double, int> s = e => (int)Math.Round(e * eighth * Rate);

        int[] roots = { 48, 45, 41, 43 };                     // C - Am - F - G
        int[][] chords = { new[] { 60, 64, 67 }, new[] { 57, 60, 64 }, new[] { 57, 60, 65 }, new[] { 55, 59, 62 } };

        for (int bar = 0; bar < bars; bar++)
        {
            int ci = bar % 4;
            bool second = bar >= 8;
            for (int e = 0; e < 8; e++)
            {
                double at = bar * 8 + e;
                // ベース（ルートとオクターブ上を交互に）
                double fb = Midi(roots[ci] + (e % 2 == 1 ? 12 : 0));
                Tone(b, s(at), (float)(eighth * 0.85), fb, fb, Wave.Triangle, 0.45f, 0.003f, 3f, 0.02f, true);
                // 裏打ちの和音
                if (e % 2 == 1)
                    foreach (int n in chords[ci])
                        Tone(b, s(at), (float)(eighth * 0.5), Midi(n), Midi(n), Wave.Pulse12, 0.07f, 0.002f, 10f, 0.02f, true);
                // ドラム
                if (e == 0 || e == 4) Kick(b, s(at));
                if (e == 2 || e == 6) Snare(b, s(at));
                Hat(b, s(at), 0.11f);
                if (second) Hat(b, s(at + 0.5), 0.06f);
            }
            // 4小節ごとに太鼓（ドンッ）
            if (bar % 4 == 0) Tone(b, s(bar * 8), 0.35f, 95, 60, Wave.Sine, 0.6f, 0.002f, 8f, 0.05f, true);
        }

        // メロディ（音の高さ, 長さ[8分音符]）。8小節を2回（2回目は1オクターブ上・三角波）
        int[] melody =
        {
            76,1, 79,1, 81,1, 79,1, 76,1, 74,1, 72,1, 74,1,
            76,2, 79,1, 76,1, 74,1, 72,1, 69,2,
            69,1, 72,1, 74,1, 76,1, 79,1, 76,1, 74,1, 72,1,
            74,3, 67,1, 69,1, 72,1, 74,2,
            76,1, 79,1, 81,1, 84,1, 81,1, 79,1, 76,1, 79,1,
            81,2, 79,1, 76,1, 74,1, 76,1, 72,2,
            69,1, 72,1, 74,1, 76,1, 79,1, 81,1, 79,1, 76,1,
            74,1, 76,1, 74,1, 72,1, 72,4,
        };
        for (int pass = 0; pass < 2; pass++)
        {
            double pos = pass * 64;
            for (int i = 0; i < melody.Length; i += 2)
            {
                int note = melody[i] + (pass == 1 ? 12 : 0);
                int len = melody[i + 1];
                double f = Midi(note);
                float dur = (float)(len * eighth * 0.92);
                float vib = len >= 2 ? 0.004f : 0f;
                if (pass == 0) Tone(b, s(pos), dur, f, f, Wave.Pulse25, 0.2f, 0.005f, 2.5f, 0.03f, true, 5.5f, vib);
                else Tone(b, s(pos), dur, f, f, Wave.Triangle, 0.3f, 0.005f, 2.5f, 0.03f, true, 5.5f, vib);
                pos += len;
            }
        }
        return b;
    }

    static void Kick(float[] b, int start)
    {
        Tone(b, start, 0.16f, 150, 45, Wave.Sine, 0.9f, 0.001f, 16f, 0.02f, true);
    }

    static void Snare(float[] b, int start)
    {
        NoiseBand(b, start, 0.15f, 1800f, 1800f, 0.35f, u => Mathf.Exp(-u * 6f), Filter.Band, true);
        Tone(b, start, 0.1f, 190, 170, Wave.Triangle, 0.25f, 0.001f, 25f, 0.02f, true);
    }

    static void Hat(float[] b, int start, float vol)
    {
        NoiseBand(b, start, 0.05f, 8000f, 8000f, vol, u => Mathf.Exp(-u * 5f), Filter.High, true);
    }

    // ================= 部品 =================
    // ごく短いノイズ（3ミリ秒）で弾いて、鋭いフィルターで共鳴させる（木や金属を叩いたような、生っぽい響き）
    // q が大きいほど細く長く響く
    static void Resonate(float[] b, int start, float freq, float q, float vol)
    {
        double w0 = 2.0 * Math.PI * freq / Rate;
        double alpha = Math.Sin(w0) / (2.0 * q);
        double a0 = 1.0 + alpha;
        double b0 = alpha / a0, b2 = -alpha / a0, a1 = -2.0 * Math.Cos(w0) / a0, a2 = (1.0 - alpha) / a0;
        int burst = At(0.003f);
        int n = Mathf.Min(b.Length - start, burst + Mathf.CeilToInt((float)(7.0 * q / (Math.PI * freq)) * Rate));
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        float gain = vol * q * 0.4f;
        for (int i = 0; i < n; i++)
        {
            double x = i < burst ? s_rng.NextDouble() * 2.0 - 1.0 : 0.0;
            double y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            b[start + i] += (float)y * gain;
        }
    }

    // 叩いて響かせる（ring 秒で 1/1000 まで小さくなる）。長いほど細く鋭い共鳴になり、鐘のように響く
    static void Ring(float[] b, int start, float freq, float ring, float vol)
    {
        float q = ring / 6.9f * Mathf.PI * freq;
        Resonate(b, start, freq, q, vol);
    }

    // こだま（delay 秒ごとに、だんだん小さく・こもりながら返ってくる）
    static void Echo(float[] b, float delay, float gain, int repeats)
    {
        var dry = (float[])b.Clone();
        int d = At(delay);
        float g = gain;
        for (int r = 1; r <= repeats; r++, g *= gain)
        {
            int offset = d * r;
            float lp = 0f;
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * (4000f / r) / Rate);   // 遠いこだまほどこもる
            for (int i = 0; i + offset < b.Length; i++)
            {
                lp += a * (dry[i] - lp);
                b[i + offset] += lp * g;
            }
        }
    }

    // 観客の歓声「ワーッ」（「あ」の声に近い帯域のノイズを、ふくらませて揺らす）
    static void Crowd(float[] b, int start, float dur, float vol)
    {
        Func<float, float> swell = u => Mathf.Min(1f, u * 5f) * Mathf.Pow(1f - u, 1.5f);
        NoiseBand(b, start, dur, 750f, 750f, vol, u => swell(u) * (0.8f + 0.2f * Mathf.Sin(u * 37f)), Filter.Band);
        NoiseBand(b, start, dur, 1250f, 1250f, vol * 0.7f, u => swell(u) * (0.8f + 0.2f * Mathf.Sin(u * 29f + 1f)), Filter.Band);
        NoiseBand(b, start, dur, 2600f, 2600f, vol * 0.3f, u => swell(u) * (0.7f + 0.3f * Mathf.Sin(u * 53f + 2f)), Filter.Band);
    }

    // ひずませて、音に厚みと迫力を出す（大きいところほど丸く押しつぶす）
    static void Saturate(float[] b, float drive)
    {
        float max = 0f;
        foreach (var v in b) max = Mathf.Max(max, Mathf.Abs(v));
        if (max <= 0f) return;
        float norm = (float)Math.Tanh(drive);
        for (int i = 0; i < b.Length; i++) b[i] = (float)Math.Tanh(b[i] / max * drive) / norm;
    }

    // 残響（広い場所で鳴らしたような響き）。mix：響きの量　feedback：響きの長さ（0.8〜0.9）
    static void Reverb(float[] b, float mix, float feedback)
    {
        if (mix <= 0f) return;
        int[] combLengths = { 1557, 1617, 1491, 1422, 1277, 1356 };
        int[] allpassLengths = { 556, 441, 341 };
        var wet = new float[b.Length];
        foreach (int len in combLengths)
        {
            var line = new float[len];
            int idx = 0;
            float store = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float o = line[idx];
                store = o * 0.65f + store * 0.35f;   // 高い音ほど早く消える
                line[idx] = b[i] + store * feedback;
                wet[i] += o;
                idx = (idx + 1) % len;
            }
        }
        foreach (int len in allpassLengths)
        {
            var line = new float[len];
            int idx = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float bo = line[idx];
                float o = -wet[i] + bo;
                line[idx] = wet[i] + bo * 0.5f;
                wet[i] = o;
                idx = (idx + 1) % len;
            }
        }
        float dryMax = 0f, wetMax = 0f;
        for (int i = 0; i < b.Length; i++) { dryMax = Mathf.Max(dryMax, Mathf.Abs(b[i])); wetMax = Mathf.Max(wetMax, Mathf.Abs(wet[i])); }
        float scale = wetMax > 0f ? dryMax / wetMax : 0f;
        for (int i = 0; i < b.Length; i++) b[i] = b[i] * (1f - mix * 0.4f) + wet[i] * scale * mix;
    }

    // 審判の笛（細かく震える高い音 ＋ 息の音）
    static void Whistle(float[] b, int start, float dur, double f)
    {
        Tone(b, start, dur, f, f, Wave.Sine, 0.8f, 0.015f, 0f, 0.06f, vibRate: 32f, vibDepth: 0.028f);
        NoiseBand(b, start, dur, 3000f, 3000f, 0.12f, u => 1f, Filter.High);
    }

    // 音を1つ足す。周波数は f0 → f1 へ指数で変化。attack 秒で立ち上がり、decay の速さで減衰し、最後の release 秒で消える
    // wrap = true なら、バッファの端を越えた分を先頭に回す（BGM のループをつなぎ目なくするため）
    static void Tone(float[] b, int start, float dur, double f0, double f1, Wave w, float vol,
                     float attack = 0.003f, float decay = 0f, float release = 0.01f, bool wrap = false,
                     float vibRate = 0f, float vibDepth = 0f)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt(dur * Rate));
        double phase = 0;
        double ratio = f1 / f0;
        for (int i = 0; i < n; i++)
        {
            int idx = start + i;
            if (idx >= b.Length) { if (!wrap) break; idx %= b.Length; }
            float t = (float)i / Rate;
            double f = ratio == 1.0 ? f0 : f0 * Math.Pow(ratio, (double)i / n);
            if (vibDepth > 0f) f *= 1.0 + vibDepth * Math.Sin(2.0 * Math.PI * vibRate * t);
            phase += f / Rate;
            float env = Mathf.Min(1f, t / Mathf.Max(attack, 0.0001f));
            if (decay > 0f) env *= Mathf.Exp(-decay * t);
            float rem = dur - t;
            if (rem < release) env *= Mathf.Max(0f, rem / release);
            b[idx] += Osc(w, phase) * env * vol;
        }
    }

    // 帯域を絞ったノイズ（cutoff を c0 → c1 へ変える）。env は 0〜1 の進み具合から音量を返す
    static void NoiseBand(float[] b, int start, float dur, float c0, float c1, float vol, Func<float, float> env, Filter filter, bool wrap = false)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt(dur * Rate));
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            int idx = start + i;
            if (idx >= b.Length) { if (!wrap) break; idx %= b.Length; }
            float u = (float)i / n;
            float c = c0 * Mathf.Pow(c1 / c0, u);
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * c / Rate);
            float a2 = 1f - Mathf.Exp(-2f * Mathf.PI * c * 0.35f / Rate);
            float x = (float)(s_rng.NextDouble() * 2.0 - 1.0);
            lp += a * (x - lp);
            lp2 += a2 * (lp - lp2);
            float v = filter == Filter.High ? x - lp : filter == Filter.Low ? lp : lp - lp2;
            b[idx] += v * env(u) * vol;
        }
    }

    static float Osc(Wave w, double phase)
    {
        double p = phase - Math.Floor(phase);
        switch (w)
        {
            case Wave.Sine: return (float)Math.Sin(2.0 * Math.PI * p);
            case Wave.Triangle: return (float)(p < 0.5 ? 4.0 * p - 1.0 : 3.0 - 4.0 * p);
            case Wave.Square: return p < 0.5 ? 0.8f : -0.8f;
            case Wave.Pulse25: return p < 0.25 ? 0.8f : -0.8f;
            default: return p < 0.125 ? 0.8f : -0.8f;
        }
    }

    static double Midi(int note) => 440.0 * Math.Pow(2.0, (note - 69) / 12.0);
    static float[] Buf(float seconds) => new float[Mathf.Max(1, Mathf.CeilToInt(seconds * Rate))];
    static int At(float seconds) => Mathf.RoundToInt(seconds * Rate);

    static AudioClip Cached(string key, Func<float[]> build, float peak = 0.9f)
    {
        if (s_cache.TryGetValue(key, out var clip) && clip != null) return clip;
        var data = build();
        float max = 0f;
        foreach (var v in data) max = Mathf.Max(max, Mathf.Abs(v));
        if (max > 0f)
            for (int i = 0; i < data.Length; i++) data[i] *= peak / max;
        clip = AudioClip.Create("Synth_" + key, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        s_cache[key] = clip;
        return clip;
    }
}
