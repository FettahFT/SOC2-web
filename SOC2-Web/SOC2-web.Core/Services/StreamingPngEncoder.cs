using System.IO.Compression;

namespace ShadeOfColor2.Core.Services;

public class StreamingPngEncoder
{
    private const int ScanlineBufferSize = 4096; // Buffer for scanlines
    
    public static async Task EncodeToStreamAsync(
        IAsyncEnumerable<RgbaPixel> pixels, 
        Stream output, 
        int width, 
        int height,
        CancellationToken cancellationToken = default)
    {
        // Write PNG header
        await PngStreamWriter.WritePngHeaderAsync(output, width, height);
        
        // Create deflate stream for compression
        using var compressedDataStream = new MemoryStream();
        using var deflateStream = new DeflateStream(compressedDataStream, CompressionLevel.Optimal, true);
        
        var scanlineBuffer = new byte[width * 4 + 1]; // +1 for filter byte
        var pixelIndex = 0;
        var currentRow = 0;
        var currentCol = 0;
        
        await foreach (var pixel in pixels.WithCancellation(cancellationToken))
        {
            // Start new scanline
            if (currentCol == 0)
            {
                scanlineBuffer[0] = 0; // No filter
            }
            
            // Add pixel to scanline buffer
            var bufferIndex = currentCol * 4 + 1; // +1 for filter byte
            scanlineBuffer[bufferIndex] = pixel.R;
            scanlineBuffer[bufferIndex + 1] = pixel.G;
            scanlineBuffer[bufferIndex + 2] = pixel.B;
            scanlineBuffer[bufferIndex + 3] = pixel.A;
            
            currentCol++;
            
            // Complete scanline - compress and potentially write
            if (currentCol >= width)
            {
                await deflateStream.WriteAsync(scanlineBuffer, 0, scanlineBuffer.Length, cancellationToken);
                
                currentCol = 0;
                currentRow++;
                
                // Write IDAT chunk when buffer reaches threshold or at end
                if (compressedDataStream.Length > ScanlineBufferSize || currentRow >= height)
                {
                    await deflateStream.FlushAsync(cancellationToken);
                    
                    if (compressedDataStream.Length > 0)
                    {
                        await PngStreamWriter.WriteIDAT(output, compressedDataStream.ToArray());
                        compressedDataStream.SetLength(0);
                        compressedDataStream.Position = 0;
                    }
                }
            }
            
            pixelIndex++;
            
            // Stop if we've processed all expected pixels
            if (currentRow >= height)
                break;
        }
        
        // Ensure deflate stream is properly closed
        deflateStream.Close();
        
        // Write any remaining compressed data
        if (compressedDataStream.Length > 0)
        {
            await PngStreamWriter.WriteIDAT(output, compressedDataStream.ToArray());
        }
        
        // Write PNG end
        await PngStreamWriter.WritePngEndAsync(output);
    }
}