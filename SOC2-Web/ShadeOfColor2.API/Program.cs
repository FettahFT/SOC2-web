using ShadeOfColor2.Core.Services;
using System.Net.Mime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

var builder = WebApplication.CreateBuilder(args);

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
            "https://soc2-web.netlify.app"
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

// Use the fallback CORS policy (allow all origins)
app.UseCors("AllowAll");
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
    Console.WriteLine($"[{DateTime.UtcNow}] Hide endpoint accessed - File: {file?.FileName}");
    
    // Validate input
    var validationResult = ValidateUploadedFile(file);
    if (validationResult != null)
        return validationResult;

    try
    {
        using var fileStream = file.OpenReadStream();
        var encodedImage = await processor.CreateCarrierImageAsync(
            fileStream, 
            Path.GetFileName(file.FileName)
        );

        using var outputStream = new MemoryStream();
        await encodedImage.SaveAsPngAsync(outputStream);
        
        // Generate random PNG name to hide original file type
        var randomName = $"image_{Guid.NewGuid().ToString("N")[..8]}.png";
        return Results.File(
            outputStream.ToArray(), 
            "image/png", 
            randomName
        );
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception)
    {
        return Results.Problem("An error occurred while processing the file");
    }
})
.DisableAntiforgery()
.RequireRateLimiting("hide")
.Produces(200, contentType: "image/png")
.Produces(400);

// Decode endpoint - extract file from image
app.MapPost("/api/extract", async (HttpContext context, IFormFile image, IImageProcessor processor) =>
{
    Console.WriteLine($"[{DateTime.UtcNow}] Extract endpoint accessed - Image: {image?.FileName}");
    
    // Validate input
    var validationResult = ValidateUploadedImage(image);
    if (validationResult != null)
        return validationResult;

    try
    {
        using var imageStream = image.OpenReadStream();
        var extractedFile = await processor.ExtractFileAsync(imageStream);
        
        // Use the original filename stored in the image (includes extension)
        var originalFileName = extractedFile.FileName;
        Console.WriteLine($"[{DateTime.UtcNow}] Extracted file: {originalFileName}");
        
        // Add custom header for reliable filename extraction
        context.Response.Headers.Add("X-Original-Filename", originalFileName);
        
        return Results.File(
            extractedFile.Data,
            "application/octet-stream",
            originalFileName
        );
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception)
    {
        return Results.Problem("An error occurred while extracting the file");
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