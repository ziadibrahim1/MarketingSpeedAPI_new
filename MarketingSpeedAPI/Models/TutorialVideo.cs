namespace MarketingSpeedAPI.Models
{
    public class VideoCategory
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string NameEN { get; set; } = null!;
        public string? Description { get; set; }
        public string? DescriptionEN { get; set; }
        public ICollection<TutorialVideo>? Videos { get; set; }
        public int? Index { get; set; }
    }

    public class TutorialVideo
    {
        public int Id { get; set; }
        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string VideoType { get; set; } = "file"; // "youtube" أو "file"
        public string? VideoUrl { get; set; }
        public string? FilePath { get; set; }
        public int? Duration { get; set; }
        public string Language { get; set; } = "ar";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int? CategoryId { get; set; }
        public bool IsActive { get; set; } = true;

        public VideoCategory? Category { get; set; }
    }

}
