using ImageResizer.Api;
using ImageResizer.Api.Services;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(o =>
    o.Limits.MaxRequestBodySize = 10L * UploadLimits.MaxFileSizeBytes);

// Trust X-Forwarded-For so the rate limiter partitions by real client IP, not the
// proxy IP, in App Service / Application Gateway / AKS ingress / CDN deployments.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// CORS policy to allow the frontend to call the API. For the test assignment purpose hardcoded the localhost origin.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(p =>
        p.WithOrigins("http://localhost:5173")
         .AllowAnyHeader()
         .AllowAnyMethod()));

// Basic IP-based rate limiting to mitigate DoS and abuse. In production, more sophisticated approaches may be applied
builder.Services.AddRateLimiter(o =>
{
    o.AddPolicy("api", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Azure-based blob storage service
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
// Image resizing service using SkiaSharp
builder.Services.AddSingleton<ImageResizeService>();
// Background service that processes the resize jobs from the queue
builder.Services.AddSingleton<ResizeJobQueue>();
// Hosted service for processing resize jobs
builder.Services.AddHostedService<ResizeWorkerService>();
// Hosted service for cleaning up old blobs
builder.Services.AddHostedService<BlobCleanupService>();

var app = builder.Build();
app.UseForwardedHeaders();
app.UseCors();
app.UseRateLimiter();

// Endpoints implemented using the minimal APIs pattern for simplicity and performance.
app.MapPost("/api/images/upload", async (
    IFormFileCollection files,
    IBlobStorageService blobs) =>
{
    if (files.Count == 0)
        return Results.BadRequest("No files provided.");

    // Limit the number of files to prevent DoS and excessive memory usage.
    if (files.Count > 10)
        return Results.BadRequest("Cannot upload more than 10 files at once.");

    var blobNames = new List<string>();
    var errors = new List<UploadError>();

    foreach (var file in files)
    {
        // Validate file size before processing to avoid excessive memory usage.
        if (file.Length > UploadLimits.MaxFileSizeBytes)
        {
            errors.Add(new UploadError(file.FileName, $"File exceeds the {UploadLimits.MaxFileSizeBytes / 1024 / 1024} MB per-file limit."));
            continue;
        }
        try
        {
            await using var stream = file.OpenReadStream();
            if (!await ImageFormatValidator.IsValidAsync(stream))
            {
                errors.Add(new UploadError(file.FileName, "Unsupported format. Only JPEG and PNG are allowed."));
                continue;
            }
            var name = await blobs.UploadOriginalAsync(stream, file.FileName, file.ContentType);
            blobNames.Add(name);
        }
        catch (Exception ex)
        {
            errors.Add(new UploadError(file.FileName, ex.Message));
        }
    }

    return Results.Ok(new { filesUploaded = blobNames.Count, blobNames, errors });
})
.DisableAntiforgery()
.WithRequestTimeout(TimeSpan.FromSeconds(30))
.RequireRateLimiting("api");

app.MapPost("/api/images/resize", (
    ResizeRequest request,
    ResizeJobQueue queue) =>
{
    if (request.Percentage < 0 || request.Percentage > 100)
        return Results.BadRequest("Percentage must be between 0 and 100.");

    if (request.BlobNames.Count == 0)
        return Results.BadRequest("No blob names provided.");

    if (request.BlobNames.Count > 10)
        return Results.BadRequest("Cannot resize more than 10 images per job.");

    // Enqueue the job. If the queue is full, return 503 to signal the client to retry later.
    var job = queue.Enqueue(request.BlobNames, request.Percentage);
    if (job is null)
        return Results.StatusCode(503);

    return Results.Ok(new { jobId = job.JobId });
})
.WithRequestTimeout(TimeSpan.FromSeconds(30))
.RequireRateLimiting("api");

app.MapGet("/api/images/resize/{jobId}/status", (string jobId, ResizeJobQueue queue) =>
{
    var job = queue.GetJob(jobId);
    if (job is null)
        return Results.NotFound();

    return Results.Ok(new
    {
        status = job.Status.ToString().ToLower(),
        resizedBlobNames = job.ResizedBlobNames,
        error = job.Error,
    });
});

app.MapGet("/api/images/download", async (
    string blobName,
    IBlobStorageService blobs) =>
{
    if (string.IsNullOrWhiteSpace(blobName))
        return Results.BadRequest("blobName is required.");

    var (content, contentType, fileName) = await blobs.DownloadResizedAsync(blobName);
    return Results.File(content, contentType, fileName);
});

app.Run();

record ResizeRequest(List<string> BlobNames, float Percentage);
record UploadError(string OriginalFileName, string ErrorMessage);

public partial class Program {}