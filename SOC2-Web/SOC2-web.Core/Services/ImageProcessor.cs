using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Security.Cryptography;
using SixLabors.ImageSharp.Formats;

namespace ShadeOfColor2.Core.Services;

public interface IImageProcessor
{
    Task<Image<Rgba32>> CreateCarrierImageAsync(Stream fileData, string fileName, CancellationToken cancellationToken = default);
    Task<ExtractedFile> ExtractFileAsync(Stream imageStream, CancellationToken cancellationToken = default);
}

public record ExtractedFile(string FileName, byte[] Data, byte[] Sha256Hash);

public class ImageProcessor : IImageProcessor
{
    private const int BaseHeaderSize = 2 + 8 + 4; // Signature + FileSize + FileNameLength
    private const int MaxFileSize = 50 * 1024 * 1024; // 50MB
    private const int MaxFilenameLength = 255;
    private const int Sha256HashSize = 32;
    private const int BytesPerPixel = 4; // RGBA
    
    private readonly string _signature;
    
    public ImageProcessor()
    {
        _signature = "SC"; // ShadeOfColor signature
    }

    public async Task<Image<Rgba32>> CreateCarrierImageAsync(Stream fileData, string fileName, CancellationToken cancellationToken = default)
    {
        // Read entire file into memory to avoid stream consumption issues
        byte[] fileBytes;
        if (fileData is MemoryStream ms && fileData.CanSeek)
        {
            fileBytes = ms.ToArray();
        }
        else
        {
            using var tempStream = new MemoryStream();
            await fileData.CopyToAsync(tempStream, cancellationToken);
            fileBytes = tempStream.ToArray();
        }
        
        var fileSize = fileBytes.Length;
        
        // Validate file size early
        if (fileSize > MaxFileSize)
            throw new ArgumentException($"File too large. Maximum size is {MaxFileSize / (1024 * 1024)}MB.");

        // Calculate SHA256 hash from byte array
        var sha256Hash = SHA256.HashData(fileBytes);


        
        // Convert filename to bytes
        var fileNameBytes = System.Text.Encoding.UTF8.GetBytes(fileName);
        if (fileNameBytes.Length > MaxFilenameLength)
            throw new ArgumentException($"Filename too long. Must be less than {MaxFilenameLength + 1} bytes.");

        // Calculate header size with filename and padding
        var headerWithFilename = BaseHeaderSize + fileNameBytes.Length;
        var padding = (4 - (headerWithFilename % 4)) % 4;
        var totalHeaderSize = headerWithFilename + padding + Sha256HashSize;
        
        // Calculate required pixels
        var totalDataSize = totalHeaderSize + fileSize;
        var pixelCount = (int)Math.Ceiling(totalDataSize / (double)BytesPerPixel);
        var imageSize = (int)Math.Ceiling(Math.Sqrt(pixelCount));

        // Create image with white background
        var image = new Image<Rgba32>(imageSize, imageSize, Color.White);
        var pixelIndex = 0;

        // Write header
        WriteHeader(image, pixelIndex, fileSize, fileNameBytes, sha256Hash);
        pixelIndex += totalHeaderSize;

        // Write file data from byte array
        WriteBytesToImage(image, pixelIndex, fileBytes);
        
        // Force garbage collection after processing large file
        if (fileSize > 10 * 1024 * 1024) // 10MB+
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return image;
    }

    private void WriteHeader(Image<Rgba32> image, int startIndex, long fileSize, byte[] fileNameBytes, byte[] sha256Hash)
    {
        var currentSize = 2 + 8 + 4 + fileNameBytes.Length;
        var padding = (4 - (currentSize % 4)) % 4;
        var totalSize = currentSize + padding + Sha256HashSize;
        
        var bytes = new byte[totalSize];
        var offset = 0;
        
        // Signature (2 bytes)
        var signatureBytes = System.Text.Encoding.ASCII.GetBytes(_signature);
        Array.Copy(signatureBytes, 0, bytes, offset, 2);
        offset += 2;
        
        // File size (8 bytes, little-endian)
        var fileSizeBytes = BitConverter.GetBytes(fileSize);
        Array.Copy(fileSizeBytes, 0, bytes, offset, 8);
        offset += 8;
        
        // Filename length (4 bytes, little-endian)
        var fileNameLengthBytes = BitConverter.GetBytes(fileNameBytes.Length);
        Array.Copy(fileNameLengthBytes, 0, bytes, offset, 4);
        offset += 4;
        
        // Filename bytes
        Array.Copy(fileNameBytes, 0, bytes, offset, fileNameBytes.Length);
        offset += fileNameBytes.Length;
        
        // Padding (already zeroed in new byte array)
        offset += padding;
        
        // SHA256 hash
        Array.Copy(sha256Hash, 0, bytes, offset, Sha256HashSize);

        WriteBytesToImage(image, startIndex, bytes);
    }



