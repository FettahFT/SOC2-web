using ShadeOfColor2.Core.Services;
using System.Net.Mime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Configure port for Railway deployment
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Configure request size limits
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 52428800; // 50MB
});

builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 52428800; // 50MB
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
});

builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = 52428800; // 50MB
    options.MultipartHeadersLengthLimit = int.MaxValue;
});

// Register services
builder.Services.AddSingleton<IImageProcessor, ImageProcessor>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAntiforgery();

// Add rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("hide", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });
    
    options.AddFixedWindowLimiter("extract", limiterOptions =>
    {
        limiterOptions.PermitLimit = 15;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });
    
    options.AddFixedWindowLimiter("health", limiterOptions =>
    {
        limiterOptions.PermitLimit = 100;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });
});

// Add CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
            "http://localhost:3000", 
            "http://localhost:5173",
            "https://soc2-web.netlify.app",
            "https://soc2-web-production.up.railway.app"
        )
        .AllowAnyHeader()
        .AllowAnyMethod();
    });
    
    // Fallback policy - allow any origin
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("X-Original-Filename");
    });
});

var app = builder.Build();

// Use CORS before other middleware
app.UseCors("AllowAll");

// Add detailed error handling
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        Console.WriteLine($"[{DateTime.UtcNow}] Unhandled exception: {error?.Error?.GetType().Name} - {error?.Error?.Message}");
        
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new 
        { 
            error = "Server error occurred", 
            type = error?.Error?.GetType().Name ?? "Unknown",
            message = error?.Error?.Message ?? "Unknown error"
        }));
    });
});
app.UseRateLimiter();
app.UseAntiforgery();

// Health check
app.MapGet("/", () => 
{
    Console.WriteLine($"[{DateTime.UtcNow}] Health check accessed");
    return Results.Ok(new { status = "ShadeOfColor2 API is running" });
})
.RequireRateLimiting("health");

// Encode endpoint - hide file in image
app.MapPost("/api/hide", async (IFormFile file, string password, IImageProcessor processor, CancellationToken cancellationToken) =>
{
    var startTime = DateTime.UtcNow;
    var initialMemory = GC.GetTotalMemory(false);
    Console.WriteLine($"[{startTime}] Hide endpoint accessed - File: {file?.FileName}, Initial Memory: {initialMemory / 1024 / 1024}MB");
    
    // Validate input
    if (string.IsNullOrWhiteSpace(password))
        return Results.BadRequest(new { error = "Password is required" });

    var validationResult = ValidateUploadedFile(file!);
    if (validationResult != null)
        return validationResult;
        
    // Check available memory before processing large files
    var availableMemory = GC.GetTotalMemory(false);
    if (file!.Length > 7 * 1024 * 1024 && availableMemory > 300 * 1024 * 1024) // 7MB file, 300MB memory
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        return Results.BadRequest(new { error = "Server memory pressure detected. Please try a smaller file or wait a moment." });
    }

    try
    {
        // Check memory before processing
        var currentMemory = GC.GetTotalMemory(false);
        if (currentMemory > 200 * 1024 * 1024) // 200MB
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            currentMemory = GC.GetTotalMemory(false);
            Console.WriteLine($"[{DateTime.UtcNow}] Forced GC before encryption - Memory: {currentMemory / 1024 / 1024}MB");
        }

        using var fileStream = file!.OpenReadStream();
        var encodedImage = await processor.CreateCarrierImageAsync(
            fileStream,
            Path.GetFileName(file!.FileName),
            password,
            cancellationToken
        );

        // Generate random PNG name to hide original file type
        var randomName = $"image_{Guid.NewGuid().ToString("N")[..8]}.png";

        // Convert to byte array to avoid disposal issues
        using var memoryStream = new MemoryStream();
        Console.WriteLine($"[{DateTime.UtcNow}] Saving image {encodedImage.Width}x{encodedImage.Height} to PNG");

        try
        {
            await encodedImage.SaveAsync(memoryStream, new PngEncoder(), cancellationToken);
            Console.WriteLine($"[{DateTime.UtcNow}] PNG saved successfully, size: {memoryStream.Length} bytes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow}] Failed to save PNG: {ex.Message}");
            Console.WriteLine($"[{DateTime.UtcNow}] Stack trace: {ex.StackTrace}");
            return Results.BadRequest(new { error = $"Failed to create PNG image: {ex.Message}" });
        }

        // Verify image before disposal by reloading
        var imageBytes = memoryStream.ToArray();
        if (imageBytes.Length == 0)
        {
            Console.WriteLine($"[{DateTime.UtcNow}] ERROR: Generated PNG is empty!");
            throw new InvalidOperationException("Generated PNG is empty");
        }

        try
        {
            using var verifyStream = new MemoryStream(imageBytes);
            using var verifyImage = await Image.LoadAsync<Rgba32>(verifyStream, cancellationToken);
            Console.WriteLine($"[{DateTime.UtcNow}] Encryption - Image saved and reloaded successfully: {verifyImage.Width}x{verifyImage.Height}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow}] Encryption - Failed to reload saved image: {ex.Message}");
            Console.WriteLine($"[{DateTime.UtcNow}] Image bytes length: {imageBytes.Length}, first 20 bytes: {Convert.ToHexString(imageBytes.Take(20).ToArray())}");
            throw new InvalidOperationException($"Generated image is invalid: {ex.Message}");
        }

        encodedImage.Dispose(); // Dispose immediately after saving
        
        // Response is created above with memory cleanup
        
        Console.WriteLine($"[{DateTime.UtcNow}] Returning PNG file: {randomName}, size: {imageBytes.Length} bytes");
        
        return Results.File(
            imageBytes,
            "image/png",
            randomName
        );
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (OutOfMemoryException ex)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Hide endpoint OOM: {ex.Message}");
        GC.Collect();
        return Results.BadRequest(new { error = "File too large for current server capacity. Try a smaller file." });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Hide endpoint error: {ex.GetType().Name} - {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
        return Results.BadRequest(new { error = $"Processing failed: {ex.Message}" });
    }
    finally
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        
        var endTime = DateTime.UtcNow;
        var finalMemory = GC.GetTotalMemory(false);
        var duration = endTime - startTime;
        Console.WriteLine($"[{endTime}] Hide endpoint completed - Duration: {duration.TotalSeconds:F2}s, Final Memory: {finalMemory / 1024 / 1024}MB, Memory Delta: {(finalMemory - initialMemory) / 1024 / 1024}MB");
    }
})
.DisableAntiforgery()
.RequireRateLimiting("hide")
.Produces(200, contentType: "image/png")
.Produces(400);

