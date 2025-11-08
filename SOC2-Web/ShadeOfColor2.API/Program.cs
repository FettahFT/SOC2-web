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

// Serve static files (frontend)
app.UseStaticFiles();

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

// Handle OPTIONS for CORS preflight
app.MapMethods("/api/hide", new[] { "OPTIONS" }, () => Results.Ok());
app.MapMethods("/api/extract", new[] { "OPTIONS" }, () => Results.Ok());
app.MapMethods("/api/metadata", new[] { "OPTIONS" }, () => Results.Ok());

// Serve SPA fallback
app.MapFallbackToFile("index.html");

app.Run();

// Helper method for file validation
static string? ValidateUploadedFile(IFormFile file)
{
    if (file == null || file.Length == 0)
        return "No file uploaded";

    if (file.Length > 10 * 1024 * 1024)
        return "File too large. Maximum size is 10MB for server stability.";

    if (string.IsNullOrWhiteSpace(file.FileName) || file.FileName.Length > 255)
        return "Invalid filename";

    return null;
}

// Helper method for image validation
static string? ValidateUploadedImage(IFormFile image)
{
    if (image == null || image.Length == 0)
        return "No image uploaded";

    if (image.Length > 25 * 1024 * 1024)
        return "Image too large. Maximum size is 25MB for server stability.";

    if (!image.ContentType.StartsWith("image/"))
        return "Invalid file type. Please upload an image.";

    return null;
}