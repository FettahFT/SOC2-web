using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text;

namespace ShadeOfColor2.Core.Services;

public class StreamingImageProcessor : IImageProcessor
{
    private const int BaseHeaderSize = 2 + 8 + 4; // Signature + FileSize + FileNameLength
    private const int MaxFileSize = 50 * 1024 * 1024; // 50MB
    private const int MaxFilenameLength = 255;
    private const int Sha256HashSize = 32;
    private const int BytesPerPixel = 4; // RGBA
    private const int ChunkSize = 64 * 1024; // 64KB chunks
    
    private readonly string _signature;
    
    public StreamingImageProcessor()
    {
        _signature = "SC"; // ShadeOfColor signature
    }

    public async Task<Image<Rgba32>> CreateCarrierImageAsync(Stream fileData, string fileName, CancellationToken cancellationToken = default)
    {
        // Force cleanup before processing
        if (MemoryMonitor.IsMemoryPressureHigh())
        {
            MemoryMonitor.ForceCleanup();
        }
        
        // Phase 1: Analyze file stream to get size and hash
        var analysis = await FileStreamAnalyzer.AnalyzeAsync(fileData, cancellationToken);
        
        // Validate file size
        if (analysis.Size > MaxFileSize)
            throw new ArgumentException($"File too large. Maximum size is {MaxFileSize / (1024 * 1024)}MB.");
        
        // Convert filename to bytes
        var fileNameBytes = Encoding.UTF8.GetBytes(fileName);
        if (fileNameBytes.Length > MaxFilenameLength)
            throw new ArgumentException($"Filename too long. Must be less than {MaxFilenameLength + 1} bytes.");

        // Calculate header size with filename and padding
        var headerWithFilename = BaseHeaderSize + fileNameBytes.Length;
        var padding = (4 - (headerWithFilename % 4)) % 4;
        var totalHeaderSize = headerWithFilename + padding + Sha256HashSize;
        
        // Calculate required pixels
        var totalDataSize = totalHeaderSize + analysis.Size;
        var pixelCount = (int)Math.Ceiling(totalDataSize / (double)BytesPerPixel);
        var imageSize = (int)Math.Ceiling(Math.Sqrt(pixelCount));

        // Create image with white background
        var image = new Image<Rgba32>(imageSize, imageSize, Color.White);
        
        try
        {
            // Write header
            var headerBytes = CreateHeader(analysis.Size, fileNameBytes, analysis.Sha256Hash);
            WriteHeaderToImage(image, headerBytes);
            
            // Phase 2: Stream file data to image pixels
            await StreamFileDataToImage(image, analysis.ResetStream, totalHeaderSize, cancellationToken);
            
            // Force cleanup after processing
            GC.Collect(0, GCCollectionMode.Optimized);
            
            return image;
        }
        catch
        {
            image?.Dispose();
            MemoryMonitor.ForceCleanup();
            throw;
        }
    }

    private byte[] CreateHeader(long fileSize, byte[] fileNameBytes, byte[] sha256Hash)
    {
        var currentSize = 2 + 8 + 4 + fileNameBytes.Length;
        var padding = (4 - (currentSize % 4)) % 4;
        var totalSize = currentSize + padding + Sha256HashSize;
        
        var bytes = new byte[totalSize];
        var offset = 0;
        
        // Signature (2 bytes)
        var signatureBytes = Encoding.ASCII.GetBytes(_signature);
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

        return bytes;
    }

    private void WriteHeaderToImage(Image<Rgba32> image, byte[] headerBytes)
    {
        WriteBytesToImage(image, 0, headerBytes);
    }

    private async Task StreamFileDataToImage(Image<Rgba32> image, Stream fileStream, int startIndex, CancellationToken cancellationToken)
    {
        var buffer = new byte[ChunkSize];
        var currentIndex = startIndex;
        
        int bytesRead;
        while ((bytesRead = await fileStream.ReadAsync(buffer, 0, ChunkSize, cancellationToken)) > 0)
        {
            var chunk = bytesRead == ChunkSize ? buffer : buffer[..bytesRead];
            WriteBytesToImage(image, currentIndex, chunk);
            currentIndex += bytesRead;
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

    // Keep existing ExtractFileAsync method unchanged for now
    public async Task<ExtractedFile> ExtractFileAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        try
        {
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
                
            var image = await Image.LoadAsync<Rgba32>(imageStream, cancellationToken);
        
            // Read and verify signature
            var signatureBytes = ReadBytesFromImage(image, 0, 2);
            var signature = Encoding.ASCII.GetString(signatureBytes);
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
            var fileName = Encoding.UTF8.GetString(fileNameBytes);

            // Calculate SHA256 offset (account for padding)
            var headerWithoutHash = 2 + 8 + 4 + fileNameLength;
            var sha256Offset = headerWithoutHash + (4 - (headerWithoutHash % 4)) % 4;

            // Read SHA256 hash
            var sha256Hash = ReadBytesFromImage(image, sha256Offset, Sha256HashSize);

            // Read file data
            var fileDataOffset = sha256Offset + Sha256HashSize;
            var fileData = ReadBytesFromImage(image, fileDataOffset, (int)fileSize);

            // Verify SHA256 hash
            var computedHash = System.Security.Cryptography.SHA256.HashData(fileData);
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
}