// Decode endpoint - extract file from image
app.MapPost("/api/extract", async (HttpContext context, IFormFile image, string? password, IImageProcessor processor, CancellationToken cancellationToken) =>
{
    var startTime = DateTime.UtcNow;
    var initialMemory = GC.GetTotalMemory(false);
    Console.WriteLine($"[{startTime}] Extract endpoint accessed - Image: {image?.FileName}, Initial Memory: {initialMemory / 1024 / 1024}MB");
    
    // Validate input
    var validationResult = ValidateUploadedImage(image!);
    if (validationResult != null)
        return validationResult;

    Console.WriteLine($"[{DateTime.UtcNow}] Extract - Password provided: {!string.IsNullOrWhiteSpace(password)}");

    // Check available memory before processing large images
    var availableMemory = GC.GetTotalMemory(false);
    if (image!.Length > 15 * 1024 * 1024 && availableMemory > 400 * 1024 * 1024) // 15MB image, 400MB memory
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        return Results.BadRequest(new { error = "Server memory pressure detected. Please try again in a moment." });
    }

    try
    {
        using var imageStream = image!.OpenReadStream();
        Console.WriteLine($"[{DateTime.UtcNow}] Processing uploaded image: {image.FileName}, size: {image.Length} bytes");

        // Read first few bytes to check if it's a valid PNG
        var buffer = new byte[20];
        var bytesRead = await imageStream.ReadAsync(buffer, 0, 20, cancellationToken);
        Console.WriteLine($"[{DateTime.UtcNow}] First 20 bytes: {Convert.ToHexString(buffer[..bytesRead])}");
        imageStream.Position = 0;

        var extractedFile = await processor.ExtractFileAsync(imageStream, password, cancellationToken);

        // Use the original filename stored in the image (includes extension)
        var originalFileName = extractedFile.FileName;
        Console.WriteLine($"[{DateTime.UtcNow}] Extracted file: {originalFileName}, Size: {extractedFile.Data.Length / 1024 / 1024}MB");

        // Add custom header for reliable filename extraction
        context.Response.Headers["X-Original-Filename"] = originalFileName;

        // Create response
        return Results.File(
            extractedFile.Data,
            "application/octet-stream",
            originalFileName
        );
    }
    catch (InvalidDataException ex)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Extract endpoint - Invalid data: {ex.Message}");
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (UnknownImageFormatException ex)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Extract endpoint - Unknown image format: {ex.Message}");
        return Results.BadRequest(new { error = "Image format not supported or file is corrupted" });
    }
    catch (OutOfMemoryException ex)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Extract endpoint OOM: {ex.Message}");
        GC.Collect();
        return Results.BadRequest(new { error = "Image too large for current server capacity. Try a smaller image." });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Extract endpoint error: {ex.GetType().Name} - {ex.Message}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
        return Results.BadRequest(new { error = $"Extraction failed: {ex.Message}" });
    }
    finally
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        
        var endTime = DateTime.UtcNow;
        var finalMemory = GC.GetTotalMemory(false);
        var duration = endTime - startTime;
        Console.WriteLine($"[{endTime}] Extract endpoint completed - Duration: {duration.TotalSeconds:F2}s, Final Memory: {finalMemory / 1024 / 1024}MB, Memory Delta: {(finalMemory - initialMemory) / 1024 / 1024}MB");
    }
})
.DisableAntiforgery()
.RequireRateLimiting("extract")
.Produces(200, contentType: "application/octet-stream")
.Produces(400);

