using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GUI.Components.Models
{
    public class Snake
    {
        [JsonPropertyName("snake")]
        public int snake { get; set; }

        [JsonPropertyName("name")]
        public string? name { get; set; }

        [JsonPropertyName("body")]
        public List<Point2D>? body { get; set; }

        [JsonPropertyName("dir")]
        public Point2D? dir { get; set; }

        [JsonPropertyName("score")]
        public int score { get; set; }

        [JsonPropertyName("died")]
        public bool died { get; set; }

        [JsonPropertyName("alive")]
        public bool alive { get; set; }

        [JsonPropertyName("dc")]
        public bool dc { get; set; }

        [JsonPropertyName("join")]
        public bool join { get; set; }

        public Snake() { }
    }
}

