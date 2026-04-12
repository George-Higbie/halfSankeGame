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
        [JsonPropertyName("snake")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("body")]
        public List<Point2D>? Body { get; set; }

        [JsonPropertyName("dir")]
        public Point2D? Direction { get; set; }

        [JsonPropertyName("score")]
        public int? Score { get; set; }

        [JsonPropertyName("died")]
        public bool? Died { get; set; }

        [JsonPropertyName("alive")]
        public bool? Alive { get; set; }

        [JsonPropertyName("dc")]
        public bool? Disconnected { get; set; }

        [JsonPropertyName("join")]
        public bool? Joined { get; set; }

        [JsonPropertyName("skin")]
        public int? Skin { get; set; }

        /// <summary>
        /// Returns a deep copy to prevent cross-thread mutation during rendering.
        /// </summary>
        public Snake Clone() => new()
        {
            Id = Id,
            Name = Name,
            Body = Body?.ToList(),
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

