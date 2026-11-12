using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Chencho.Core;

namespace Chencho.Overlay;

/// Reconnects forever; while disconnected, the last known sessions stay on screen.
/// Everything but the socket I/O runs on the UI thread.
internal sealed class AgentLink
{
    public enum LinkStatus { Connecting, Online, NotPaired, Offline }

    /// `SessionId` is null when the notice is about the add button's request for the session library.
    public sealed record Notice(string? SessionId, string Message);

    public SessionList List { get; } = new();
    public LinkStatus Status { get; private set; } = LinkStatus.Connecting;
    public Notice? CurrentNotice { get; private set; }

    /// After any change to the list, the status or the notice.
    public event Action? Changed;

    private readonly Dispatcher dispatcher;
    private ClientWebSocket? socket;
    private TimeSpan retryDelay = MinRetry;
    private ClientMessage? lastSent;
    private Action<IReadOnlyList<LibraryEntry>>? libraryReply;
    private DispatcherTimer? noticeExpiry;

    private static readonly TimeSpan MinRetry = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(10);
    /// A socket can die silently while the PC sleeps; an unanswered ping notices it.
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan NoticeVisible = TimeSpan.FromSeconds(4);
    private const int MaxMessageSize = 4 << 20;

    public AgentLink(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
    }

    public void Start() => Connect();

    public void Send(ClientMessage message)
    {
        if (Status != LinkStatus.Online || socket is not { State: WebSocketState.Open } open)
        {
            Show(new Notice(message.SessionId, "not connected to the agent"));
            return;
        }
        lastSent = message;
        var bytes = Encoding.UTF8.GetBytes(message.Json);
        _ = SendAsync(open, bytes);
    }

    /// `reply` runs once, with the sessions that can be pinned, most recent first.
    public void RequestLibrary(Action<IReadOnlyList<LibraryEntry>> reply)
    {
        libraryReply = reply;
        Send(new ClientMessage.LibraryRequest());
    }

    /// Shows a failure beside a session's creature, or beside the add button when `sessionId` is null.
    public void Show(Notice notice)
    {
        CurrentNotice = notice;
        noticeExpiry?.Stop();
        noticeExpiry = new DispatcherTimer(NoticeVisible, DispatcherPriority.Normal, (_, _) =>
        {
            noticeExpiry?.Stop();
            CurrentNotice = null;
            Changed?.Invoke();
        }, dispatcher);
        noticeExpiry.Start();
        Changed?.Invoke();
    }

    private static async Task SendAsync(ClientWebSocket socket, byte[] bytes)
    {
        try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
        catch (Exception) { /* the receive loop notices a dead socket and reconnects */ }
    }

    private void Connect()
    {
        if (AgentConfig.Token() is not { } token)
        {
            SetStatus(LinkStatus.NotPaired);
            Retry();
            return;
        }
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = PingEvery;
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(10);
        this.socket = socket;
        _ = Receive(socket, AgentConfig.WebSocketUrl(token));
    }

    private async Task Receive(ClientWebSocket socket, Uri url)
    {
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        try
        {
            await socket.ConnectAsync(url, CancellationToken.None);
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("closed by the agent");
                message.Write(buffer, 0, result.Count);
                if (message.Length > MaxMessageSize) throw new WebSocketException("message too large");
                if (!result.EndOfMessage) continue;
                var text = result.MessageType == WebSocketMessageType.Text ? Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length) : null;
                message.SetLength(0);
                if (text is not null) await dispatcher.InvokeAsync(() => Handle(text));
            }
        }
        catch (Exception)
        {
            socket.Dispose();
            await dispatcher.InvokeAsync(() =>
            {
                if (this.socket != socket) return;
                this.socket = null;
                SetStatus(LinkStatus.Offline);
                Retry();
            });
        }
    }

    private void Handle(string text)
    {
        if (ServerMessage.Parse(text) is not { } parsed) return;
        // The agent answers a bad token by refusing the upgrade, so any message means we're in.
        Status = LinkStatus.Online;
        retryDelay = MinRetry;
        switch (parsed)
        {
            case ServerMessage.Error error:
                // The agent's errors don't name a session; they answer the last message sent.
                if (lastSent is not null) Show(new Notice(lastSent.SessionId, error.Message));
                break;
            case ServerMessage.Library library:
                var reply = libraryReply;
                libraryReply = null;
                reply?.Invoke(library.Sessions);
                break;
            default:
                List.Apply(parsed);
                break;
        }
        Changed?.Invoke();
    }

    private void SetStatus(LinkStatus status)
    {
        if (Status == status) return;
        Status = status;
        Changed?.Invoke();
    }

    private void Retry()
    {
        var delay = retryDelay;
        retryDelay = TimeSpan.FromTicks(Math.Min((retryDelay * 2).Ticks, MaxRetry.Ticks));
        var timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Connect();
        };
        timer.Start();
    }
}
