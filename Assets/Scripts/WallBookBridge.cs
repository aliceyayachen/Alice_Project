using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Connects the floor (Unity) to the wall book (the browser page sicamour-wall.html).
///
/// What it does:
///   - Runs a tiny local connection point at ws://localhost:8787/book that the book connects to.
///   - About 30 times a second, sends each page zone's Proximity (0-1) to the book.
///   - When any blob arrives in a page zone, tells the book "this page: arrived",
///     plus where along the page the blob came in (so the light enters at that spot).
///   - Shows in the Inspector whether the book is connected and what each page is doing.
///
/// It only READS the PageDestination zones and the blobs. It doesn't change them.
/// Everything stays on this computer (localhost); nothing goes to the internet.
/// </summary>
public class WallBookBridge : MonoBehaviour
{
    [Header("Page zones on the floor")]
    [SerializeField] private PageDestination leftZone;
    [SerializeField] private PageDestination rightZone;

    [Header("Where the light enters the page")]
    [Tooltip("If on, the light enters the page at the point matching where the blob entered the zone. If off, it enters at the page's middle.")]
    [SerializeField] private bool useBlobEntryPoint = true;

    [Header("Connection")]
    [Tooltip("Must match the book's address: ws://localhost:8787/book")]
    [SerializeField] private int port = 8787;
    [SerializeField, Range(5f, 60f)] private float sendsPerSecond = 30f;

    [Header("Live readout (Play Mode)")]
    [SerializeField] private bool bookConnected;
    [SerializeField] private string leftPageState = "-";
    [SerializeField] private string rightPageState = "-";
    [SerializeField] private bool comparisonRunning;
    [SerializeField] private string lastMessageFromBook = "";

    // ---- connection internals (run on background threads) ----
    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private TcpListener listener;
    private Thread acceptThread;
    private volatile bool running;
    private readonly object clientLock = new object();
    private TcpClient client;
    private NetworkStream stream;
    private volatile bool clientReady;
    private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
    private readonly ConcurrentQueue<string> events = new ConcurrentQueue<string>();
    private float sendTimer;

    // Read by BlobAbsorber: is the book connected, and what is each page doing?
    public bool BookConnected => bookConnected;
    public string StateOf(PageDestination.Page page) => page == PageDestination.Page.Left ? leftPageState : rightPageState;

    private void OnEnable()
    {
        if (leftZone == null || rightZone == null)
            Debug.LogWarning("WallBookBridge: assign both Left Zone and Right Zone.", this);

        StartServer();
    }

    private void OnDisable()
    {
        StopServer();
    }

    private void OnApplicationQuit()
    {
        StopServer();
    }

    private void Update()
    {
        // Messages/events from the background threads are handled here on Unity's main thread.
        while (events.TryDequeue(out string note)) Debug.Log("WallBookBridge: " + note, this);
        while (incoming.TryDequeue(out string message)) HandleBookMessage(message);
        bookConnected = clientReady;
        if (!bookConnected) { leftPageState = "-"; rightPageState = "-"; comparisonRunning = false; }

        if (!clientReady) return;

        sendTimer += Time.deltaTime;
        if (sendTimer >= 1f / sendsPerSecond)
        {
            sendTimer = 0f;
            // Only idle pages get a preview: a page that's still revealing / holding / covering gets 0,
            // so a blob hanging near a busy page doesn't make its preview flicker.
            float left = leftZone != null && PageCanPreview(leftPageState) ? leftZone.Proximity : 0f;
            float right = rightZone != null && PageCanPreview(rightPageState) ? rightZone.Proximity : 0f;
            Send(string.Format(CultureInfo.InvariantCulture,
                "{{\"type\":\"proximity\",\"left\":{0:0.###},\"right\":{1:0.###}}}", left, right));
        }
    }

    // LateUpdate runs after every PageDestination has updated this frame,
    // so ArrivedThisFrame is reliable here.
    private void LateUpdate()
    {
        if (leftZone != null && leftZone.ArrivedThisFrame) SendArrival(leftZone, "left");
        if (rightZone != null && rightZone.ArrivedThisFrame) SendArrival(rightZone, "right");
    }

