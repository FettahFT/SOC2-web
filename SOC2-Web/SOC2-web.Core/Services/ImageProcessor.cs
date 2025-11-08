using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Security.Cryptography;
using ShadeOfColor2.Core.Models;

namespace ShadeOfColor2.Core.Services;

public interface IImageProcessor
{
    Task<Image<Rgba32>> CreateCarrierImageAsync(Stream fileData, string fileName, string? password = null, CancellationToken cancellationToken = default);
    Task<ExtractedFile> ExtractFileAsync(Stream imageStream, string? password = null, CancellationToken cancellationToken = default);
    Task<SteganographyMetadata> ExtractMetadataAsync(Stream imageStream, CancellationToken cancellationToken = default);
}

public record ExtractedFile(string FileName, byte[] Data, byte[] Sha256Hash);

public class ImageProcessor : IImageProcessor
{
    private const int BaseHeaderSize = 2 + 8 + 4;
    private const int MaxFileSize = 10 * 1024 * 1024; // Reduced for Railway free tier
    private const int MaxFilenameLength = 255;
    private const int Sha256HashSize = 32;
    private const int BytesPerPixel = 4;
    
    private readonly string _signature;
    
    public ImageProcessor()
    {
        _signature = "SC";
    }

    public async Task<Image<Rgba32>> CreateCarrierImageAsync(Stream fileData, string fileName, string? password = null, CancellationToken cancellationToken = default)
    {
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

        var isEncrypted = !string.IsNullOrEmpty(password);
        if (isEncrypted)
        {
            fileBytes = EncryptData(fileBytes, password!);
        }

        var fileSize = fileBytes.Length;

        if (fileSize > MaxFileSize)
            throw new ArgumentException($"File too large. Maximum size is {MaxFileSize / (1024 * 1024)}MB.");

        var sha256Hash = SHA256.HashData(fileBytes);
        
        var fileNameBytes = System.Text.Encoding.UTF8.GetBytes(fileName);
        if (fileNameBytes.Length > MaxFilenameLength)
            throw new ArgumentException($"Filename too long. Must be less than {MaxFilenameLength + 1} bytes.");

        var headerWithFilename = BaseHeaderSize + fileNameBytes.Length + 1; // +1 for IsEncrypted byte
        var padding = (4 - (headerWithFilename % 4)) % 4;
        var totalHeaderSize = headerWithFilename + padding + Sha256HashSize;
        
        var totalDataSize = totalHeaderSize + fileSize;
        var pixelCount = (int)Math.Ceiling(totalDataSize / (double)BytesPerPixel);
        var imageSize = (int)Math.Ceiling(Math.Sqrt(pixelCount));

        var image = new Image<Rgba32>(imageSize, imageSize, Color.White);
        var pixelIndex = 0;

        WriteHeader(image, pixelIndex, fileSize, fileNameBytes, sha256Hash, isEncrypted);
        pixelIndex += totalHeaderSize;

        WriteFileDataInChunks(image, pixelIndex, fileBytes);

        return image;
    }

    private void WriteHeader(Image<Rgba32> image, int startIndex, long fileSize, byte[] fileNameBytes, byte[] sha256Hash, bool isEncrypted)
    {
        var currentSize = 2 + 8 + 4 + fileNameBytes.Length + 1; // +1 for isEncrypted
        var padding = (4 - (currentSize % 4)) % 4;
        var totalSize = currentSize + padding + Sha256HashSize;

        var bytes = new byte[totalSize];
        var offset = 0;

        var signatureBytes = System.Text.Encoding.ASCII.GetBytes(_signature);
        Array.Copy(signatureBytes, 0, bytes, offset, 2);
        offset += 2;

        var fileSizeBytes = BitConverter.GetBytes(fileSize);
        Array.Copy(fileSizeBytes, 0, bytes, offset, 8);
        offset += 8;

        var fileNameLengthBytes = BitConverter.GetBytes(fileNameBytes.Length);
        Array.Copy(fileNameLengthBytes, 0, bytes, offset, 4);
        offset += 4;

        Array.Copy(fileNameBytes, 0, bytes, offset, fileNameBytes.Length);
        offset += fileNameBytes.Length;

        bytes[offset] = (byte)(isEncrypted ? 1 : 0); // IsEncrypted byte
        offset += 1;

        offset += padding;

        Array.Copy(sha256Hash, 0, bytes, offset, Sha256HashSize);

        WriteBytesToImage(image, startIndex, bytes);
    }

