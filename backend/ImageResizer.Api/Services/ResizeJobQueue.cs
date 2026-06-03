using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ImageResizer.Api.Services;

public class ResizeJobQueue
{
    private readonly Channel<ResizeJob> _channel;
    private readonly ConcurrentDictionary<string, ResizeJob> _jobs = new();

    public ResizeJobQueue(int capacity = 100)
    {
        _channel = Channel.CreateBounded<ResizeJob>(
            new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait });
    }

    public ResizeJob? Enqueue(List<string> blobNames, float percentage)
    {
        var job = new ResizeJob { BlobNames = blobNames, Percentage = percentage };
        if (!_channel.Writer.TryWrite(job))
            return null;
        _jobs[job.JobId] = job;
        return job;
    }

    public ResizeJob? GetJob(string jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job : null;

    public ChannelReader<ResizeJob> Reader => _channel.Reader;
}
