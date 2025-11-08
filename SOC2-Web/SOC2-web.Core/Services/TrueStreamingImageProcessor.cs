using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ShadeOfColor2.Core.Services;

public class TrueStreamingImageProcessor : ITrueStreamingImageProcessor
{
    private const int MaxFileSize = 50 * 1024 * 1024; // 50MB
    private const int MaxFilenameLength = 255;
    
    public async Task<Image<Rgba32>> CreateCarrierImageAsync(Stream fileData, string fileName, CancellationToken cancellationToken = default)
    {
        // This method is kept for interface compatibility but shouldn't be used
        // Use CreateCarrierStreamAsync instead
        throw new NotSupportedException("Use CreateCarrierStreamAsync for true streaming");
    }
    
    public async Task CreateCarrierStreamAsync(
        Stream fileData, 
        string fileName, 
        Stream outputStream,
        CancellationToken cancellationToken = default)
    {
        // Validate file size by checking stream length if possible
        if (fileData.CanSeek && fileData.Length > MaxFileSize)
            throw new ArgumentException($"File too large. Maximum size is {MaxFileSize / (1024 * 1024)}MB.");
        
        // Validate filename
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > MaxFilenameLength)
            throw new ArgumentException("Invalid filename");
        
        // Calculate image dimensions
        var fileSize = fileData.CanSeek ? fileData.Length : MaxFileSize; // Estimate if unknown
        var (width, height) = StreamingPixelGenerator.CalculateImageDimensions(fileSize, fileName);
        var totalPixels = width * height;
        
        Console.WriteLine($"Streaming: {fileName} ({fileSize} bytes) -> {width}x{height} image");
        
        // Generate pixels and encode to PNG stream
        var pixels = StreamingPixelGenerator.GeneratePixelsAsync(fileData, fileName, totalPixels, cancellationToken);
        await StreamingPngEncoder.EncodeToStreamAsync(pixels, outputStream, width, height, cancellationToken);
        
        // Force cleanup
        GC.Collect(0, GCCollectionMode.Optimized);
    }
    
    public async Task<ExtractedFile> ExtractFileAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        // For now, fall back to original extraction method
        // TODO: Implement streaming extraction in Phase 4
        var fallbackProcessor = new ImageProcessor();
        return await fallbackProcessor.ExtractFileAsync(imageStream, cancellationToken);
    }
}