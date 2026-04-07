using System.Text.Json.Serialization;

namespace GUI.Components.Models
{
    public class Wall
    {
        [JsonPropertyName("wall")]
        public int wall { get; set; }

        [JsonPropertyName("p1")]
        public Point2D? p1 { get; set; }

        [JsonPropertyName("p2")]
        public Point2D? p2 { get; set; }

        public Wall() { }
    }
}

