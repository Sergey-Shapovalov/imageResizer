using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ImageResizer.Api.Services;

public class ResizeJobQueue
{
    private readonly Channel<ResizeJob> _channel = Channel.CreateUnbounded<ResizeJob>();
    private readonly ConcurrentDictionary<string, ResizeJob> _jobs = new();

    public ResizeJob Enqueue(List<string> blobNames, float percentage)
    {
        var job = new ResizeJob { BlobNames = blobNames, Percentage = percentage };
        _jobs[job.JobId] = job;
        _channel.Writer.TryWrite(job);
        return job;
    }

    public ResizeJob? GetJob(string jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job : null;

    public ChannelReader<ResizeJob> Reader => _channel.Reader;
}
