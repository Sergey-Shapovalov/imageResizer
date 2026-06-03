namespace ImageResizer.Api.Services;

public static class ImageFormatValidator
{
    private static readonly byte[] JpegPrefix = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngPrefix  = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static async Task<bool> IsValidAsync(Stream stream)
    {
        var header = new byte[8];
        var read = await stream.ReadAsync(header);
        stream.Position = 0;

        return header[..3].SequenceEqual(JpegPrefix) ||
               (read == 8 && header.SequenceEqual(PngPrefix));
    }
}
