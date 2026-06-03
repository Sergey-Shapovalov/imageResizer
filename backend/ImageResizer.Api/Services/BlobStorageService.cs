using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace ImageResizer.Api.Services;

public class BlobStorageService : IBlobStorageService
{
    private const string ContainerName = "resize-container";
    
    private readonly BlobServiceClient _client;

    public BlobStorageService(IConfiguration config)
    {
        _client = new BlobServiceClient(config["AzureStorage:ConnectionString"]);
    }

    public async Task<string> UploadOriginalAsync(Stream stream, string fileName, string contentType)
    {
        var container = _client.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync();

        // Format: {guid}_{sanitizedFileName}
        var blobName = $"{Guid.NewGuid()}_{SanitizeFileName(fileName)}";
        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        });

        return blobName;
    }

    public async Task<(Stream Content, string ContentType)> DownloadOriginalAsync(string blobName)
    {
        var blob = _client.GetBlobContainerClient(ContainerName).GetBlobClient(blobName);
        var props = await blob.GetPropertiesAsync();
        var download = await blob.DownloadStreamingAsync();
        return (download.Value.Content, props.Value.ContentType);
    }

    public async Task<string> UploadResizedAsync(Stream stream, string originalBlobName, string contentType)
    {
        var container = _client.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync();

        // Format: {guid}_{name}_resized.{ext}  — mirrors the original {guid}_{name}.{ext} naming
        var parts = originalBlobName.Split('_', 2);
        var sanitizedFileName = parts.Length == 2 ? parts[1] : originalBlobName;
        var blobName = $"{parts[0]}_{Path.GetFileNameWithoutExtension(sanitizedFileName)}_resized{Path.GetExtension(sanitizedFileName)}";
        var blob = container.GetBlobClient(blobName);
        // No conditions = unconditional overwrite when blob already exists.
        await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        });

        return blobName;
    }

    public async Task DeleteOriginalAsync(string blobName)
    {
        var blob = _client.GetBlobContainerClient(ContainerName).GetBlobClient(blobName);
        await blob.DeleteIfExistsAsync();
    }

    public async Task<(Stream Content, string ContentType, string FileName)> DownloadResizedAsync(string blobName)
    {
        var blob = _client.GetBlobContainerClient(ContainerName).GetBlobClient(blobName);
        var props = await blob.GetPropertiesAsync();
        var download = await blob.DownloadStreamingAsync();

        // Blob name format: "{guid}_{filename}" — strip the guid prefix to get the download filename.
        // GUIDs contain '-' not '_', so splitting on '_' max 2 parts is unambiguous.
        var parts = blobName.Split('_', 2);
        var fileName = parts.Length == 2 ? parts[1] : blobName;

        return (download.Value.Content, props.Value.ContentType, fileName);
    }

    public async Task<int> DeleteOldBlobsAsync(DateTimeOffset cutoff)
    {
        int count = 0;
        count += await DeleteOldBlobsFromContainerAsync(ContainerName, cutoff);
        return count;
    }

    private async Task<int> DeleteOldBlobsFromContainerAsync(string containerName, DateTimeOffset cutoff)
    {
        var container = _client.GetBlobContainerClient(containerName);
        if (!await container.ExistsAsync())
            return 0;

        int count = 0;
        await foreach (var blob in container.GetBlobsAsync())
        {
            if (blob.Properties.CreatedOn < cutoff)
            {
                await container.GetBlobClient(blob.Name).DeleteIfExistsAsync();
                count++;
            }
        }
        return count;
    }

    private static string SanitizeFileName(string fileName) =>
        Path.GetFileName(fileName).Replace(" ", "-");
}
