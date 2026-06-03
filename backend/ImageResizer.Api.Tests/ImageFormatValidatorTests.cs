using ImageResizer.Api.Services;

namespace ImageResizer.Api.Tests;

public class ImageFormatValidatorTests
{
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static readonly byte[] PngHeader  = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public async Task IsValidAsync_ValidJpeg_ReturnsTrue()
    {
        var stream = new MemoryStream(JpegHeader);
        Assert.True(await ImageFormatValidator.IsValidAsync(stream));
    }

    [Fact]
    public async Task IsValidAsync_ValidPng_ReturnsTrue()
    {
        var stream = new MemoryStream(PngHeader);
        Assert.True(await ImageFormatValidator.IsValidAsync(stream));
    }

    [Fact]
    public async Task IsValidAsync_RandomBytes_ReturnsFalse()
    {
        var stream = new MemoryStream([0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07]);
        Assert.False(await ImageFormatValidator.IsValidAsync(stream));
    }

    [Fact]
    public async Task IsValidAsync_EmptyStream_ReturnsFalse()
    {
        var stream = new MemoryStream([]);
        Assert.False(await ImageFormatValidator.IsValidAsync(stream));
    }

    [Fact]
    public async Task IsValidAsync_TooShortForPng_ReturnsFalse()
    {
        // PNG header is 8 bytes; only 7 provided
        var stream = new MemoryStream(PngHeader[..7]);
        Assert.False(await ImageFormatValidator.IsValidAsync(stream));
    }

    [Fact]
    public async Task IsValidAsync_ResetsStreamPositionToZero()
    {
        var stream = new MemoryStream(JpegHeader);
        await ImageFormatValidator.IsValidAsync(stream);
        Assert.Equal(0, stream.Position);
    }
}
