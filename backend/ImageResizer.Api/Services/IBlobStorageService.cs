namespace ImageResizer.Api.Services;

public interface IBlobStorageService
{
    Task<string> UploadOriginalAsync(Stream stream, string fileName, string contentType);
    Task<(Stream Content, string ContentType)> DownloadOriginalAsync(string blobName);
    Task<string> UploadResizedAsync(Stream stream, string originalBlobName, string contentType);
    Task DeleteOriginalAsync(string blobName);
    Task<(Stream Content, string ContentType, string FileName)> DownloadResizedAsync(string blobName);
    Task<int> DeleteOldBlobsAsync(DateTimeOffset cutoff);
}
