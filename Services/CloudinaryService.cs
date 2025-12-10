using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace CSE325_visioncoders.Services
{
    public interface ICloudinaryService
    {
        Task<string> UploadAsync(Stream stream, string fileName, CancellationToken ct = default);
    }

    public class CloudinaryService : ICloudinaryService
    {
        private readonly Cloudinary _cloudinary;

        public CloudinaryService()
        {
            var url = Environment.GetEnvironmentVariable("CLOUDINARY_URL");
            if (string.IsNullOrWhiteSpace(url))
                throw new InvalidOperationException("CLOUDINARY_URL no está configurado.");
            _cloudinary = new Cloudinary(url);
            _cloudinary.Api.Secure = true;
        }

        public async Task<string> UploadAsync(Stream stream, string fileName, CancellationToken ct = default)
        {
            var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
                throw new InvalidOperationException("Extensión no permitida.");

            var publicId = $"meals/{Guid.NewGuid():N}";
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, stream),
                PublicId = publicId,
                Overwrite = false
            };

            var result = await _cloudinary.UploadAsync(uploadParams, ct);
            if (result.StatusCode != HttpStatusCode.OK || result.Error != null)
                throw new Exception(result.Error?.Message ?? "Cloudinary upload failed");

            var publicUrl = result.SecureUrl?.ToString();
            if (string.IsNullOrEmpty(publicUrl))
                throw new Exception("No se obtuvo URL pública.");
                
            return publicUrl;
        }
    }
}