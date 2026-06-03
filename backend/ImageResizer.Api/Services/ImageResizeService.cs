using SkiaSharp;

namespace ImageResizer.Api.Services;

public class ImageResizeService
{
    public async Task<(Stream Output, string ContentType)> ResizeAsync(Stream original, float percentage)
    {
        // Buffer to MemoryStream — Azure blob download streams are non-seekable.
        var ms = new MemoryStream();
        await original.CopyToAsync(ms);

        var bytes = ms.ToArray();

        // Detect source format so the output preserves it.
        using var codec = SKCodec.Create(new SKMemoryStream(bytes));
        var encodedFormat = codec?.EncodedFormat ?? SKEncodedImageFormat.Jpeg;
        var contentType = encodedFormat  == SKEncodedImageFormat.Png  ? "image/png" : "image/jpeg";

        using var originalBitmap = SKBitmap.Decode(bytes)
            ?? throw new InvalidOperationException("Cannot decode image.");

        var newWidth  = Math.Max(1, (int)(originalBitmap.Width  * percentage / 100.0));
        var newHeight = Math.Max(1, (int)(originalBitmap.Height * percentage / 100.0));

        using var resized = originalBitmap.Resize(new SKImageInfo(newWidth, newHeight), SKSamplingOptions.Default);
        using var skImage = SKImage.FromBitmap(resized);
        using var encoded = skImage.Encode(encodedFormat, 90);

        var output = new MemoryStream();
        encoded.SaveTo(output);
        output.Position = 0;

        return (output, contentType);
    }
}
