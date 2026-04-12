// <copyright file="Wall.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using System.Text.Json.Serialization;

namespace GUI.Components.Models
{
    /// <summary>
    /// Represents a wall segment defined by two endpoints in the game world.
    /// Walls are axis-aligned: either <see cref="Point1"/> and <see cref="Point2"/>
    /// share an X coordinate (vertical) or a Y coordinate (horizontal).
    /// </summary>
    public class Wall
    {
        /// <summary>Server-assigned unique wall identifier.</summary>
        [JsonPropertyName("wall")]
        public int Id { get; set; }

        /// <summary>First endpoint of the wall segment.</summary>
        [JsonPropertyName("p1")]
        public Point2D? Point1 { get; set; }

        /// <summary>Second endpoint of the wall segment.</summary>
        [JsonPropertyName("p2")]
        public Point2D? Point2 { get; set; }
    }
}

