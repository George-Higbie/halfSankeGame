// <copyright file="Point2D.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using System.Text.Json.Serialization;

namespace GUI.Components.Models
{
    /// <summary>
    /// Represents a 2D coordinate or direction vector in the game world.
    /// Used for positions, directions, and body segments.
    /// </summary>
    public class Point2D
    {
        /// <summary>Horizontal coordinate (positive = right).</summary>
        [JsonPropertyName("X")]
        public int X { get; set; }

        /// <summary>Vertical coordinate (positive = down).</summary>
        [JsonPropertyName("Y")]
        public int Y { get; set; }

        /// <summary>Initializes a new <see cref="Point2D"/> at the origin (0, 0).</summary>
        public Point2D() { }

        /// <summary>Initializes a new <see cref="Point2D"/> at the given coordinates.</summary>
        /// <param name="x">The horizontal coordinate.</param>
        /// <param name="y">The vertical coordinate.</param>
        public Point2D(int x, int y) { X = x; Y = y; }
    }
}

