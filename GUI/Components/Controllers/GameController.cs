using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GUI.Components.Models;

namespace GUI.Components.Controllers
{
    public class GameController
    {
        private readonly ConcurrentDictionary<int, Snake> _snakes = new();
        private readonly ConcurrentDictionary<int, Wall> _walls = new();
        private readonly ConcurrentDictionary<int, Powerup> _powerups = new();

        public int? PlayerId { get; private set; }
        public int? WorldSize { get; private set; }

        // Event raised when the state is updated; consumers should call InvokeAsync / StateHasChanged
        public event Action? OnStateUpdated;

        private readonly NetworkController _network;

        private DateTime _lastSend = DateTime.MinValue;

        public GameController(NetworkController network)
        {
            _network = network;
            // subscribe
            _network.OnPlayerIdReceived += id => { PlayerId = id; OnStateUpdated?.Invoke(); };
            _network.OnWorldSizeReceived += ws => { WorldSize = ws; OnStateUpdated?.Invoke(); };
            _network.OnWallReceived += w => { _walls[w.wall] = w; OnStateUpdated?.Invoke(); };
            _network.OnSnakeReceived += s => { 
                if (s.dc && _snakes.ContainsKey(s.snake)) {
                    _snakes.TryRemove(s.snake, out _);
                } else {
                    // Merge fields so we don't nullify body on death packets
                    if (_snakes.TryGetValue(s.snake, out var existing)) {
                        existing.score = s.score;
                        existing.died = s.died;
                        existing.alive = s.alive;
                        existing.join = s.join;
                        existing.dc = s.dc;
                        if (s.name != null) existing.name = s.name;
                        if (s.body != null) existing.body = s.body;
                        if (s.dir != null) existing.dir = s.dir;
                    } else {
                        _snakes[s.snake] = s; 
                    }
                }
                OnStateUpdated?.Invoke(); 
            };
            _network.OnPowerupReceived += p => { 
                if (p.died && _powerups.ContainsKey(p.power)) {
                    _powerups.TryRemove(p.power, out _);
                } else {
                    _powerups[p.power] = p; 
                }
                OnStateUpdated?.Invoke(); 
            };
            _network.OnDisconnected += () => { OnStateUpdated?.Invoke(); };
        }

        public IReadOnlyCollection<Snake> GetSnakes() => _snakes.Values.ToList().AsReadOnly();
        public IReadOnlyCollection<Wall> GetWalls() => _walls.Values.ToList().AsReadOnly();
        public IReadOnlyCollection<Powerup> GetPowerups() => _powerups.Values.ToList().AsReadOnly();

        public Snake? GetPlayerSnake()
        {
            if (!PlayerId.HasValue) return null;
            _snakes.TryGetValue(PlayerId.Value, out var s);
            return s;
        }

        public Task ConnectAsync(string host, int port, string name)
        {
            _snakes.Clear();
            _walls.Clear();
            _powerups.Clear();
            PlayerId = null;
            WorldSize = null;
            return _network.ConnectAsync(host, port, name);
        }

        public void Disconnect()
        {
            PlayerId = null;
            WorldSize = null;
            _snakes.Clear();
            _walls.Clear();
            _powerups.Clear();
            _network.Disconnect();
        }

        public Task SendMoveAsync(string moving)
        {
            // ensure handshake completed
            if (!PlayerId.HasValue || !WorldSize.HasValue) return Task.CompletedTask;

            // throttle -> at most one send per ~15ms (avoid flooding)
            var now = DateTime.UtcNow;
            if ((now - _lastSend).TotalMilliseconds < 15) return Task.CompletedTask;
            _lastSend = now;
            return _network.SendMoveAsync(moving);
        }
    }
}


