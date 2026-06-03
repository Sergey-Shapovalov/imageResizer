using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ImageResizer.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace ImageResizer.Api.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public Mock<IBlobStorageService> MockBlobs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true",
                ["BlobCleanup:IntervalMinutes"] = "60",
                ["BlobCleanup:ExpiryMinutes"] = "60",
            }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBlobStorageService>();
            services.AddSingleton(MockBlobs.Object);
        });
    }
}

public class EndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly Mock<IBlobStorageService> _mockBlobs;

    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] InvalidBytes = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05];

    public EndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _mockBlobs = factory.MockBlobs;
        _mockBlobs.Reset();
        _client = factory.CreateClient();
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_NoFiles_Returns400()
    {
        var response = await _client.PostAsync("/api/images/upload", new MultipartFormDataContent());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ValidJpeg_Returns200WithBlobName()
    {
        _mockBlobs
            .Setup(b => b.UploadOriginalAsync(It.IsAny<Stream>(), "photo.jpg", It.IsAny<string>()))
            .ReturnsAsync("abc_photo.jpg");

        var response = await _client.PostAsync("/api/images/upload", BuildFileUpload(JpegBytes, "photo.jpg", "image/jpeg"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJson(response);
        Assert.Equal(1, body.GetProperty("filesUploaded").GetInt32());
        Assert.Equal("abc_photo.jpg", body.GetProperty("blobNames")[0].GetString());
        Assert.Empty(body.GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task Upload_InvalidFormat_Returns200WithError()
    {
        var response = await _client.PostAsync("/api/images/upload", BuildFileUpload(InvalidBytes, "doc.pdf", "application/pdf"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJson(response);
        Assert.Equal(0, body.GetProperty("filesUploaded").GetInt32());
        var error = body.GetProperty("errors")[0];
        Assert.Equal("doc.pdf", error.GetProperty("originalFileName").GetString());
        _mockBlobs.Verify(b => b.UploadOriginalAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ── Resize ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resize_PercentageTooLow_Returns400()
    {
        var response = await PostJson("/api/images/resize", new { blobNames = new[] { "blob1" }, percentage = -1 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Resize_PercentageTooHigh_Returns400()
    {
        var response = await PostJson("/api/images/resize", new { blobNames = new[] { "blob1" }, percentage = 101 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Resize_EmptyBlobNames_Returns400()
    {
        var response = await PostJson("/api/images/resize", new { blobNames = Array.Empty<string>(), percentage = 50 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Resize_ValidRequest_Returns200WithJobId()
    {
        var response = await PostJson("/api/images/resize", new { blobNames = new[] { "blob1" }, percentage = 50 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ParseJson(response);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("jobId").GetString()));
    }

    // ── Status ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Status_UnknownJobId_Returns404()
    {
        var response = await _client.GetAsync("/api/images/resize/unknown-id/status");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Status_KnownJobId_Returns200WithStatus()
    {
        var queue = _factory.Services.GetRequiredService<ResizeJobQueue>();
        var job = queue.Enqueue(["blob1"], 50f);

        var response = await _client.GetAsync($"/api/images/resize/{job.JobId}/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = (await ParseJson(response)).GetProperty("status").GetString();
        Assert.True(status is "queued" or "processing" or "done" or "failed");
    }

    // ── Download ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Download_MissingBlobName_Returns400()
    {
        var response = await _client.GetAsync("/api/images/download?blobName=");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Download_ValidBlobName_ReturnsFile()
    {
        _mockBlobs
            .Setup(b => b.DownloadResizedAsync("abc_photo_resized.jpg"))
            .ReturnsAsync((new MemoryStream(JpegBytes) as Stream, "image/jpeg", "photo_resized.jpg"));

        var response = await _client.GetAsync("/api/images/download?blobName=abc_photo_resized.jpg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MultipartFormDataContent BuildFileUpload(byte[] bytes, string fileName, string contentType)
    {
        var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "files", fileName);
        return form;
    }

    private Task<HttpResponseMessage> PostJson(string url, object body)
    {
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return _client.PostAsync(url, content);
    }

    private static async Task<JsonElement> ParseJson(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json).RootElement;
    }
}
