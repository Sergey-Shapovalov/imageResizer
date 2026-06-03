using ImageResizer.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(p =>
        p.WithOrigins("http://localhost:5173")
         .AllowAnyHeader()
         .AllowAnyMethod()));

builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
builder.Services.AddSingleton<ImageResizeService>();
builder.Services.AddSingleton<ResizeJobQueue>();
builder.Services.AddHostedService<ResizeWorkerService>();
builder.Services.AddHostedService<BlobCleanupService>();

var app = builder.Build();
app.UseCors();

app.MapPost("/api/images/upload", async (
    IFormFileCollection files,
    IBlobStorageService blobs) =>
{
    if (files.Count == 0)
        return Results.BadRequest("No files provided.");

    var blobNames = new List<string>();
    var errors = new List<UploadError>();

    foreach (var file in files)
    {
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
.WithRequestTimeout(TimeSpan.FromSeconds(30));

app.MapPost("/api/images/resize", (
    ResizeRequest request,
    ResizeJobQueue queue) =>
{
    if (request.Percentage < 0 || request.Percentage > 100)
        return Results.BadRequest("Percentage must be between 0 and 100.");

    if (request.BlobNames.Count == 0)
        return Results.BadRequest("No blob names provided.");

    var job = queue.Enqueue(request.BlobNames, request.Percentage);
    return Results.Ok(new { jobId = job.JobId });
})
.WithRequestTimeout(TimeSpan.FromSeconds(30));

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

