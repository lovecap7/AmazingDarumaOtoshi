using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using UnityEngine;

// LAN の中で「へやばんごう」(4けたの数字)からホストを見つける。IP アドレスを入れなくてもつながるようにする。
//  ・ホスト: へやを開くと番号を決めて、UDP のポート kPort で「その番号の部屋はある？」という問い合わせを待ち、
//            番号が合えば、問い合わせてきた人にゲームのポート番号を返す(返事の送り元がホストの IP アドレスになる)
//            シーンをまたいで答え続ける(試合のシーンに移ってからも、遅れて来た人が見つけられる)。止めたら自分で消える
//  ・さんか: Search で、LAN 全体(ブロードキャスト)とこの PC に問い合わせを送り続け、返事が来たらその IP アドレスにつなぐ
// 同じ LAN(Wi-Fi)の中だけで使える。ブロードキャストを通さないネットワークでは見つからないので、IP アドレスでもつなげるようにしておく。
// (インターネット越しに遊ぶなら、ここを Unity Relay の参加コードに置き換える)
public class NetRoom : MonoBehaviour
{
    // 問い合わせを待つポート。使われていたら次を試す(同じ PC でホストを2つ開いたときなど)。さんかする人は全部に問い合わせる
    public const int kPort = 47777;
    public const int kPortCount = 4;
    const string kAsk = "DARUMA-ROOM?";
    const string kAnswer = "DARUMA-ROOM!";

    static NetRoom s_instance;

    // いま開いているへやの番号(開いていなければ 0)
    public static int Current => s_instance != null ? s_instance.m_room : 0;
    // 問い合わせを待てているか(待てていなければ、番号では見つけてもらえない)
    public static bool IsAnswering => s_instance != null && s_instance.m_udp != null;

    UdpClient m_udp;
    int m_room;
    ushort m_gamePort;

    // ホスト: へやを開く(番号を決めて問い合わせを待ち始める)。もう開いていれば同じ番号のまま
    public static int Open(ushort gamePort)
    {
        if (s_instance != null) return s_instance.m_room;
        var go = new GameObject("NetRoom");
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<NetRoom>();
        s_instance.m_room = UnityEngine.Random.Range(1000, 10000);
        s_instance.m_gamePort = gamePort;
        for (int i = 0; i < kPortCount && s_instance.m_udp == null; i++)
        {
            try
            {
                s_instance.m_udp = new UdpClient(new IPEndPoint(IPAddress.Any, kPort + i));
            }
            catch (SocketException)
            {
                // 同じ PC でほかのホストが使っているときなど。次のポートを試す
            }
        }
        // どれも使えなければ、IP アドレスでならつなげる
        if (s_instance.m_udp != null) IgnoreConnectionReset(s_instance.m_udp);
        else Debug.LogWarning($"[NetRoom] ポート {kPort}〜{kPort + kPortCount - 1} がどれも使えません(へやばんごうでは見つけてもらえません)");
        return s_instance.m_room;
    }

    // Windows では、届かなかった送信の知らせ(ICMP)で次の受信が失敗するので、それを切る
    static void IgnoreConnectionReset(UdpClient udp)
    {
        if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor) return;
        try
        {
            const int SIO_UDP_CONNRESET = -1744830452;
            udp.Client.IOControl(SIO_UDP_CONNRESET, new byte[] { 0 }, null);
        }
        catch (Exception)
        {
            // 使えない環境では、そのまま(受信の失敗は捕まえて続ける)
        }
    }

    public static void Close()
    {
        if (s_instance != null) Destroy(s_instance.gameObject);
    }

    void Update()
    {
        // ホストをやめたら閉じる
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer)
        {
            Destroy(gameObject);
            return;
        }
        if (m_udp == null) return;
        try
        {
            while (m_udp.Available > 0)
            {
                var from = new IPEndPoint(IPAddress.Any, 0);
                string text = Encoding.ASCII.GetString(m_udp.Receive(ref from));
                if (text != kAsk + m_room) continue;
                byte[] answer = Encoding.ASCII.GetBytes($"{kAnswer}{m_room}!{m_gamePort}");
                m_udp.Send(answer, answer.Length, from);
            }
        }
        catch (SocketException)
        {
            // 相手がいなくなったときなどに出る。次のフレームでまた待つ
        }
    }

    void OnDestroy()
    {
        m_udp?.Close();
        m_udp = null;
        if (s_instance == this) s_instance = null;
    }

    //========================================
    // さんか: 番号でホストを探す
    //========================================

    public sealed class Search : IDisposable
    {
        const float kInterval = 0.5f;   // 問い合わせを送り直す間隔(秒)

        readonly UdpClient m_udp;
        readonly int m_room;
        readonly List<IPEndPoint> m_targets = new List<IPEndPoint>();
        float m_nextSend;

        public float Elapsed { get; private set; }
        public bool IsFound { get; private set; }
        public string Address { get; private set; }
        public ushort GamePort { get; private set; }

        public Search(int room)
        {
            m_room = room;
            m_udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
            IgnoreConnectionReset(m_udp);
            // LAN 全体・つないでいるネットワークごとのブロードキャスト・この PC(同じ PC でホストしているとき)
            var addresses = new List<IPAddress> { IPAddress.Broadcast };
            addresses.AddRange(DirectedBroadcasts());
            addresses.Add(IPAddress.Loopback);
            foreach (var a in addresses)
            {
                for (int i = 0; i < kPortCount; i++) m_targets.Add(new IPEndPoint(a, kPort + i));
            }
        }

        // 毎フレーム呼ぶ
        public void Update(float dt)
        {
            if (IsFound) return;
            Elapsed += dt;
            m_nextSend -= dt;
            if (m_nextSend <= 0.0f)
            {
                m_nextSend = kInterval;
                byte[] ask = Encoding.ASCII.GetBytes(kAsk + m_room);
                foreach (var t in m_targets)
                {
                    try { m_udp.Send(ask, ask.Length, t); }
                    catch (SocketException) { }   // 送れないネットワークは飛ばす
                }
            }
            try
            {
                while (m_udp.Available > 0)
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    string text = Encoding.ASCII.GetString(m_udp.Receive(ref from));
                    string head = $"{kAnswer}{m_room}!";
                    if (!text.StartsWith(head) || !ushort.TryParse(text.Substring(head.Length), out var port)) continue;
                    IsFound = true;
                    Address = from.Address.ToString();
                    GamePort = port;
                    return;
                }
            }
            catch (SocketException)
            {
                // 届かなかった問い合わせの知らせ(Windows)など。気にせず探し続ける
            }
        }

        public void Dispose() => m_udp.Close();

        // つないでいるネットワークごとのブロードキャストアドレス(例: 192.168.1.10 / 255.255.255.0 → 192.168.1.255)
        static IEnumerable<IPAddress> DirectedBroadcasts()
        {
            var list = new List<IPAddress>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var a in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (a.Address.AddressFamily != AddressFamily.InterNetwork || a.IPv4Mask == null) continue;
                        byte[] ip = a.Address.GetAddressBytes(), mask = a.IPv4Mask.GetAddressBytes();
                        var b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                        list.Add(new IPAddress(b));
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NetRoom] ネットワークを調べられませんでした: " + e.Message);
            }
            return list;
        }
    }
}