    private void SendArrival(PageDestination zone, string page)
    {
        float entry = 0.5f;
        Transform blob = zone.LastArrivedBlob; // whichever blob just arrived
        if (useBlobEntryPoint && blob != null)
        {
            // Position across the zone's width (its local Z). Facing the wall, +Z is the left side,
            // so the page's left edge (0) is at +Z and its right edge (1) is at -Z.
            Vector3 local = zone.transform.InverseTransformPoint(blob.position);
            entry = Mathf.Clamp01(0.5f - local.z);
        }

        if (!clientReady)
        {
            Debug.Log("WallBookBridge: " + page + " page arrival, but the book isn't connected.", this);
            return;
        }
        Send(string.Format(CultureInfo.InvariantCulture,
            "{{\"type\":\"arrive\",\"page\":\"{0}\",\"x\":{1:0.###}}}", page, entry));
        Debug.Log("WallBookBridge: sent " + page + " page arrival (entry " + entry.ToString("0.00") + ")", this);
    }

    // "-" means the book hasn't reported yet: allow the preview.
    private static bool PageCanPreview(string state) => state == "idle" || state == "-";

    private void HandleBookMessage(string message)
    {
        lastMessageFromBook = message;
        // Small, known messages from the book: read the fields we need without a JSON library.
        if (message.Contains("\"type\":\"state\""))
        {
            leftPageState = ReadString(message, "left");
            rightPageState = ReadString(message, "right");
            comparisonRunning = message.Contains("\"collator\":true");
        }
        else if (message.Contains("\"type\":\"arrived\"") && message.Contains("\"accepted\":false"))
        {
            Debug.Log("WallBookBridge: the book was busy, so it ignored that arrival: " + message, this);
        }
    }

    private static string ReadString(string json, string key)
    {
        string pattern = "\"" + key + "\":\"";
        int start = json.IndexOf(pattern, StringComparison.Ordinal);
        if (start < 0) return "-";
        start += pattern.Length;
        int end = json.IndexOf('"', start);
        return end > start ? json.Substring(start, end - start) : "-";
    }

    // For testing from the Inspector: click the ⋮ menu on this component.
    [ContextMenu("Test: Left page arrives")]
    private void TestLeftArrives() { if (leftZone != null) SendArrival(leftZone, "left"); }

    [ContextMenu("Test: Right page arrives")]
    private void TestRightArrives() { if (rightZone != null) SendArrival(rightZone, "right"); }

    // ------------------------------------------------------------------
    // Minimal WebSocket server (localhost only). You don't need to edit below here.
    // ------------------------------------------------------------------

