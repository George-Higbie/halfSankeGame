using System.Text.Json.Serialization;

namespace GUI.Components.Models
{
    /// <summary>
    /// Represents a 2D coordinate or direction vector in the game world.
    /// </summary>
    public class Point2D
    {
        [JsonPropertyName("X")]
        public int X { get; set; }

        [JsonPropertyName("Y")]
        public int Y { get; set; }

        public Point2D() { }
        public Point2D(int x, int y) { X = x; Y = y; }
    }
}

