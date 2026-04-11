using System.Text.Json.Serialization;

namespace GUI.Components.Models
{
    /// <summary>
    /// Represents a collectible powerup at a location in the game world.
    /// </summary>
    public class Powerup
    {
        [JsonPropertyName("power")]
        public int Id { get; set; }

        [JsonPropertyName("loc")]
        public Point2D? Location { get; set; }

        [JsonPropertyName("died")]
        public bool Died { get; set; }
    }
}

