using System.ComponentModel.DataAnnotations;

public class MediaEntries
{
    [System.Text.Json.Serialization.JsonPropertyName("RECDATA")]
    public List<MediaRecdata>? RecEntries { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("METADATA")]
    public MediaMetadata? Meta { get; set; }
}
public class MediaRecdata
{
    [Key]
    public String? EntityMediaID { get; set; }
    public String? MediaType { get; set; }
    public String? EntityID { get; set; }
    public String? EntityType { get; set; }
    public String? Title { get; set; }
    public String? Subtitle { get; set; }
    public String? Description { get; set; }
    public String? EmbedCode { get; set; }
    public Int32? Height { get; set; }
    public Int32? Width { get; set; }
    public String? URL { get; set; }
    public String? Credits { get; set; }
    public bool? IsPrimary { get; set; }
    public bool? IsPreview { get; set; }
    public bool? IsGallery { get; set; }
}
public class MediaMetadata
{
    public String? SOURCE { get; set; }
    public String? LASTUPDATED { get; set; }
}