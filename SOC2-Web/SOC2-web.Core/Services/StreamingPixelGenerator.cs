using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;

namespace ShadeOfColor2.Core.Services;

public struct RgbaPixel
{
    public byte R, G, B, A;
    
    public RgbaPixel(byte r, byte g, byte b, byte a = 255)
    {
        R = r; G = g; B = b; A = a;
    }
}

public class StreamingPixelGenerator
{
    private const int BaseHeaderSize = 2 + 8 + 4; // Signature + FileSize + FileNameLength
    private const int Sha256HashSize = 32;
    private const int ChunkSize = 64 * 1024; // 64KB chunks
    
    public static async IAsyncEnumerable<RgbaPixel> GeneratePixelsAsync(
        Stream fileData, 
        string fileName, 
        int totalPixels,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Phase 1: Generate header pixels
        var headerPixels = await GenerateHeaderPixelsAsync(fileData, fileName, cancellationToken);
        foreach (var pixel in headerPixels)
        {
            yield return pixel;
        }
        
        // Phase 2: Generate file data pixels
        var pixelsGenerated = headerPixels.Count;
        var buffer = new byte[ChunkSize];
        
        // Reset stream to beginning for data reading
        if (fileData.CanSeek)
            fileData.Position = 0;
        
        int bytesRead;
        while ((bytesRead = await fileData.ReadAsync(buffer, 0, ChunkSize, cancellationToken)) > 0)
        {
            for (int i = 0; i < bytesRead && pixelsGenerated < totalPixels; i += 4)
            {
                var r = i < bytesRead ? buffer[i] : (byte)0;
                var g = i + 1 < bytesRead ? buffer[i + 1] : (byte)0;
                var b = i + 2 < bytesRead ? buffer[i + 2] : (byte)0;
                var a = i + 3 < bytesRead ? buffer[i + 3] : (byte)255;
                
                yield return new RgbaPixel(r, g, b, a);
                pixelsGenerated++;
            }
        }
        
        // Fill remaining pixels with white
        while (pixelsGenerated < totalPixels)
        {
            yield return new RgbaPixel(255, 255, 255, 255);
            pixelsGenerated++;
        }
    }
    
    private static async Task<List<RgbaPixel>> GenerateHeaderPixelsAsync(
        Stream fileData, 
        string fileName, 
        CancellationToken cancellationToken)
    {
        // Calculate file size and hash
        var analysis = await FileStreamAnalyzer.AnalyzeAsync(fileData, cancellationToken);
        
        // Create header data
        var fileNameBytes = Encoding.UTF8.GetBytes(fileName);
        var headerSize = BaseHeaderSize + fileNameBytes.Length;
        var padding = (4 - (headerSize % 4)) % 4;
        var totalHeaderSize = headerSize + padding + Sha256HashSize;
        
        var headerData = new byte[totalHeaderSize];
        var offset = 0;
        
        // Signature "SC"
        headerData[offset++] = (byte)'S';
        headerData[offset++] = (byte)'C';
        
        // File size (8 bytes, little-endian)
        var fileSizeBytes = BitConverter.GetBytes(analysis.Size);
        Array.Copy(fileSizeBytes, 0, headerData, offset, 8);
        offset += 8;
        
        // Filename length (4 bytes, little-endian)
        var fileNameLengthBytes = BitConverter.GetBytes(fileNameBytes.Length);
        Array.Copy(fileNameLengthBytes, 0, headerData, offset, 4);
        offset += 4;
        
        // Filename bytes
        Array.Copy(fileNameBytes, 0, headerData, offset, fileNameBytes.Length);
        offset += fileNameBytes.Length;
        
        // Padding (already zeroed)
        offset += padding;
        
        // SHA256 hash
        Array.Copy(analysis.Sha256Hash, 0, headerData, offset, Sha256HashSize);
        
        // Convert header bytes to pixels
        var pixels = new List<RgbaPixel>();
        for (int i = 0; i < headerData.Length; i += 4)
        {
            var r = headerData[i];
            var g = i + 1 < headerData.Length ? headerData[i + 1] : (byte)0;
            var b = i + 2 < headerData.Length ? headerData[i + 2] : (byte)0;
            var a = i + 3 < headerData.Length ? headerData[i + 3] : (byte)255;
            
            pixels.Add(new RgbaPixel(r, g, b, a));
        }
        
        return pixels;
    }
    
    public static (int width, int height) CalculateImageDimensions(long fileSize, string fileName)
    {
        var fileNameBytes = Encoding.UTF8.GetBytes(fileName);
        var headerSize = BaseHeaderSize + fileNameBytes.Length;
        var padding = (4 - (headerSize % 4)) % 4;
        var totalHeaderSize = headerSize + padding + Sha256HashSize;
        
        var totalDataSize = totalHeaderSize + fileSize;
        var pixelCount = (int)Math.Ceiling(totalDataSize / 4.0);
        var imageSize = (int)Math.Ceiling(Math.Sqrt(pixelCount));
        
        return (imageSize, imageSize);
    }
}