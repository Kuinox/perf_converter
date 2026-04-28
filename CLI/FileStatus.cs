namespace CLI;

public class FileStatus
{
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long EntryCount { get; set; }
    public long BufferedCount { get; set; }
    public long FlushedCount { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public DateTime? LastActivity { get; set; }
}