    private void WriteFileDataInChunks(Image<Rgba32> image, int startIndex, byte[] fileData)
    {
        WriteBytesToImage(image, startIndex, fileData);
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

    public async Task<ExtractedFile> ExtractFileAsync(Stream imageStream, string? password = null, CancellationToken cancellationToken = default)
    {
        try
        {
            if (imageStream.CanSeek && imageStream.Position != 0)
            {
                imageStream.Position = 0;
            }

            var image = await Image.LoadAsync<Rgba32>(imageStream, cancellationToken);

            var signatureBytes = ReadBytesFromImage(image, 0, 2);
            var signature = System.Text.Encoding.ASCII.GetString(signatureBytes);
            
            if (signature == "ER")
                return await ExtractFileFromOldFormatAsync(image, cancellationToken);
            
            if (signature != _signature)
                throw new InvalidDataException($"Invalid signature '{signature}'. This is not a ShadeOfColor2 encoded image.");

            var fileSizeBytes = ReadBytesFromImage(image, 2, 8);
            var fileSize = BitConverter.ToInt64(fileSizeBytes);

            var fileNameLengthBytes = ReadBytesFromImage(image, 10, 4);
            var fileNameLength = BitConverter.ToInt32(fileNameLengthBytes);

            var fileNameBytes = ReadBytesFromImage(image, 14, fileNameLength);
            var fileName = System.Text.Encoding.UTF8.GetString(fileNameBytes);

            var isEncryptedByte = ReadBytesFromImage(image, 14 + fileNameLength, 1);
            var isEncrypted = isEncryptedByte[0] == 1;

            var headerWithoutHash = 2 + 8 + 4 + fileNameLength + 1; // +1 for isEncrypted
            var sha256Offset = headerWithoutHash + (4 - (headerWithoutHash % 4)) % 4;

            var sha256Hash = ReadBytesFromImage(image, sha256Offset, Sha256HashSize);

            var fileDataOffset = sha256Offset + Sha256HashSize;
            var fileData = ReadBytesFromImage(image, fileDataOffset, (int)fileSize);

            if (isEncrypted)
            {
                if (string.IsNullOrEmpty(password))
                    throw new InvalidDataException("File is encrypted but no password provided.");
                fileData = DecryptData(fileData, password);
            }

            var computedHash = SHA256.HashData(fileData);
            if (!computedHash.SequenceEqual(sha256Hash))
                throw new InvalidDataException("SHA256 hash mismatch. File may be corrupted or wrong password.");

            return new ExtractedFile(fileName, fileData, sha256Hash);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Cannot read the uploaded file as an image. Please ensure you're uploading a valid PNG image created by this application. Error: {ex.Message}");
        }
    }

    private Task<ExtractedFile> ExtractFileFromOldFormatAsync(Image<Rgba32> image, CancellationToken cancellationToken)
    {
        const int OldFileNameFieldLength = 256;
        const int Sha1Length = 20;
        
        var fileSizeBytes = ReadBytesFromImage(image, 2, 8);
        var fileSize = BitConverter.ToInt64(fileSizeBytes);
        
        var fileNameBytes = ReadBytesFromImage(image, 10, OldFileNameFieldLength);
        var fileName = System.Text.Encoding.UTF8.GetString(fileNameBytes).TrimEnd('\0');
        
        var dataOffset = 2 + 8 + OldFileNameFieldLength + Sha1Length;
        var fileData = ReadBytesFromImage(image, dataOffset, (int)fileSize);
        
        var sha256Hash = SHA256.HashData(fileData);
        
        return Task.FromResult(new ExtractedFile(fileName, fileData, sha256Hash));
    }

    private byte[] ReadBytesFromImage(Image<Rgba32> image, int startIndex, int length)
    {
        const int ChunkSize = 1024 * 1024; // 1MB chunks
        var bytes = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var chunkLength = Math.Min(ChunkSize, length - offset);
            var chunkBytes = new byte[chunkLength];

            image.ProcessPixelRows(accessor =>
            {
                for (int i = 0; i < chunkLength; i++)
                {
                    var pixelIndex = startIndex + offset + i;
                    var pixelOffset = pixelIndex % 4;
                    var pixelPosition = pixelIndex / 4;
                    var row = pixelPosition / image.Width;
                    var col = pixelPosition % image.Width;

                    if (row >= image.Height) break;

                    var pixelRow = accessor.GetRowSpan(row);
                    var pixel = pixelRow[col];
                    chunkBytes[i] = pixelOffset switch
                    {
                        0 => pixel.R,
                        1 => pixel.G,
                        2 => pixel.B,
                        3 => pixel.A,
                        _ => 0
                    };
                }
            });

            Array.Copy(chunkBytes, 0, bytes, offset, chunkLength);
            offset += chunkLength;

            // Force GC after each chunk to free memory
            GC.Collect();
        }

        return bytes;
    }

