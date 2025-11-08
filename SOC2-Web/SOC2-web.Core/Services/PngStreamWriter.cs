using System.Text;

namespace ShadeOfColor2.Core.Services;

public class PngStreamWriter
{
    private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };
    
    public static async Task WritePngHeaderAsync(Stream output, int width, int height)
    {
        // PNG signature
        await output.WriteAsync(PngSignature);
        
        // IHDR chunk
        await WriteChunkAsync(output, "IHDR", CreateIHDRData(width, height));
    }
    
    public static async Task WriteIDAT(Stream output, byte[] compressedData)
    {
        await WriteChunkAsync(output, "IDAT", compressedData);
    }
    
    public static async Task WritePngEndAsync(Stream output)
    {
        await WriteChunkAsync(output, "IEND", Array.Empty<byte>());
    }
    
    private static byte[] CreateIHDRData(int width, int height)
    {
        var data = new byte[13];
        var offset = 0;
        
        // Width (4 bytes, big-endian)
        WriteInt32BigEndian(data, offset, width);
        offset += 4;
        
        // Height (4 bytes, big-endian)
        WriteInt32BigEndian(data, offset, height);
        offset += 4;
        
        // Bit depth (1 byte) - 8 bits per channel
        data[offset++] = 8;
        
        // Color type (1 byte) - RGBA
        data[offset++] = 6;
        
        // Compression method (1 byte) - deflate
        data[offset++] = 0;
        
        // Filter method (1 byte) - adaptive
        data[offset++] = 0;
        
        // Interlace method (1 byte) - no interlace
        data[offset] = 0;
        
        return data;
    }
    
    private static async Task WriteChunkAsync(Stream output, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        
        // Length (4 bytes, big-endian)
        var lengthBytes = new byte[4];
        WriteInt32BigEndian(lengthBytes, 0, data.Length);
        await output.WriteAsync(lengthBytes);
        
        // Type (4 bytes)
        await output.WriteAsync(typeBytes);
        
        // Data
        await output.WriteAsync(data);
        
        // CRC (4 bytes)
        var crc = CalculateCRC(typeBytes, data);
        var crcBytes = new byte[4];
        WriteInt32BigEndian(crcBytes, 0, (int)crc);
        await output.WriteAsync(crcBytes);
    }
    
    private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
    
    private static uint CalculateCRC(byte[] type, byte[] data)
    {
        var crcTable = GenerateCRCTable();
        uint crc = 0xFFFFFFFF;
        
        foreach (byte b in type)
            crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        
        foreach (byte b in data)
            crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        
        return crc ^ 0xFFFFFFFF;
    }
    
    private static uint[] GenerateCRCTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++)
            {
                if ((c & 1) == 1)
                    c = 0xEDB88320 ^ (c >> 1);
                else
                    c >>= 1;
            }
            table[i] = c;
        }
        return table;
    }
}