    private void WriteBytesToImage(Image<Rgba32> image, int startIndex, byte[] data)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int i = 0; i < data.Length; i++)
            {
                var pixelIndex = startIndex + i;
                var pixelOffset = pixelIndex % 4;
                var pixelPosition = pixelIndex / 4;
                var row = pixelPosition / image.Width;
                var col = pixelPosition % image.Width;

                if (row >= image.Height) break;

                var pixelRow = accessor.GetRowSpan(row);
                var pixel = pixelRow[col];
                switch (pixelOffset)
                {
                    case 0: pixel.R = data[i]; break;
                    case 1: pixel.G = data[i]; break;
                    case 2: pixel.B = data[i]; break;
                    case 3: pixel.A = data[i]; break;
                }
                pixelRow[col] = pixel;
            }
        });
    }

    public async Task<ExtractedFile> ExtractFileAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        Image<Rgba32>? image = null;
        byte[]? fileData = null;
        try
        {
            // Reset stream position if possible
            if (imageStream.CanSeek && imageStream.Position != 0)
            {
                imageStream.Position = 0;
            }
            
            // Load image directly from stream (revert to original approach)
            image = await Image.LoadAsync<Rgba32>(imageStream, cancellationToken);
            
            Console.WriteLine($"[{DateTime.UtcNow}] Image loaded successfully: {image.Width}x{image.Height}");
        
        // Read and verify signature
        var signatureBytes = ReadBytesFromImage(image, 0, 2);
        var signature = System.Text.Encoding.ASCII.GetString(signatureBytes);
        Console.WriteLine($"[{DateTime.UtcNow}] Read signature: '{signature}', Expected: '{_signature}'");
        
        if (signature != _signature)
            throw new InvalidDataException($"Invalid signature. Found '{signature}', expected '{_signature}'. This is not a ShadeOfColor2 encoded image.");

        // Read file size
        var fileSizeBytes = ReadBytesFromImage(image, 2, 8);
        var fileSize = BitConverter.ToInt64(fileSizeBytes);
        Console.WriteLine($"[{DateTime.UtcNow}] File size: {fileSize} bytes");

        // Read filename length
        var fileNameLengthBytes = ReadBytesFromImage(image, 10, 4);
        var fileNameLength = BitConverter.ToInt32(fileNameLengthBytes);
        Console.WriteLine($"[{DateTime.UtcNow}] Filename length: {fileNameLength}");

        // Read filename
        var fileNameBytes = ReadBytesFromImage(image, 14, fileNameLength);
        var fileName = System.Text.Encoding.UTF8.GetString(fileNameBytes);
        Console.WriteLine($"[{DateTime.UtcNow}] Filename: '{fileName}'")

        // Calculate SHA256 offset (account for padding)
        var headerWithoutHash = 2 + 8 + 4 + fileNameLength;
        var sha256Offset = headerWithoutHash + (4 - (headerWithoutHash % 4)) % 4;

        // Read SHA256 hash
        var sha256Hash = ReadBytesFromImage(image, sha256Offset, Sha256HashSize);

        // Read file data using streaming for large files
        var fileDataOffset = sha256Offset + Sha256HashSize;
        fileData = await ReadFileDataStreamingAsync(image, fileDataOffset, (int)fileSize, cancellationToken);

        // Dispose image immediately after reading data
        image.Dispose();
        image = null;

        // Verify SHA256 hash
        var computedHash = SHA256.HashData(fileData);
        if (!computedHash.SequenceEqual(sha256Hash))
        {
            // Clear file data on hash mismatch
            Array.Clear(fileData, 0, fileData.Length);
            throw new InvalidDataException("SHA256 hash mismatch. File may be corrupted.");
        }

        // Force garbage collection for large files
        if (fileSize > 5 * 1024 * 1024) // 5MB+
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        return new ExtractedFile(fileName, fileData, sha256Hash);
        }
        catch (OperationCanceledException)
        {
            // Clean up on cancellation
            if (fileData != null)
            {
                Array.Clear(fileData, 0, fileData.Length);
            }
            throw;
        }
        catch (UnknownImageFormatException ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow}] UnknownImageFormatException: {ex.Message}");
            throw new InvalidDataException("This image was not created by ShadeOfColor2 or the file is corrupted.");
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to extract file from image: {ex.Message}", ex);
        }
        finally
        {
            // Ensure proper disposal
            image?.Dispose();
            
            // Force cleanup on cancellation or error
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
    
    private async Task<byte[]> ReadFileDataStreamingAsync(Image<Rgba32> image, int startIndex, int length, CancellationToken cancellationToken)
    {
        const int chunkSize = 512 * 1024; // 512KB chunks for better CPU utilization
        var result = new byte[length];
        var processedBytes = 0;
        
        // Disable parallel processing temporarily to avoid data corruption
        // TODO: Re-enable after fixing thread safety issues
        if (false) // length > 10 * 1024 * 1024) // 10MB+
        {
            // Parallel processing code disabled
        }
        else
        {
            // Sequential processing for smaller files
            while (processedBytes < length)
            {
                var remainingBytes = length - processedBytes;
                var currentChunkSize = Math.Min(chunkSize, remainingBytes);
                
                var chunk = ReadBytesFromImage(image, startIndex + processedBytes, currentChunkSize);
                Array.Copy(chunk, 0, result, processedBytes, currentChunkSize);
                
                processedBytes += currentChunkSize;
                
                // Check cancellation more frequently
                cancellationToken.ThrowIfCancellationRequested();
                
                // Yield less frequently to improve performance
                if (processedBytes % (4 * 1024 * 1024) == 0) // Every 4MB
                    await Task.Yield();
            }
        }
        
        return result;
    }

    private byte[] ReadBytesFromImage(Image<Rgba32> image, int startIndex, int length)
    {
        var bytes = new byte[length];
        
        image.ProcessPixelRows(accessor =>
        {
            for (int i = 0; i < length; i++)
            {
                var pixelIndex = startIndex + i;
                var pixelOffset = pixelIndex % 4;
                var pixelPosition = pixelIndex / 4;
                var row = pixelPosition / image.Width;
                var col = pixelPosition % image.Width;

                if (row >= image.Height) break;

                var pixelRow = accessor.GetRowSpan(row);
                var pixel = pixelRow[col];
                bytes[i] = pixelOffset switch
                {
                    0 => pixel.R,
                    1 => pixel.G,
                    2 => pixel.B,
                    3 => pixel.A,
                    _ => 0
                };
            }
        });
        
        return bytes;
    }
}