namespace ImageResizer.Api.Services;

// Background service that continuously processes resize jobs from the queue. 
// It uses parallel processing to handle multiple jobs concurrently, 
// with a degree of parallelism equal to the number of processor cores. 
// Each job is processed by downloading the original blobs, resizing them using the ImageResizeService, 
// uploading the resized images, and then deleting the original blobs. 
// The job status is updated accordingly, and any errors are logged.
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

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Parallel.ForEachAsync(
            _queue.Reader.ReadAllAsync(stoppingToken),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = stoppingToken,
            },
            ProcessJobAsync);

    private async ValueTask ProcessJobAsync(ResizeJob job, CancellationToken ct)
    {
        job.Status = JobStatus.Processing;
        try
        {
            foreach (var blobName in job.BlobNames)
            {
                // If percentage is 0, we don't do any resize and skip the upload of a resized version, 
                // but we still delete the original blob to clean up.
                if (job.Percentage == 0)
                {
                    await _blobs.DeleteOriginalAsync(blobName);
                }
                // If percentage is 100, we just return the original blob as the resized version without doing any processing,
                // but we still add it to the list of resized blob names for the client to download, 
                // and we don't delete the original blob since it's also the resized version in this case.
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
