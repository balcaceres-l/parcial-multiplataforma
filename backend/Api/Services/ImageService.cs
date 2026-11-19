namespace Api.Services;

public interface IImageService
{
    Task<string> SaveImageAsync(IFormFile file, string subFolder = "uploads");
    void DeleteImage(string? relativeUrl);
}

public class ImageService : IImageService
{
    private readonly IWebHostEnvironment _env;

    public ImageService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public async Task<string> SaveImageAsync(IFormFile file, string subFolder = "uploads")
    {
        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var folderPath = Path.Combine(webRoot, subFolder);
        Directory.CreateDirectory(folderPath);

        var extension = Path.GetExtension(file.FileName);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(folderPath, fileName);

        await using var stream = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(stream);

        return $"/{subFolder}/{fileName}";
    }

    public void DeleteImage(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)) return;

        var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var relativePath = relativeUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(webRoot, relativePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
}