    public async Task<SteganographyMetadata> ExtractMetadataAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        try
        {
            if (imageStream.CanSeek && imageStream.Position != 0)
            {
                imageStream.Position = 0;
            }

            var image = await Image.LoadAsync<Rgba32>(imageStream, cancellationToken);

            var signatureBytes = ReadBytesFromImage(image, 0, 2);
            var signature = System.Text.Encoding.ASCII.GetString(signatureBytes);

            if (signature == "ER")
                throw new InvalidDataException("Old format not supported for metadata extraction.");

            if (signature != _signature)
                throw new InvalidDataException($"Invalid signature '{signature}'. This is not a ShadeOfColor2 encoded image.");

            var fileSizeBytes = ReadBytesFromImage(image, 2, 8);
            var fileSize = BitConverter.ToInt64(fileSizeBytes);

            var fileNameLengthBytes = ReadBytesFromImage(image, 10, 4);
            var fileNameLength = BitConverter.ToInt32(fileNameLengthBytes);

            var fileNameBytes = ReadBytesFromImage(image, 14, fileNameLength);
            var fileName = System.Text.Encoding.UTF8.GetString(fileNameBytes);

            var isEncryptedByte = ReadBytesFromImage(image, 14 + fileNameLength, 1);
            var isEncrypted = isEncryptedByte[0] == 1;

            var headerWithoutHash = 2 + 8 + 4 + fileNameLength + 1;
            var sha256Offset = headerWithoutHash + (4 - (headerWithoutHash % 4)) % 4;

            var sha256Hash = ReadBytesFromImage(image, sha256Offset, Sha256HashSize);

            return new SteganographyMetadata
            {
                Signature = signature,
                OriginalFileSize = fileSize,
                OriginalFileName = fileName,
                Sha256Hash = sha256Hash,
                IsEncrypted = isEncrypted
            };
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Cannot read metadata from the uploaded file. Please ensure it's a valid PNG image created by this application. Error: {ex.Message}");
        }
    }

    private byte[] EncryptData(byte[] data, string password)
    {
        using var aes = Aes.Create();
        var key = new Rfc2898DeriveBytes(password, new byte[16], 10000, HashAlgorithmName.SHA256).GetBytes(32);
        aes.GenerateIV(); // Generate random IV
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        ms.Write(iv, 0, iv.Length); // Prepend IV
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
            cs.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }

    private byte[] DecryptData(byte[] data, string password)
    {
        using var aes = Aes.Create();
        var key = new Rfc2898DeriveBytes(password, new byte[16], 10000, HashAlgorithmName.SHA256).GetBytes(32);

        // Extract IV from beginning
        var iv = new byte[16];
        Array.Copy(data, 0, iv, 0, 16);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        using var ms = new MemoryStream(data, 16, data.Length - 16); // Skip IV
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var result = new MemoryStream();
        cs.CopyTo(result);
        return result.ToArray();
    }
}