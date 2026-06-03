namespace ImageResizer.Api.Services;

public class ResizeWorkerService : BackgroundService
{
    private readonly ResizeJobQueue _queue;
    private readonly IBlobStorageService _blobs;
    private readonly ImageResizeService _resizer;
    private readonly ILogger<ResizeWorkerService> _logger;

    public ResizeWorkerService(
        ResizeJobQueue queue,
        IBlobStorageService blobs,
        ImageResizeService resizer,
        ILogger<ResizeWorkerService> logger)
    {
        _queue = queue;
        _blobs = blobs;
        _resizer = resizer;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            job.Status = JobStatus.Processing;
            try
            {
                foreach (var blobName in job.BlobNames)
                {
                    if (job.Percentage == 0)
                    {
                        await _blobs.DeleteOriginalAsync(blobName);
                    }
                    else if (job.Percentage == 100)
                    {
                        job.ResizedBlobNames.Add(blobName);
                    }
                    else
                    {
                        var (originalStream, _) = await _blobs.DownloadOriginalAsync(blobName);
                        var (resizedStream, contentType) = await _resizer.ResizeAsync(originalStream, job.Percentage);
                        var resizedName = await _blobs.UploadResizedAsync(resizedStream, blobName, contentType);
                        job.ResizedBlobNames.Add(resizedName);
                        await _blobs.DeleteOriginalAsync(blobName);
                    }
                }
                job.Status = JobStatus.Done;
            }
            catch (Exception ex)
            {
                job.Status = JobStatus.Failed;
                job.Error = ex.Message;
                _logger.LogError(ex, "Resize job {JobId} failed.", job.JobId);
            }
        }
    }
}