// Metadata endpoint - extract metadata without file
app.MapPost("/api/metadata", async (IFormFile image, IImageProcessor processor, CancellationToken cancellationToken) =>
{
    var startTime = DateTime.UtcNow;
    Console.WriteLine($"[{startTime}] Metadata endpoint accessed - Image: {image?.FileName}, Size: {image?.Length}");

    // Validate input
    var validationResult = ValidateUploadedImage(image!);
    if (validationResult != null)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Metadata validation failed: {validationResult}");
        return validationResult;
    }

    try
    {
        using var imageStream = image!.OpenReadStream();
        Console.WriteLine($"[{DateTime.UtcNow}] Extracting metadata...");
        var metadata = await processor.ExtractMetadataAsync(imageStream, cancellationToken);
        Console.WriteLine($"[{DateTime.UtcNow}] Metadata extracted - Signature: {metadata.Signature}, Size: {metadata.OriginalFileSize}, Name: {metadata.OriginalFileName}, Encrypted: {metadata.IsEncrypted}");

        return Results.Ok(new
        {
            signature = metadata.Signature,
            fileSize = metadata.OriginalFileSize,
            fileName = metadata.OriginalFileName,
            sha256 = Convert.ToHexString(metadata.Sha256Hash),
            isEncrypted = metadata.IsEncrypted
        });
    }
    catch (InvalidDataException ex)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Metadata endpoint - Invalid data: {ex.Message}");
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Metadata endpoint error: {ex.GetType().Name} - {ex.Message}");
        Console.WriteLine($"[{DateTime.UtcNow}] Stack trace: {ex.StackTrace}");
        return Results.BadRequest(new { error = $"Metadata extraction failed: {ex.Message}" });
    }
    finally
    {
        GC.Collect();
        var endTime = DateTime.UtcNow;
        Console.WriteLine($"[{endTime}] Metadata endpoint completed - Duration: {(endTime - startTime).TotalSeconds:F2}s");
    }
})
.RequireRateLimiting("extract")
.Produces(200)
.Produces(400);

app.Run();

// Helper method for file validation
static IResult? ValidateUploadedFile(IFormFile file)
{
    if (file == null || file.Length == 0)
        return Results.BadRequest(new { error = "No file uploaded" });
    
    if (file.Length > 10 * 1024 * 1024)
        return Results.BadRequest(new { error = "File too large. Maximum size is 10MB for server stability." });
    
    if (string.IsNullOrWhiteSpace(file.FileName) || file.FileName.Length > 255)
        return Results.BadRequest(new { error = "Invalid filename" });
        
    return null;
}

// Helper method for image validation
static IResult? ValidateUploadedImage(IFormFile image)
{
    if (image == null || image.Length == 0)
        return Results.BadRequest(new { error = "No image uploaded" });
    
    if (image.Length > 25 * 1024 * 1024)
        return Results.BadRequest(new { error = "Image too large. Maximum size is 25MB for server stability." });
    
    if (!image.ContentType.StartsWith("image/"))
        return Results.BadRequest(new { error = "Invalid file type. Please upload an image." });
        
    return null;
}