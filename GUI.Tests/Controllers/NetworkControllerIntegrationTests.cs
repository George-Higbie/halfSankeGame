// <copyright file="NetworkControllerIntegrationTests.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann, George Higbie, & CS 3500 Course Staff + Associates. All rights reserved.
// </copyright>
// Author: Alex Waldmann
// Date: 2026-04-12

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using GUI.Components.Controllers;
using GUI.Components.Models;

namespace GUI.Tests.Controllers;

/// <summary>
/// Integration tests for <see cref="NetworkController"/> that spin up an in-process
/// <see cref="TcpListener"/> to exercise the full TCP read loop and handshake paths.
/// </summary>
[TestClass]
public class NetworkControllerIntegrationTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    private static readonly FieldInfo _writerField =
        typeof(NetworkController).GetField(
            "_writer",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

    // ==================== Helpers ====================

    /// <summary>Starts a TCP listener on a random loopback port. Caller must call Stop().</summary>
    private static (TcpListener listener, int port) StartFakeServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return (listener, ((IPEndPoint)listener.LocalEndpoint!).Port);
    }

    /// <summary>
    /// Connects <paramref name="nc"/> to the fake server, accepts the client on the server side,
    /// reads the two hello lines, and sends the handshake (player ID + world size).
    /// Returns the server-side client and writer for the test to send further lines.
    /// </summary>
    private static async Task<(TcpClient serverClient, StreamWriter serverWriter)> DoHandshakeAsync(
        TcpListener listener,
        NetworkController nc,
        int playerId,
        int worldSize,
        string playerName = "Tester",
        int skinIndex = 0)
    {
        var connectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        nc.OnConnected += () => connectedTcs.TrySetResult();

        var connectTask = nc.ConnectAsync(
            "127.0.0.1",
            listener.LocalEndpoint is IPEndPoint ep ? ep.Port : 0,
            playerName,
            skinIndex);

        var serverClient = await listener.AcceptTcpClientAsync();
        var stream = serverClient.GetStream();
        var serverReader = new StreamReader(stream, Encoding.UTF8);
        var serverWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        // Read the two hello lines the client sends: name then skin index.
        await serverReader.ReadLineAsync();
        await serverReader.ReadLineAsync();

        // Send handshake responses.
        await serverWriter.WriteLineAsync(playerId.ToString());
        await serverWriter.WriteLineAsync(worldSize.ToString());

        await connectTask;
        await connectedTcs.Task.WaitAsync(_timeout);

        return (serverClient, serverWriter);
    }

    /// <summary>
    /// A <see cref="StreamWriter"/> subclass whose <see cref="WriteLineAsync(string?)"/> always
    /// returns a faulted <see cref="Task"/>.  Because it wraps <see cref="Stream.Null"/> all
    /// disposal-time flushes succeed without throwing.
    /// </summary>
    private sealed class FailingWriter : StreamWriter
    {
        public FailingWriter() : base(Stream.Null, Encoding.UTF8, leaveOpen: true) { }

        public override Task WriteLineAsync(string? value)
            => Task.FromException(new IOException("Simulated write failure"));
    }

    // ==================== Handshake ====================

    [TestMethod]
    public async Task NetworkController_ConnectAsync_ValidServer_HandshakeSetsPropertiesAndFiresEvents()
    {
        var (listener, _) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            int? receivedId = null;
            int? receivedSize = null;
            bool connectedFired = false;

            nc.OnPlayerIdReceived += id => receivedId = id;
            nc.OnWorldSizeReceived += ws => receivedSize = ws;
            nc.OnConnected += () => connectedFired = true;

            var (serverClient, _) = await DoHandshakeAsync(listener, nc, 42, 2000);
            serverClient.Dispose();

            Assert.AreEqual(42, receivedId);
            Assert.AreEqual(2000, receivedSize);
            Assert.IsTrue(connectedFired);
            Assert.IsTrue(nc.IsConnected);
            Assert.AreEqual(42, nc.PlayerId);
            Assert.AreEqual(2000, nc.WorldSize);
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task NetworkController_ConnectAsync_WithSkinIndex_SendsCorrectHelloLines()
    {
        var (listener, port) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            string? gotName = null;
            string? gotSkin = null;

            var connectTask = nc.ConnectAsync("127.0.0.1", port, "MySkin", 7);

            var serverClient = await listener.AcceptTcpClientAsync();
            var sr = new StreamReader(serverClient.GetStream(), Encoding.UTF8);
            var sw = new StreamWriter(serverClient.GetStream(), Encoding.UTF8) { AutoFlush = true };

            gotName = await sr.ReadLineAsync();
            gotSkin = await sr.ReadLineAsync();

            // Complete the handshake so ConnectAsync returns.
            await sw.WriteLineAsync("1");
            await sw.WriteLineAsync("100");
            await connectTask;

            Assert.AreEqual("MySkin", gotName);
            Assert.AreEqual("7", gotSkin);
            serverClient.Dispose();
        }
        finally
        {
            listener.Stop();
        }
    }

    // ==================== ReadLoop — JSON object routing ====================

    [TestMethod]
    public async Task NetworkController_ReadLoop_WallJson_FiresOnWallReceived()
    {
        var (listener, _) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            var tcs = new TaskCompletionSource<Wall>(TaskCreationOptions.RunContinuationsAsynchronously);
            nc.OnWallReceived += w => tcs.TrySetResult(w);

            var (serverClient, sw) = await DoHandshakeAsync(listener, nc, 1, 100);

            await sw.WriteLineAsync("{\"wall\":5,\"p1\":{\"x\":0,\"y\":0},\"p2\":{\"x\":100,\"y\":0}}");

            var wall = await tcs.Task.WaitAsync(_timeout);
            Assert.AreEqual(5, wall.Id);
            serverClient.Dispose();
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task NetworkController_ReadLoop_SnakeJson_FiresOnSnakeReceived()
    {
        var (listener, _) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            var tcs = new TaskCompletionSource<Snake>(TaskCreationOptions.RunContinuationsAsynchronously);
            nc.OnSnakeReceived += s => tcs.TrySetResult(s);

            var (serverClient, sw) = await DoHandshakeAsync(listener, nc, 1, 100);

            await sw.WriteLineAsync("{\"snake\":7,\"name\":\"Hero\",\"alive\":true}");

            var snake = await tcs.Task.WaitAsync(_timeout);
            Assert.AreEqual(7, snake.Id);
            Assert.AreEqual("Hero", snake.Name);
            serverClient.Dispose();
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task NetworkController_ReadLoop_PowerupJson_FiresOnPowerupReceived()
    {
        var (listener, _) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            var tcs = new TaskCompletionSource<Powerup>(TaskCreationOptions.RunContinuationsAsynchronously);
            nc.OnPowerupReceived += p => tcs.TrySetResult(p);

            var (serverClient, sw) = await DoHandshakeAsync(listener, nc, 1, 100);

            await sw.WriteLineAsync("{\"power\":9,\"loc\":{\"x\":50,\"y\":50},\"died\":false}");

            var powerup = await tcs.Task.WaitAsync(_timeout);
            Assert.AreEqual(9, powerup.Id);
            serverClient.Dispose();
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task NetworkController_ReadLoop_UnknownJson_IgnoresWithoutError()
    {
        var (listener, _) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            // If the loop crashes on unknown JSON, the subsequent wall will never arrive.
            var tcs = new TaskCompletionSource<Wall>(TaskCreationOptions.RunContinuationsAsynchronously);
            nc.OnWallReceived += w => tcs.TrySetResult(w);

            var (serverClient, sw) = await DoHandshakeAsync(listener, nc, 1, 100);

            await sw.WriteLineAsync("{\"unknownKey\":42}");
            await sw.WriteLineAsync("{\"wall\":1,\"p1\":{\"x\":0,\"y\":0},\"p2\":{\"x\":10,\"y\":0}}");

            var wall = await tcs.Task.WaitAsync(_timeout);
            Assert.AreEqual(1, wall.Id);
            serverClient.Dispose();
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task NetworkController_ReadLoop_MalformedJson_IgnoresWithoutError()
    {
        var (listener, _) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            var tcs = new TaskCompletionSource<Wall>(TaskCreationOptions.RunContinuationsAsynchronously);
            nc.OnWallReceived += w => tcs.TrySetResult(w);

            var (serverClient, sw) = await DoHandshakeAsync(listener, nc, 1, 100);

            // Malformed JSON followed by a valid wall to confirm loop survives.
            await sw.WriteLineAsync("not-valid-json{{{");
            await sw.WriteLineAsync("{\"wall\":2,\"p1\":{\"x\":0,\"y\":0},\"p2\":{\"x\":20,\"y\":0}}");

            var wall = await tcs.Task.WaitAsync(_timeout);
            Assert.AreEqual(2, wall.Id);
            serverClient.Dispose();
        }
        finally
        {
            listener.Stop();
        }
    }

    // ==================== ReadLoop — connection close ====================

    [TestMethod]
    public async Task NetworkController_ReadLoop_ServerCloses_FiresOnDisconnected()
    {
        var (listener, _) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            var disconnectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var (serverClient, _) = await DoHandshakeAsync(listener, nc, 1, 100);

            // Subscribe AFTER handshake so the Disconnect() inside ConnectAsync doesn't trigger it.
            nc.OnDisconnected += () => disconnectedTcs.TrySetResult();

            // Closing the server side simulates a connection drop.
            serverClient.Dispose();

            await disconnectedTcs.Task.WaitAsync(_timeout);
        }
        finally
        {
            listener.Stop();
        }
    }

    // ==================== SendMoveAsync ====================

    [TestMethod]
    public async Task NetworkController_SendMoveAsync_WhenConnected_ServerReceivesJson()
    {
        var (listener, _) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            var (serverClient, _) = await DoHandshakeAsync(listener, nc, 1, 100);
            var serverReader = new StreamReader(serverClient.GetStream(), Encoding.UTF8);

            await nc.SendMoveAsync("left");

            var line = await serverReader.ReadLineAsync().WaitAsync(_timeout);
            Assert.IsNotNull(line);
            Assert.Contains("left", line!);
            serverClient.Dispose();
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task NetworkController_SendMoveAsync_WriteException_FiresOnDisconnected()
    {
        // FailingWriter is a StreamWriter subclass whose WriteLineAsync always returns
        // a faulted Task.  It wraps Stream.Null so disposal-time flushes never throw.
        var (listener, _) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            var (serverClient, _) = await DoHandshakeAsync(listener, nc, 1, 100);

            _writerField.SetValue(nc, new FailingWriter());

            var disconnectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            nc.OnDisconnected += () => disconnectedTcs.TrySetResult();

            await nc.SendMoveAsync("right");

            await disconnectedTcs.Task.WaitAsync(_timeout);
            serverClient.Dispose();
        }
        finally
        {
            listener.Stop();
        }
    }

    // ==================== ReconnectAsync ====================

    [TestMethod]
    public async Task NetworkController_ReconnectAsync_CancelledBeforeFirstAttempt_Exits()
    {
        var (listener, _) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            var (serverClient, _) = await DoHandshakeAsync(listener, nc, 1, 100);
            serverClient.Dispose();
            nc.Disconnect();
            listener.Stop();

            // An already-cancelled token must cause immediate exit without touching the network.
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await nc.ReconnectAsync(cts.Token);
        }
        finally
        {
            if (listener.Server.IsBound) listener.Stop();
        }
    }

    [TestMethod]
    public async Task NetworkController_ReconnectAsync_FailsAndRetries_UntilCancelled()
    {
        // Connect first so prior host/port/name are recorded, then tear down the server.
        var (listener, _) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            var (serverClient, _) = await DoHandshakeAsync(listener, nc, 1, 100);
            serverClient.Dispose();
            nc.Disconnect();
            listener.Stop();

            // ReconnectAsync should enter the while-body, fail to connect (port closed),
            // then await Task.Delay which gets cancelled by the token after 200 ms.
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMilliseconds(200));

            bool threw = false;
            try
            {
                await nc.ReconnectAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }

            Assert.IsTrue(threw,
                "ReconnectAsync should propagate OperationCanceledException when the token fires during the retry delay");
        }
        finally
        {
            if (listener.Server.IsBound) listener.Stop();
        }
    }

    // ==================== Disconnect / Dispose edge cases ====================

    [TestMethod]
    public async Task NetworkController_Disconnect_WhileConnected_DoesNotDoubleFireOnDisconnected()
    {
        var (listener, _) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            var (serverClient, _) = await DoHandshakeAsync(listener, nc, 1, 100);

            // Subscribe AFTER the handshake so that the Disconnect() fired inside ConnectAsync
            // (before each new connection) does not inflate the counter.
            int fireCount = 0;
            nc.OnDisconnected += () => Interlocked.Increment(ref fireCount);

            nc.Disconnect();

            // Give the background read loop time to drain and attempt its own notification.
            await Task.Delay(150);

            Assert.AreEqual(1, fireCount,
                "OnDisconnected should fire exactly once (from Disconnect()) — the read-loop finalizer must not double-fire it.");
            serverClient.Dispose();
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestMethod]
    public void NetworkController_Disconnect_CalledTwice_DoesNotThrow()
    {
        using var nc = new NetworkController();
        nc.Disconnect();
        nc.Disconnect(); // second call — exercises the already-null / already-cancelled guard
    }

    [TestMethod]
    public void NetworkController_Dispose_CalledTwice_DoesNotThrow()
    {
        var nc = new NetworkController();
        nc.Dispose();
        nc.Dispose(); // second Dispose — must be idempotent
    }

    [TestMethod]
    public async Task NetworkController_Dispose_WhileConnected_DoesNotThrow()
    {
        var (listener, _) = StartFakeServer();
        var nc = new NetworkController();
        try
        {
            var (serverClient, _) = await DoHandshakeAsync(listener, nc, 1, 100);
            nc.Dispose();
            serverClient.Dispose();
        }
        finally
        {
            listener.Stop();
        }
    }

    // ==================== Handshake — non-numeric fallthrough ====================

    [TestMethod]
    public async Task NetworkController_ConnectAsync_NonNumericLinesDuringHandshake_HandshakeStillCompletes()
    {
        var (listener, port) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            var connectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            nc.OnConnected += () => connectedTcs.TrySetResult();

            var connectTask = nc.ConnectAsync("127.0.0.1", port, "Tester");

            var serverClient = await listener.AcceptTcpClientAsync();
            var sr = new StreamReader(serverClient.GetStream(), Encoding.UTF8);
            var sw = new StreamWriter(serverClient.GetStream(), Encoding.UTF8) { AutoFlush = true };

            // Consume the two hello lines the client sends.
            await sr.ReadLineAsync();
            await sr.ReadLineAsync();

            // Wait for ConnectAsync to return — TCP is up, hello lines sent, read loop running.
            await connectTask;

            // Non-numeric before player ID: exercises the false branch of the PlayerId TryParse.
            await sw.WriteLineAsync("not-a-number");
            // Real player ID.
            await sw.WriteLineAsync("42");
            // Non-numeric before world size: exercises the false branch of the WorldSize TryParse.
            await sw.WriteLineAsync("still-not-a-number");
            // Real world size.
            await sw.WriteLineAsync("200");

            await connectedTcs.Task.WaitAsync(_timeout);

            Assert.AreEqual(42, nc.PlayerId);
            Assert.AreEqual(200, nc.WorldSize);
            serverClient.Dispose();
        }
        finally
        {
            listener.Stop();
        }
    }

    // ==================== ReconnectAsync — success after retry ====================

    [TestMethod]
    [Timeout(8000, CooperativeCancellation = true)]
    public async Task NetworkController_ReconnectAsync_SucceedsOnSecondAttempt_CoversReturnAndDelay()
    {
        var (listener, port) = StartFakeServer();
        using var nc = new NetworkController();
        try
        {
            // Initial connect records host/port/name internally.
            var (serverClient, _) = await DoHandshakeAsync(listener, nc, 1, 100);
            serverClient.Dispose();
            nc.Disconnect();
            listener.Stop();

            // Background task: start a second listener on the same port ~100 ms from now.
            // The first ReconnectAsync attempt fires immediately and fails (port closed).
            // The backoff after attempt 1 is 1000 ms, so listener2 will be ready in time.
            var handshakeDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reconnectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            nc.OnConnected += () => reconnectedTcs.TrySetResult();
            _ = Task.Run(async () =>
            {
                await Task.Delay(100);
                var l2 = new TcpListener(IPAddress.Loopback, port);
                l2.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                l2.Start();
                try
                {
                    var c2 = await l2.AcceptTcpClientAsync();
                    var sr2 = new StreamReader(c2.GetStream(), Encoding.UTF8);
                    var sw2 = new StreamWriter(c2.GetStream(), Encoding.UTF8) { AutoFlush = true };
                    await sr2.ReadLineAsync(); // player name
                    await sr2.ReadLineAsync(); // skin index
                    await sw2.WriteLineAsync("2");   // player ID
                    await sw2.WriteLineAsync("200"); // world size
                    handshakeDone.TrySetResult();
                    await Task.Delay(500); // keep alive long enough for the read loop to run
                    c2.Dispose();
                }
                finally
                {
                    l2.Stop();
                }
            });

            // ReconnectAsync: first attempt fails (port closed), waits ~1000 ms,
            // second attempt succeeds — hitting `return;` and the catch-delay lines.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(7));
            await nc.ReconnectAsync(cts.Token);

            // ReconnectAsync returns as soon as ConnectAsync succeeds, but the read loop
            // finishes the handshake asynchronously — wait for OnConnected before asserting.
            await reconnectedTcs.Task.WaitAsync(_timeout);
            await handshakeDone.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(2, nc.PlayerId);
            Assert.AreEqual(200, nc.WorldSize);
        }
        finally
        {
            if (listener.Server.IsBound) listener.Stop();
        }
    }
}
