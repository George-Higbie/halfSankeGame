// <copyright file="NetworkController.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GUI.Components.Models;

namespace GUI.Components.Controllers
{
    /// <summary>
    /// Handles the TCP connection to the Snake server, parsing the line-delimited
    /// protocol (player ID, world size, then JSON objects) and raising typed events.
    /// </summary>
    public class NetworkController : IDisposable
    {
        private TcpClient? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cts;

        /// <summary>The player ID received during the handshake.</summary>
        public int? PlayerId { get; private set; }

        /// <summary>The world size received during the handshake.</summary>
        public int? WorldSize { get; private set; }

        private string? _host;
        private int _port;
        private string? _playerName;

        /// <summary>Raised when the server sends the player ID.</summary>
        public event Action<int>? OnPlayerIdReceived;

        /// <summary>Raised when the server sends the world size.</summary>
        public event Action<int>? OnWorldSizeReceived;

        /// <summary>Raised for each wall object received.</summary>
        public event Action<Wall>? OnWallReceived;

        /// <summary>Raised for each snake object received.</summary>
        public event Action<Snake>? OnSnakeReceived;

        /// <summary>Raised for each powerup object received.</summary>
        public event Action<Powerup>? OnPowerupReceived;

        /// <summary>Raised when the handshake (player ID + world size) completes.</summary>
        public event Action? OnConnected;

        /// <summary>Raised when the connection is lost or closed.</summary>
        public event Action? OnDisconnected;

        /// <summary>Whether the underlying TCP client is currently connected.</summary>
        public bool IsConnected => _client?.Connected ?? false;

        /// <summary>Initializes a new instance of the <see cref="NetworkController"/> class.</summary>
        public NetworkController() { }

        /// <summary>Connects to the server, sends the player name and skin index, and starts the read loop.</summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="host"/> or <paramref name="playerName"/> is null.</exception>
        public async Task ConnectAsync(string host, int port, string playerName, int skinIndex = 0, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(playerName);
            Disconnect();

            PlayerId = null;
            WorldSize = null;
            _host = host;
            _port = port;
            _playerName = playerName;

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _client = new TcpClient();
            await _client.ConnectAsync(host, port);
            var stream = _client.GetStream();
            _reader = new StreamReader(stream, new UTF8Encoding(false));
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };

            await _writer.WriteAsync($"{playerName}\n{skinIndex}\n");

            _ = Task.Run(() => ReadLoopAsync(_cts.Token));
        }

        /// <summary>Sends a JSON movement command to the server.</summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="moving"/> is null.</exception>
        public async Task SendMoveAsync(string moving)
        {
            ArgumentNullException.ThrowIfNull(moving);
            if (_writer == null) return;
            var cmd = JsonSerializer.Serialize(new { moving = moving });
            try
            {
                await _writer.WriteLineAsync(cmd);
            }
            catch (IOException)
            {
                OnDisconnected?.Invoke();
            }
            catch (ObjectDisposedException)
            {
                OnDisconnected?.Invoke();
            }
        }

        /// <summary>Disconnects and clears connection state.</summary>
        public void Disconnect()
        {
            CancelTokenSource();
            DisposeConnectionResources();

            _client = null;
            _reader = null;
            _writer = null;

            PlayerId = null;
            WorldSize = null;
            NotifyDisconnected();
        }

        /// <summary>Releases all resources held by this controller.</summary>
        public void Dispose()
        {
            CancelTokenSource();
            DisposeConnectionResources();
        }

        private void NotifyDisconnected()
        {
            CancelTokenSource();
            OnDisconnected?.Invoke();
        }

        /// <summary>Cancels the token source if it has not already been cancelled.</summary>
        private void CancelTokenSource()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                try { _cts.Cancel(); }
                catch (ObjectDisposedException) { /* already disposed — safe to ignore */ }
            }
        }

        /// <summary>Disposes the reader, writer, and TCP client, tolerating already-disposed resources.</summary>
        private void DisposeConnectionResources()
        {
            try { _reader?.Dispose(); }
            catch (ObjectDisposedException) { /* already disposed — safe to ignore */ }

            try { _writer?.Dispose(); }
            catch (ObjectDisposedException) { /* already disposed — safe to ignore */ }

            try { _client?.Close(); }
            catch (ObjectDisposedException) { /* already disposed — safe to ignore */ }

            try { _client?.Dispose(); }
            catch (ObjectDisposedException) { /* already disposed — safe to ignore */ }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var line = await _reader!.ReadLineAsync();
                    if (line == null) break; // remote closed

                    // initial two numeric lines: player id and world size
                    if (!PlayerId.HasValue)
                    {
                        if (int.TryParse(line.Trim(), out var pid))
                        {
                            PlayerId = pid;
                            OnPlayerIdReceived?.Invoke(pid);
                            continue;
                        }
                    }

                    if (PlayerId.HasValue && !WorldSize.HasValue)
                    {
                        if (int.TryParse(line.Trim(), out var ws))
                        {
                            WorldSize = ws;
                            OnWorldSizeReceived?.Invoke(ws);
                            // if we have both id and world size, notify connected
                            if (PlayerId.HasValue && WorldSize.HasValue)
                                OnConnected?.Invoke();
                            continue;
                        }
                    }

                    // otherwise JSON lines for walls, snakes, powerups
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("wall", out _))
                        {
                            var w = root.Deserialize<Wall>();
                            if (w != null) OnWallReceived?.Invoke(w);
                        }
                        else if (root.TryGetProperty("snake", out _))
                        {
                            var s = root.Deserialize<Snake>();
                            if (s != null) OnSnakeReceived?.Invoke(s);
                        }
                        else if (root.TryGetProperty("power", out _))
                        {
                            var p = root.Deserialize<Powerup>();
                            if (p != null) OnPowerupReceived?.Invoke(p);
                        }
                        else
                        {
                            // unknown object; ignore for now
                        }
                    }
                    catch (JsonException)
                    {
                        // ignore malformed JSON
                    }
                }
            }
            catch (IOException)
            {
                // connection lost or stream closed
            }
            catch (SocketException)
            {
                // network-level failure
            }
            catch (OperationCanceledException)
            {
                // cancellation token fired
            }
            finally
            {
                // We only invoke disconnect if we haven't already been explicitly cancelled/disconnected
                if (!ct.IsCancellationRequested)
                {
                    NotifyDisconnected();
                }
            }
        }

        /// <summary>
        /// Attempt to reconnect using the last-known host/port/name with exponential backoff.
        /// </summary>
        public async Task ReconnectAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_host) || _port == 0 || string.IsNullOrEmpty(_playerName))
                throw new InvalidOperationException("No prior connection info to reconnect");

            int attempt = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ConnectAsync(_host!, _port, _playerName!, ct: ct);
                    return;
                }
                catch (IOException)
                {
                    attempt++;
                    var delay = Math.Min(8000, 500 * (1 << Math.Min(attempt, 6)));
                    await Task.Delay(delay, ct);
                }
                catch (SocketException)
                {
                    attempt++;
                    var delay = Math.Min(8000, 500 * (1 << Math.Min(attempt, 6)));
                    await Task.Delay(delay, ct);
                }
            }
        }
    }
}


