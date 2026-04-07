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
    // Minimal network controller that connects to the server, reads line-delimited JSON
    // and raises events for the GameController to consume.
    public class NetworkController : IDisposable
    {
        private TcpClient? _client;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cts;

        public int? PlayerId { get; private set; }
        public int? WorldSize { get; private set; }
        private string? _host;
        private int _port;
        private string? _playerName;

        public event Action<int>? OnPlayerIdReceived;
        public event Action<int>? OnWorldSizeReceived;
        public event Action<Wall>? OnWallReceived;
        public event Action<Snake>? OnSnakeReceived;
        public event Action<Powerup>? OnPowerupReceived;
        public event Action? OnConnected;

        public bool IsConnected => _client?.Connected ?? false;

        public NetworkController() { }

        public async Task ConnectAsync(string host, int port, string playerName, CancellationToken ct = default)
        {
            _host = host;
            _port = port;
            _playerName = playerName;

            _cts?.Cancel();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _client = new TcpClient();
            await _client.ConnectAsync(host, port);
            var stream = _client.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8);
            _writer = new StreamWriter(stream, Encoding.UTF8) { NewLine = "\n", AutoFlush = true };

            // send player name
            await _writer.WriteLineAsync(playerName);

            // start read loop
            _ = Task.Run(() => ReadLoopAsync(_cts.Token));
        }

        public async Task SendMoveAsync(string moving)
        {
            if (_writer == null) return;
            var cmd = JsonSerializer.Serialize(new { moving = moving });
            try
            {
                await _writer.WriteLineAsync(cmd);
            }
            catch (Exception)
            {
                // on send failure, signal disconnect
                OnDisconnected?.Invoke();
            }
        }

        public event Action? OnDisconnected;

        private void NotifyDisconnected()
        {
            try { _cts?.Cancel(); } catch { }
            OnDisconnected?.Invoke();
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
                            var w = JsonSerializer.Deserialize<Wall>(line);
                            if (w != null) OnWallReceived?.Invoke(w);
                        }
                        else if (root.TryGetProperty("snake", out _))
                        {
                            var s = JsonSerializer.Deserialize<Snake>(line);
                            if (s != null) OnSnakeReceived?.Invoke(s);
                        }
                        else if (root.TryGetProperty("power", out _))
                        {
                            var p = JsonSerializer.Deserialize<Powerup>(line);
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
            catch (Exception)
            {
                // read loop ended unexpectedly
            }
            finally
            {
                // notify disconnect to allow reconnection logic elsewhere
                NotifyDisconnected();
                Dispose();
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
                    await ConnectAsync(_host!, _port, _playerName!, ct);
                    return;
                }
                catch
                {
                    attempt++;
                    var delay = Math.Min(8000, 500 * (1 << Math.Min(attempt, 6)));
                    await Task.Delay(delay, ct);
                }
            }
        }

        public void Disconnect()
        {
            try { _cts?.Cancel(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }
            PlayerId = null;
            WorldSize = null;
            NotifyDisconnected();
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }
        }
    }
}


