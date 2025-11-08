using ShadeOfColor2.Core.Services;
using System.Net.Mime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

var builder = WebApplication.CreateBuilder(args);

// Register services
builder.Services.AddSingleton<StreamingConfiguration>(new StreamingConfiguration());
builder.Services.AddSingleton<ITrueStreamingImageProcessor, TrueStreamingImageProcessor>();
builder.Services.AddSingleton<IImageProcessor>(provider => provider.GetRequiredService<ITrueStreamingImageProcessor>());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAntiforgery();

// Configure request size limits
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 100 * 1024 * 1024; // 100MB
    options.ValueLengthLimit = 100 * 1024 * 1024;
    options.MemoryBufferThreshold = 2 * 1024 * 1024; // 2MB buffer
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 100 * 1024 * 1024; // 100MB
});

// Add rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("hide", context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));
    
    options.AddPolicy("extract", context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1)
            }));
    
    options.AddPolicy("health", context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            }));
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

// Global error handling for streaming
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        context.Response.StatusCode = 499; // Client closed request
    }
    catch (TimeoutException)
    {
        context.Response.StatusCode = 408; // Request timeout
        await context.Response.WriteAsync("Request timeout during file processing");
    }
    catch (OutOfMemoryException)
    {
        MemoryMonitor.ForceCleanup();
        context.Response.StatusCode = 507; // Insufficient storage
        await context.Response.WriteAsync("Insufficient memory to process file");
    }
});

app.UseAntiforgery();

// Health check
app.MapGet("/", () => 
{
    Console.WriteLine($"[{DateTime.UtcNow}] Health check accessed");
    return Results.Ok(new { status = "ShadeOfColor2 API is running" });
})
.RequireRateLimiting("health");

// Streaming health check
app.MapGet("/health/streaming", () =>
{
    var metrics = StreamingMetrics.GetMetrics();
    var memoryUsage = MemoryMonitor.GetCurrentMemoryUsage();
    var isMemoryHigh = MemoryMonitor.IsMemoryPressureHigh();
    
    return Results.Ok(new 
    {
        status = "healthy",
        streaming = new
        {
            requests = metrics.Streaming,
            fallbacks = metrics.Fallback,
            errors = metrics.Errors,
            successRate = metrics.Streaming > 0 ? (double)(metrics.Streaming - metrics.Errors) / metrics.Streaming : 1.0
        },
        memory = new
        {
            currentBytes = memoryUsage,
            currentMB = memoryUsage / (1024 * 1024),
            highPressure = isMemoryHigh
        }
    });
})
.RequireRateLimiting("health");

// Configuration endpoint
app.MapGet("/config/streaming", (StreamingConfiguration config) =>
{
    return Results.Ok(new
    {
        streamingEnabled = config.EnableStreaming,
        thresholdMB = config.StreamingThresholdBytes / (1024 * 1024),
        maxFileSizeMB = config.MaxStreamingFileSize / (1024 * 1024),
        timeoutSeconds = config.StreamTimeoutSeconds,
        fallbackEnabled = config.EnableFallback,
        maxConcurrentStreams = config.MaxConcurrentStreams
    });
})
.RequireRateLimiting("health");

// Encode endpoint - hide file in image
app.MapPost("/api/hide", async (IFormFile file, ITrueStreamingImageProcessor processor) =>
{
    Console.WriteLine($"[{DateTime.UtcNow}] Hide endpoint accessed - File: {file?.FileName}");
    
    // Check memory before processing
    try
    {
        MemoryMonitor.ThrowIfMemoryCritical();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 507);
    }
    
    // Validate input
    var validationResult = ValidateUploadedFile(file);
    if (validationResult != null)
        return validationResult;

    try
    {
        // Generate random PNG name to hide original file type
        var randomName = $"image_{Guid.NewGuid().ToString("N")[..8]}.png";
        
        // True streaming - direct to HTTP response
        return Results.Stream(
            async (outputStream, cancellationToken) =>
            {
                using var fileStream = file.OpenReadStream();
                await processor.CreateCarrierStreamAsync(
                    fileStream, 
                    Path.GetFileName(file.FileName),
                    outputStream,
                    cancellationToken
                );
                
                // Minimal cleanup
                GC.Collect(0, GCCollectionMode.Optimized);
            },
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
    
    // Check memory before processing
    try
    {
        MemoryMonitor.ThrowIfMemoryCritical();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(ex.Message, statusCode: 507);
    }
    
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
        context.Response.Headers["X-Original-Filename"] = originalFileName;
        
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