using ShadeOfColor2.Core.Services;
using System.Net.Mime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

// Configure request size limits
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 52428800; // 50MB
});

builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 52428800; // 50MB
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

// Add error handling middleware
app.UseExceptionHandler("/error");
app.Map("/error", () => Results.Problem("An internal server error occurred."));
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
app.MapPost("/api/hide", async (IFormFile file, IImageProcessor processor) =>
{
    var startTime = DateTime.UtcNow;
    var initialMemory = GC.GetTotalMemory(false);
    Console.WriteLine($"[{startTime}] Hide endpoint accessed - File: {file?.FileName}, Initial Memory: {initialMemory / 1024 / 1024}MB");
    
    // Validate input
    var validationResult = ValidateUploadedFile(file);
    if (validationResult != null)
        return validationResult;
        
    // Check available memory before processing large files
    var availableMemory = GC.GetTotalMemory(false);
    if (file.Length > 20 * 1024 * 1024 && availableMemory > 800 * 1024 * 1024) // 20MB file, 800MB memory
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        return Results.BadRequest(new { error = "Server memory pressure detected. Please try again in a moment." });
    }

    try
    {
        using var fileStream = file.OpenReadStream();
        using var encodedImage = await processor.CreateCarrierImageAsync(
            fileStream, 
            Path.GetFileName(file.FileName)
        );

        // Generate random PNG name to hide original file type
        var randomName = $"image_{Guid.NewGuid().ToString("N")[..8]}.png";
        
        // Stream response directly without loading into memory
        return Results.Stream(
            async stream =>
            {
                await encodedImage.SaveAsPngAsync(stream);
            },
            "image/png",
            randomName
        );
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Hide endpoint error: {ex.Message}");
        return Results.Problem("An error occurred while processing the file");
    }
    finally
    {
        var endTime = DateTime.UtcNow;
        var finalMemory = GC.GetTotalMemory(true);
        var duration = endTime - startTime;
        Console.WriteLine($"[{endTime}] Hide endpoint completed - Duration: {duration.TotalSeconds:F2}s, Final Memory: {finalMemory / 1024 / 1024}MB, Memory Delta: {(finalMemory - initialMemory) / 1024 / 1024}MB");
    }
})
.DisableAntiforgery()
.RequireRateLimiting("hide")
.Produces(200, contentType: "image/png")
.Produces(400);

// Decode endpoint - extract file from image
app.MapPost("/api/extract", async (HttpContext context, IFormFile image, IImageProcessor processor) =>
{
    var startTime = DateTime.UtcNow;
    var initialMemory = GC.GetTotalMemory(false);
    Console.WriteLine($"[{startTime}] Extract endpoint accessed - Image: {image?.FileName}, Initial Memory: {initialMemory / 1024 / 1024}MB");
    
    // Validate input
    var validationResult = ValidateUploadedImage(image);
    if (validationResult != null)
        return validationResult;
        
    // Check available memory before processing large images
    var availableMemory = GC.GetTotalMemory(false);
    if (image.Length > 50 * 1024 * 1024 && availableMemory > 800 * 1024 * 1024) // 50MB image, 800MB memory
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        return Results.BadRequest(new { error = "Server memory pressure detected. Please try again in a moment." });
    }

    try
    {
        using var imageStream = image.OpenReadStream();
        var extractedFile = await processor.ExtractFileAsync(imageStream);
        
        // Use the original filename stored in the image (includes extension)
        var originalFileName = extractedFile.FileName;
        Console.WriteLine($"[{DateTime.UtcNow}] Extracted file: {originalFileName}, Size: {extractedFile.Data.Length / 1024 / 1024}MB");
        
        // Add custom header for reliable filename extraction
        context.Response.Headers["X-Original-Filename"] = originalFileName;
        
        // Stream response for large files
        return Results.Stream(
            async stream =>
            {
                await stream.WriteAsync(extractedFile.Data);
            },
            "application/octet-stream",
            originalFileName
        );
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] Extract endpoint error: {ex.Message}");
        return Results.Problem("An error occurred while extracting the file");
    }
    finally
    {
        var endTime = DateTime.UtcNow;
        var finalMemory = GC.GetTotalMemory(true);
        var duration = endTime - startTime;
        Console.WriteLine($"[{endTime}] Extract endpoint completed - Duration: {duration.TotalSeconds:F2}s, Final Memory: {finalMemory / 1024 / 1024}MB, Memory Delta: {(finalMemory - initialMemory) / 1024 / 1024}MB");
    }
})
.DisableAntiforgery()
.RequireRateLimiting("extract")
.Produces(200, contentType: "application/octet-stream")
.Produces(400);

app.Run();

// Helper method for file validation
static IResult? ValidateUploadedFile(IFormFile file)
{
    if (file == null || file.Length == 0)
        return Results.BadRequest(new { error = "No file uploaded" });
    
    if (file.Length > 50 * 1024 * 1024)
        return Results.BadRequest(new { error = "File too large. Maximum size is 50MB." });
    
    if (string.IsNullOrWhiteSpace(file.FileName) || file.FileName.Length > 255)
        return Results.BadRequest(new { error = "Invalid filename" });
        
    return null;
}

// Helper method for image validation
static IResult? ValidateUploadedImage(IFormFile image)
{
    if (image == null || image.Length == 0)
        return Results.BadRequest(new { error = "No image uploaded" });
    
    if (image.Length > 100 * 1024 * 1024)
        return Results.BadRequest(new { error = "Image too large. Maximum size is 100MB." });
    
    if (!image.ContentType.StartsWith("image/"))
        return Results.BadRequest(new { error = "Invalid file type. Please upload an image." });
        
    return null;
}