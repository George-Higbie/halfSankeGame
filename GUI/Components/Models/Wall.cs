using System.Text.Json.Serialization;

namespace GUI.Components.Models
{
    /// <summary>
    /// Represents a wall segment defined by two endpoints in the game world.
    /// </summary>
    public class Wall
    {
        [JsonPropertyName("wall")]
        public int Id { get; set; }

        [JsonPropertyName("p1")]
        public Point2D? Point1 { get; set; }

        [JsonPropertyName("p2")]
        public Point2D? Point2 { get; set; }
    }
}

