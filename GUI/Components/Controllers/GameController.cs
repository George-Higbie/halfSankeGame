// <copyright file="GameController.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GUI.Components.Models;

namespace GUI.Components.Controllers
{
    /// <summary>
    /// Manages the game state by processing events from the network controller
    /// and providing thread-safe snapshots to the rendering layer.
    /// </summary>
    public class GameController
    {
        private ConcurrentDictionary<int, Snake> _snakes = new();
        private ConcurrentDictionary<int, Wall> _walls = new();
        private ConcurrentDictionary<int, Powerup> _powerups = new();

        private bool _isInitializing;
        private ConcurrentDictionary<int, Wall> _pendingWalls = new();
        private int? _pendingPlayerId;
        private int? _pendingWorldSize;

        /// <summary>The server-assigned player ID for this client.</summary>
        public int? PlayerId { get; private set; }

        /// <summary>The world size received from the server.</summary>
        public int? WorldSize { get; private set; }

        /// <summary>Raised when game state changes; consumers should call StateHasChanged.</summary>
        public event Action? OnStateUpdated;

        private readonly NetworkController _network;

        /// <summary>
        /// Initializes the game controller and subscribes to network events.
        /// </summary>
        /// <param name="network">The network controller to subscribe to. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="network"/> is null.</exception>
        public GameController(NetworkController network)
        {
            ArgumentNullException.ThrowIfNull(network);

            _network = network;
            _network.OnPlayerIdReceived += HandlePlayerIdReceived;
            _network.OnWorldSizeReceived += HandleWorldSizeReceived;
            _network.OnWallReceived += HandleWallReceived;
            _network.OnSnakeReceived += HandleSnakeReceived;
            _network.OnPowerupReceived += HandlePowerupReceived;
            _network.OnDisconnected += () => OnStateUpdated?.Invoke();
        }

        /// <summary>Returns a thread-safe deep-copied snapshot of all snakes.</summary>
        public IReadOnlyList<Snake> GetSnakes() =>
            _snakes.Values.Select(s => s.Clone()).ToList().AsReadOnly();

        /// <summary>Returns a thread-safe snapshot of all walls.</summary>
        public IReadOnlyCollection<Wall> GetWalls() =>
            _walls.Values.ToList().AsReadOnly();

        /// <summary>Returns a thread-safe snapshot of all powerups.</summary>
        public IReadOnlyCollection<Powerup> GetPowerups() =>
            _powerups.Values.ToList().AsReadOnly();

        /// <summary>Fires when the current player dies. Raised once per death event.</summary>
        public event Action? OnPlayerDied;

        /// <summary>Returns a deep copy of the current player's snake, or null.</summary>
        public Snake? GetPlayerSnake()
        {
            if (!PlayerId.HasValue) return null;
            return _snakes.TryGetValue(PlayerId.Value, out var s) ? s.Clone() : null;
        }

        /// <summary>Connects to the game server and enters the initialization phase.</summary>
        public async Task ConnectAsync(string host, int port, string name, int skinIndex = 0)
        {
            _isInitializing = true;
            _pendingPlayerId = null;
            _pendingWorldSize = null;
            _pendingWalls = new();
            try
            {
                await _network.ConnectAsync(host, port, name, skinIndex);
            }
            catch
            {
                _isInitializing = false;
                throw;
            }
        }

        /// <summary>Disconnects from the server and clears all local game state.</summary>
        public void Disconnect()
        {
            _network.Disconnect();
            _isInitializing = false;
            PlayerId = null;
            WorldSize = null;
            _snakes.Clear();
            _powerups.Clear();
            _walls.Clear();
            _pendingWalls.Clear();
            OnStateUpdated?.Invoke();
        }

        /// <summary>Sends a movement command to the server if the handshake is complete.</summary>
        public Task SendMoveAsync(string moving)
        {
            if (!PlayerId.HasValue || !WorldSize.HasValue) return Task.CompletedTask;
            return _network.SendMoveAsync(moving);
        }

        // ==================== Private Helpers ====================

        /// <summary>
        /// Commits pending initialization data and resets collections for the new session.
        /// Called once when the first snake or powerup arrives after connecting.
        /// </summary>
        private void CommitPendingStateIfInitializing()
        {
            if (!_isInitializing) return;
            _isInitializing = false;
            if (_pendingPlayerId.HasValue) PlayerId = _pendingPlayerId;
            if (_pendingWorldSize.HasValue) WorldSize = _pendingWorldSize;
            _walls = _pendingWalls;
            _snakes = new();
            _powerups = new();
        }

        private void HandlePlayerIdReceived(int id)
        {
            if (_isInitializing) _pendingPlayerId = id;
            else PlayerId = id;
            OnStateUpdated?.Invoke();
        }

        private void HandleWorldSizeReceived(int ws)
        {
            if (_isInitializing) _pendingWorldSize = ws;
            else WorldSize = ws;
            OnStateUpdated?.Invoke();
        }

        private void HandleWallReceived(Wall w)
        {
            if (_isInitializing) _pendingWalls[w.Id] = w;
            else _walls[w.Id] = w;
            OnStateUpdated?.Invoke();
        }

        private void HandleSnakeReceived(Snake s)
        {
            CommitPendingStateIfInitializing();

            if (s.Disconnected.HasValue && s.Disconnected.Value && _snakes.ContainsKey(s.Id))
            {
                _snakes.TryRemove(s.Id, out _);
            }
            else if (_snakes.TryGetValue(s.Id, out var existing))
            {
                var wasAlive = existing.Died != true;
                MergeSnakeFields(existing, s);
                if (existing.Died == true && wasAlive && PlayerId.HasValue && s.Id == PlayerId.Value)
                    OnPlayerDied?.Invoke();
            }
            else
            {
                s.Score ??= 0;
                s.Died ??= false;
                s.Alive ??= false;
                s.Joined ??= false;
                s.Disconnected ??= false;
                _snakes[s.Id] = s;
            }

            OnStateUpdated?.Invoke();
        }

        private void HandlePowerupReceived(Powerup p)
        {
            CommitPendingStateIfInitializing();

            if (p.Died && _powerups.ContainsKey(p.Id))
                _powerups.TryRemove(p.Id, out _);
            else
                _powerups[p.Id] = p;

            OnStateUpdated?.Invoke();
        }

        /// <summary>
        /// Merges non-null fields from an incoming update into the existing snake record.
        /// </summary>
        private static void MergeSnakeFields(Snake existing, Snake update)
        {
            if (update.Score.HasValue) existing.Score = update.Score;
            if (update.Died.HasValue)
            {
                existing.Died = update.Died;
                if (update.Died.Value) existing.Alive = false;
            }
            if (update.Alive.HasValue)
            {
                existing.Alive = update.Alive;
                if (update.Alive.Value) existing.Died = false;
            }
            if (update.Joined.HasValue) existing.Joined = update.Joined;
            if (update.Disconnected.HasValue) existing.Disconnected = update.Disconnected;
            if (update.Name != null) existing.Name = update.Name;
            if (update.Body != null) existing.Body = update.Body;
            if (update.Direction != null) existing.Direction = update.Direction;
            if (update.Skin.HasValue) existing.Skin = update.Skin;
        }
    }
}


