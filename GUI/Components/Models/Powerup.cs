using System.Text.Json.Serialization;

namespace GUI.Components.Models
{
    public class Powerup
    {
        [JsonPropertyName("power")]
        public int power { get; set; }

        [JsonPropertyName("loc")]
        public Point2D? loc { get; set; }

        [JsonPropertyName("died")]
        public bool died { get; set; }

        public Powerup() { }
    }
}

