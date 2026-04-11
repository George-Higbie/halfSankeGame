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
        private ConcurrentDictionary<int, Snake> _snakes = new();
        private ConcurrentDictionary<int, Wall> _walls = new();
        private ConcurrentDictionary<int, Powerup> _powerups = new();

        private bool _isInitializing = false;
        private ConcurrentDictionary<int, Wall> _pendingWalls = new();
        private int? _pendingPlayerId = null;
        private int? _pendingWorldSize = null;

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
            _network.OnPlayerIdReceived += id => { 
                if (_isInitializing) _pendingPlayerId = id; 
                else PlayerId = id; 
                OnStateUpdated?.Invoke(); 
            };
            _network.OnWorldSizeReceived += ws => { 
                if (_isInitializing) _pendingWorldSize = ws; 
                else WorldSize = ws; 
                OnStateUpdated?.Invoke(); 
            };
            _network.OnWallReceived += w => { 
                if (_isInitializing) _pendingWalls[w.wall] = w;
                else _walls[w.wall] = w;
                OnStateUpdated?.Invoke(); 
            };
            _network.OnSnakeReceived += s => { 
                if (_isInitializing) {
                    _isInitializing = false;
                    if (_pendingPlayerId.HasValue) PlayerId = _pendingPlayerId;
                    if (_pendingWorldSize.HasValue) WorldSize = _pendingWorldSize;
                    _walls = _pendingWalls;
                    _snakes = new();
                    _powerups = new();
                }
                
                if (s.dc.HasValue && s.dc.Value && _snakes.ContainsKey(s.snake)) {
                    _snakes.TryRemove(s.snake, out _);
                } else {
                    // Merge fields so we don't nullify body on death packets
                    if (_snakes.TryGetValue(s.snake, out var existing)) {
                        if (s.score != 0) existing.score = s.score;
                        if (s.died.HasValue) existing.died = s.died;
                        if (s.alive.HasValue) existing.alive = s.alive;
                        if (s.join.HasValue) existing.join = s.join;
                        if (s.dc.HasValue) existing.dc = s.dc;
                        if (s.name != null) existing.name = s.name;
                        if (s.body != null) existing.body = s.body;
                        if (s.dir != null) existing.dir = s.dir;
                    } else {
                        // For a new snake, default omitted booleans to false
                        s.died ??= false;
                        s.alive ??= false;
                        s.join ??= false;
                        s.dc ??= false;
                        _snakes[s.snake] = s; 
                    }
                }
                OnStateUpdated?.Invoke(); 
            };
            _network.OnPowerupReceived += p => { 
                if (_isInitializing) {
                    _isInitializing = false;
                    if (_pendingPlayerId.HasValue) PlayerId = _pendingPlayerId;
                    if (_pendingWorldSize.HasValue) WorldSize = _pendingWorldSize;
                    _walls = _pendingWalls;
                    _snakes = new();
                    _powerups = new();
                }

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
            _isInitializing = true;
            _pendingPlayerId = null;
            _pendingWorldSize = null;
            _pendingWalls = new();
            return _network.ConnectAsync(host, port, name);
        }

        public void Disconnect()
        {
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


