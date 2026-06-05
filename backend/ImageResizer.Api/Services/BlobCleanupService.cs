namespace ImageResizer.Api.Services;

// Background service that periodically deletes old blobs that were not processed within a 
// configurable time frame.
public class BlobCleanupService : BackgroundService
{
    private readonly IBlobStorageService _blobs;
    private readonly ILogger<BlobCleanupService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _expiry;

    public BlobCleanupService(IBlobStorageService blobs, IConfiguration config, ILogger<BlobCleanupService> logger)
    {
        _blobs = blobs;
        _logger = logger;
        _interval = TimeSpan.FromMinutes(config.GetValue("BlobCleanup:IntervalMinutes", 60));
        _expiry = TimeSpan.FromMinutes(config.GetValue("BlobCleanup:ExpiryMinutes", 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var cutoff = DateTimeOffset.UtcNow - _expiry;
                var deleted = await _blobs.DeleteOldBlobsAsync(cutoff);
                if (deleted > 0)
                    _logger.LogInformation("Cleanup removed {Count} expired blob(s) older than {Expiry}.", deleted, _expiry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Blob cleanup failed.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
