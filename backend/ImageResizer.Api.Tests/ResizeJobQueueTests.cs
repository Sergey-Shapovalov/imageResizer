using ImageResizer.Api.Services;

namespace ImageResizer.Api.Tests;

public class ResizeJobQueueTests
{
    [Fact]
    public void Enqueue_ReturnsJobWithCorrectProperties()
    {
        var queue = new ResizeJobQueue();
        var blobNames = new List<string> { "blob1", "blob2" };

        var job = queue.Enqueue(blobNames, 50f);

        Assert.Equal(blobNames, job.BlobNames);
        Assert.Equal(50f, job.Percentage);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.NotEmpty(job.JobId);
    }

    [Fact]
    public void Enqueue_JobIsRetrievableById()
    {
        var queue = new ResizeJobQueue();

        var job = queue.Enqueue(["blob1"], 75f);

        Assert.Same(job, queue.GetJob(job.JobId));
    }

    [Fact]
    public void GetJob_UnknownId_ReturnsNull()
    {
        var queue = new ResizeJobQueue();

        Assert.Null(queue.GetJob("does-not-exist"));
    }

    [Fact]
    public void Enqueue_WritesJobToChannel()
    {
        var queue = new ResizeJobQueue();

        var job = queue.Enqueue(["blob1"], 50f);

        Assert.True(queue.Reader.TryRead(out var read));
        Assert.Same(job, read);
    }

    [Fact]
    public void Enqueue_MultipleJobs_AllRetrievable()
    {
        var queue = new ResizeJobQueue();

        var job1 = queue.Enqueue(["blob1"], 25f);
        var job2 = queue.Enqueue(["blob2"], 75f);

        Assert.Same(job1, queue.GetJob(job1.JobId));
        Assert.Same(job2, queue.GetJob(job2.JobId));
        Assert.NotEqual(job1.JobId, job2.JobId);
    }
}
