namespace ImageResizer.Api.Services;

public enum JobStatus { Queued, Processing, Done, Failed }

public class ResizeJob
{
    public string JobId { get; } = Guid.NewGuid().ToString();
    public required List<string> BlobNames { get; init; }
    public required float Percentage { get; init; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public List<string> ResizedBlobNames { get; } = [];
    public string? Error { get; set; }
}
