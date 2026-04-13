// <copyright file="Snake.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace GUI.Components.Models
{
    /// <summary>
    /// Represents a snake in the game world, including its body segments,
    /// direction, score, and lifecycle state.
    /// </summary>
    public class Snake
    {
        /// <summary>Server-assigned unique snake identifier.</summary>
        [JsonPropertyName("snake")]
        public int Id { get; set; }

        /// <summary>Player-chosen display name.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Ordered list of body segment vertices (tail first, head last).</summary>
        [JsonPropertyName("body")]
        public List<Point2D>? Body { get; set; }

        /// <summary>Current movement direction vector.</summary>
        [JsonPropertyName("dir")]
        public Point2D? Direction { get; set; }

        /// <summary>Number of powerups collected.</summary>
        [JsonPropertyName("score")]
        public int? Score { get; set; }

        /// <summary>Whether the snake died this frame.</summary>
        [JsonPropertyName("died")]
        public bool? Died { get; set; }

        /// <summary>Whether the snake is currently alive.</summary>
        [JsonPropertyName("alive")]
        public bool? Alive { get; set; }

        /// <summary>Whether the player has disconnected.</summary>
        [JsonPropertyName("dc")]
        public bool? Disconnected { get; set; }

        /// <summary>Whether the player just joined this frame.</summary>
        [JsonPropertyName("join")]
        public bool? Joined { get; set; }

        /// <summary>Index into <see cref="SnakeSkin.AllSkins"/> for the snake's visual appearance.</summary>
        [JsonPropertyName("skin")]
        public int? Skin { get; set; }

        /// <summary>
        /// Returns a deep copy to prevent cross-thread mutation during rendering.
        /// </summary>
        public Snake Clone() => new()
        {
            Id = Id,
            Name = Name,
            Body = Body?.Select(p => new Point2D(p.X, p.Y)).ToList(),
            Direction = Direction != null ? new Point2D(Direction.X, Direction.Y) : null,
            Score = Score,
            Died = Died,
            Alive = Alive,
            Disconnected = Disconnected,
            Joined = Joined,
            Skin = Skin
        };
    }
}