    private void StartServer()
    {
        if (running) return;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();
            running = true;
            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "WallBookBridge" };
            acceptThread.Start();
            Debug.Log("WallBookBridge: waiting for the book on ws://localhost:" + port + "/book", this);
        }
        catch (Exception e)
        {
            Debug.LogError("WallBookBridge: couldn't start on port " + port + ". Is another copy running? " + e.Message, this);
        }
    }

    private void StopServer()
    {
        running = false;
        CloseClient();
        try { listener?.Stop(); } catch { }
        listener = null;
        bookConnected = false;
    }

    private void AcceptLoop()
    {
        while (running)
        {
            TcpClient newClient;
            try { newClient = listener.AcceptTcpClient(); }
            catch { break; } // listener stopped

            // One book at a time: a new connection replaces the old one (e.g. after a page reload).
            CloseClient();
            Thread clientThread = new Thread(() => ClientLoop(newClient)) { IsBackground = true, Name = "WallBookBridge client" };
            clientThread.Start();
        }
    }

    private void ClientLoop(TcpClient tcp)
    {
        try
        {
            tcp.NoDelay = true;
            NetworkStream s = tcp.GetStream();
            if (!Handshake(s)) { tcp.Close(); return; }

            lock (clientLock) { client = tcp; stream = s; }
            clientReady = true;
            events.Enqueue("book connected");

            while (running)
            {
                string text = ReadFrame(s, out bool closed);
                if (closed) break;
                if (text != null) incoming.Enqueue(text);
            }
        }
        catch { /* connection dropped */ }

        bool wasCurrent;
        lock (clientLock) { wasCurrent = client == tcp; }
        if (wasCurrent)
        {
            clientReady = false;
            CloseClient();
            if (running) events.Enqueue("book disconnected - it will reconnect by itself");
        }
        else
        {
            try { tcp.Close(); } catch { }
        }
    }

    private static bool Handshake(NetworkStream s)
    {
        // Read the HTTP upgrade request.
        var request = new StringBuilder();
        var buffer = new byte[1024];
        while (!request.ToString().Contains("\r\n\r\n"))
        {
            int n = s.Read(buffer, 0, buffer.Length);
            if (n <= 0) return false;
            request.Append(Encoding.ASCII.GetString(buffer, 0, n));
            if (request.Length > 16384) return false;
        }

        string key = null;
        foreach (string line in request.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None))
        {
            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                key = line.Substring(line.IndexOf(':') + 1).Trim();
        }
        if (key == null) return false;

        string accept;
        using (SHA1 sha = SHA1.Create())
            accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + WebSocketGuid)));

        string response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
        byte[] bytes = Encoding.ASCII.GetBytes(response);
        s.Write(bytes, 0, bytes.Length);
        return true;
    }

    private static void ReadExactly(NetworkStream s, byte[] buffer, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int n = s.Read(buffer, offset, count - offset);
            if (n <= 0) throw new Exception("closed");
            offset += n;
        }
    }

    /// <summary>Reads one WebSocket frame. Returns text for text frames, null for others.</summary>
    private string ReadFrame(NetworkStream s, out bool closed)
    {
        closed = false;
        byte[] header = new byte[2];
        ReadExactly(s, header, 2);
        int opcode = header[0] & 0x0F;
        bool masked = (header[1] & 0x80) != 0;
        long length = header[1] & 0x7F;

        if (length == 126)
        {
            byte[] ext = new byte[2];
            ReadExactly(s, ext, 2);
            length = (ext[0] << 8) | ext[1];
        }
        else if (length == 127)
        {
            byte[] ext = new byte[8];
            ReadExactly(s, ext, 8);
            length = 0;
            for (int i = 0; i < 8; i++) length = (length << 8) | ext[i];
        }
        if (length > 1_000_000) { closed = true; return null; }

        byte[] mask = new byte[4];
        if (masked) ReadExactly(s, mask, 4);

        byte[] payload = new byte[length];
        if (length > 0) ReadExactly(s, payload, (int)length);
        if (masked) for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];

        if (opcode == 8) { closed = true; return null; }                        // close
        if (opcode == 9) { SendFrame(s, payload, 0xA); return null; }            // ping -> pong
        if (opcode == 1) return Encoding.UTF8.GetString(payload);               // text
        return null;
    }

    private void Send(string text)
    {
        NetworkStream s;
        lock (clientLock) { s = stream; }
        if (s == null) return;
        try { SendFrame(s, Encoding.UTF8.GetBytes(text), 0x1); }
        catch
        {
            clientReady = false;
            CloseClient();
        }
    }

    private readonly object sendLock = new object();

    private void SendFrame(NetworkStream s, byte[] payload, int opcode)
    {
        byte[] header;
        if (payload.Length < 126)
            header = new byte[] { (byte)(0x80 | opcode), (byte)payload.Length };
        else if (payload.Length <= 65535)
            header = new byte[] { (byte)(0x80 | opcode), 126, (byte)(payload.Length >> 8), (byte)payload.Length };
        else
            throw new Exception("message too long");

        lock (sendLock)
        {
            s.Write(header, 0, header.Length);
            s.Write(payload, 0, payload.Length);
        }
    }

    private void CloseClient()
    {
        lock (clientLock)
        {
            try { stream?.Close(); } catch { }
            try { client?.Close(); } catch { }
            stream = null;
            client = null;
        }
        clientReady = false;
    }
}