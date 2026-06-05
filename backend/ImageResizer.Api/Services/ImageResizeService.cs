using SkiaSharp;

namespace ImageResizer.Api.Services;

// Service that performs the actual image resizing using SkiaSharp. 
// It also checks the image dimensions against a pixel budget to guard against decompression bombs 
// before allocating memory for the bitmap.
public class ImageResizeService
{
    // Guard against decompression bombs before allocating full bitmap memory.
    private const long MaxPixelBudget = 100_000_000L; // 100 MP

    public async Task<(Stream Output, string ContentType)> ResizeAsync(Stream original, float percentage)
    {
        // Buffer to MemoryStream — Azure blob download streams are non-seekable.
        var ms = new MemoryStream();
        await original.CopyToAsync(ms);

        var bytes = ms.ToArray();

        // Detect source format so the output preserves it.
        using var codec = SKCodec.Create(new SKMemoryStream(bytes));
        var encodedFormat = codec?.EncodedFormat ?? SKEncodedImageFormat.Jpeg;
        var contentType = encodedFormat == SKEncodedImageFormat.Png ? "image/png" : "image/jpeg";

        if (codec is not null)
        {
            // Check pixel count against budget to prevent DoS via decompression bombs.
            var (w, h) = (codec.Info.Width, codec.Info.Height);
            if ((long)w * h > MaxPixelBudget)
                throw new InvalidOperationException(
                    $"Image dimensions ({w}×{h}) exceed the {MaxPixelBudget / 1_000_000} MP limit.");
        }

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
