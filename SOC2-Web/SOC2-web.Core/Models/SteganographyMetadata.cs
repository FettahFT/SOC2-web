using System.ComponentModel.DataAnnotations;

namespace ShadeOfColor2.Core.Models;

public class SteganographyMetadata
{
    [Required]
    [StringLength(2, MinimumLength = 2)]
    public string Signature { get; set; } = string.Empty; // 2 bytes
    
    [Range(1, long.MaxValue)]
    public long OriginalFileSize { get; set; }
    
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string OriginalFileName { get; set; } = string.Empty;
    
    [Required]
    [MinLength(32)]
    [MaxLength(32)]
    public byte[] Sha256Hash { get; set; } = new byte[32]; // SHA256 is 32 bytes
}