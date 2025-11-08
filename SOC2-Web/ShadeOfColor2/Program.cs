using System.Diagnostics;
using ShadeOfColor;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Health check
app.MapGet("/", () => Results.Ok(new { status = "ShadeOfColor2 API is running" }));

// Hide file endpoint
app.MapPost("/api/hide", async (IFormFile file) =>
{
    if (file == null || file.Length == 0)
        return Results.BadRequest(new { error = "No file uploaded" });

    try
    {
        var tempInputPath = Path.GetTempFileName();
        var tempOutputPath = Path.ChangeExtension(Path.GetTempFileName(), ".png");

        // Save uploaded file
        using (var stream = File.Create(tempInputPath))
        {
            await file.CopyToAsync(stream);
        }

        // Run ShadeOfColor2 console app
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"ShadeOfColor2.dll -crypt \"{tempInputPath}\" \"{tempOutputPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode == 0 && File.Exists(tempOutputPath))
        {
            var result = await File.ReadAllBytesAsync(tempOutputPath);
            File.Delete(tempInputPath);
            File.Delete(tempOutputPath);
            
            return Results.File(result, "image/png", $"image_{Guid.NewGuid().ToString("N")[..8]}.png");
        }
        else
        {
            var error = await process.StandardError.ReadToEndAsync();
            return Results.BadRequest(new { error = error });
        }
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// Extract file endpoint  
app.MapPost("/api/extract", async (IFormFile image) =>
{
    if (image == null || image.Length == 0)
        return Results.BadRequest(new { error = "No image uploaded" });

    try
    {
        var tempImagePath = Path.GetTempFileName();
        var tempOutputPath = Path.GetTempFileName();

        // Save uploaded image
        using (var stream = File.Create(tempImagePath))
        {
            await image.CopyToAsync(stream);
        }

        // Use FileToImage directly to get original filename
        try
        {
            var extractedPath = ShadeOfColor.FileToImage.DecryptImageToFile(tempImagePath, Path.GetTempPath());
            var result = await File.ReadAllBytesAsync(extractedPath);
            var originalFileName = Path.GetFileName(extractedPath);
            
            File.Delete(tempImagePath);
            File.Delete(extractedPath);
            
            return Results.File(result, "application/octet-stream", originalFileName);
        }
        catch
        {
            // Fallback to process method
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"ShadeOfColor2.dll -decrypt \"{tempImagePath}\" \"{tempOutputPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && File.Exists(tempOutputPath))
            {
                var result = await File.ReadAllBytesAsync(tempOutputPath);
                File.Delete(tempImagePath);
                File.Delete(tempOutputPath);
                
                return Results.File(result, "application/octet-stream", "extracted_file");
            }
        }
        else
        {
            var error = await process.StandardError.ReadToEndAsync();
            return Results.BadRequest(new { error = error });
        }
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();