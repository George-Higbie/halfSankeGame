// <copyright file="Powerup.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using System.Text.Json.Serialization;

namespace GUI.Components.Models
{
    /// <summary>
    /// Represents a collectible powerup at a location in the game world.
    /// When consumed by a snake the server marks it as died.
    /// </summary>
    public class Powerup
    {
        /// <summary>Server-assigned unique powerup identifier.</summary>
        [JsonPropertyName("power")]
        public int Id { get; set; }

        /// <summary>World-space location of the powerup, or null if unknown.</summary>
        [JsonPropertyName("loc")]
        public Point2D? Location { get; set; }

        /// <summary>Whether this powerup has been consumed or removed.</summary>
        [JsonPropertyName("died")]
        public bool Died { get; set; }
    }
}

