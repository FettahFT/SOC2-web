using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Security.Cryptography;

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
    private const int MaxFileSize = 20 * 1024 * 1024; // 20MB
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
        // Read file data once to avoid stream consumption issues
        byte[] fileBytes;
        if (fileData is MemoryStream ms)
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

        // Write file data in chunks to reduce memory pressure
        WriteFileDataInChunks(image, pixelIndex, fileBytes);
        
        // Force GC for large files
        if (fileSize > 10 * 1024 * 1024)
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

    private void WriteFileDataInChunks(Image<Rgba32> image, int startIndex, byte[] fileData)
    {
        const int chunkSize = 1024 * 1024; // 1MB chunks
        var currentIndex = startIndex;
        
        for (int i = 0; i < fileData.Length; i += chunkSize)
        {
            var remainingBytes = Math.Min(chunkSize, fileData.Length - i);
            var chunk = new byte[remainingBytes];
            Array.Copy(fileData, i, chunk, 0, remainingBytes);
            
            WriteBytesToImage(image, currentIndex, chunk);
            currentIndex += remainingBytes;
            
            // Force GC every 5MB to manage memory
            if (i > 0 && i % (5 * 1024 * 1024) == 0)
            {
                GC.Collect();
            }
        }
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
        try
        {
            // Reset stream position if possible
            if (imageStream.CanSeek && imageStream.Position != 0)
            {
                try
                {
                    imageStream.Position = 0;
                }
                catch (NotSupportedException)
                {
                    // Stream doesn't support seeking, continue anyway
                }
            }
                
            // Load image
            var image = await Image.LoadAsync<Rgba32>(imageStream, cancellationToken);
        
            // Read and verify signature
            var signatureBytes = ReadBytesFromImage(image, 0, 2);
            var signature = System.Text.Encoding.ASCII.GetString(signatureBytes);
            if (signature != _signature)
                throw new InvalidDataException("Invalid signature. This is not a ShadeOfColor2 encoded image.");

            // Read file size
            var fileSizeBytes = ReadBytesFromImage(image, 2, 8);
            var fileSize = BitConverter.ToInt64(fileSizeBytes);

            // Read filename length
            var fileNameLengthBytes = ReadBytesFromImage(image, 10, 4);
            var fileNameLength = BitConverter.ToInt32(fileNameLengthBytes);

            // Read filename
            var fileNameBytes = ReadBytesFromImage(image, 14, fileNameLength);
            var fileName = System.Text.Encoding.UTF8.GetString(fileNameBytes);

            // Calculate SHA256 offset (account for padding)
            var headerWithoutHash = 2 + 8 + 4 + fileNameLength;
            var sha256Offset = headerWithoutHash + (4 - (headerWithoutHash % 4)) % 4;

            // Read SHA256 hash
            var sha256Hash = ReadBytesFromImage(image, sha256Offset, Sha256HashSize);

            // Read file data using streaming for large files
            var fileDataOffset = sha256Offset + Sha256HashSize;
            byte[] fileData;
            
            if (fileSize > 5 * 1024 * 1024) // 5MB+
            {
                fileData = await ReadFileDataStreamingAsync(image, fileDataOffset, (int)fileSize, cancellationToken);
                
                // Force GC for large files
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            else
            {
                fileData = ReadBytesFromImage(image, fileDataOffset, (int)fileSize);
            }

            // Verify SHA256 hash
            var computedHash = SHA256.HashData(fileData);
            if (!computedHash.SequenceEqual(sha256Hash))
                throw new InvalidDataException("SHA256 hash mismatch. File may be corrupted.");

            return new ExtractedFile(fileName, fileData, sha256Hash);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Failed to extract file from image: {ex.Message}", ex);
        }
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
    
    private async Task<byte[]> ReadFileDataStreamingAsync(Image<Rgba32> image, int startIndex, int length, CancellationToken cancellationToken)
    {
        const int chunkSize = 512 * 1024; // 512KB chunks
        var result = new byte[length];
        var processedBytes = 0;
        
        while (processedBytes < length)
        {
            var remainingBytes = length - processedBytes;
            var currentChunkSize = Math.Min(chunkSize, remainingBytes);
            
            var chunk = ReadBytesFromImage(image, startIndex + processedBytes, currentChunkSize);
            Array.Copy(chunk, 0, result, processedBytes, currentChunkSize);
            
            processedBytes += currentChunkSize;
            
            // Check cancellation and yield control
            cancellationToken.ThrowIfCancellationRequested();
            
            if (processedBytes % (2 * 1024 * 1024) == 0) // Every 2MB
                await Task.Yield();
        }
        
        return result;
    }
}