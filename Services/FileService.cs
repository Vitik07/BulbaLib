using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BulbaLib.Services
{
    public class FileService
    {
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly string _uploadsBaseFolder = "uploads";
        private readonly string _coversFolder = "covers";

        public FileService(IWebHostEnvironment webHostEnvironment)
        {
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task<string> SaveNovelCoverAsync(IFormFile coverFile, int novelId)
        {
            if (coverFile == null || coverFile.Length == 0)
            {
                return null;
            }

            // Папка для обложек конкретной новеллы: wwwroot/uploads/covers/{novelId}
            var novelCoverDirectory = Path.Combine(_webHostEnvironment.WebRootPath, _uploadsBaseFolder, _coversFolder, novelId.ToString());

            if (!Directory.Exists(novelCoverDirectory))
            {
                Directory.CreateDirectory(novelCoverDirectory);
            }

            // Генерируем уникальное имя файла, чтобы избежать коллизий и проблем с кэшированием
            var fileExtension = Path.GetExtension(coverFile.FileName);
            var uniqueFileName = $"cover_{DateTime.UtcNow.Ticks}{fileExtension}";
            var filePath = Path.Combine(novelCoverDirectory, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await coverFile.CopyToAsync(stream);
            }

            // Возвращаем относительный путь для сохранения в БД
            return $"/{_uploadsBaseFolder}/{_coversFolder}/{novelId}/{uniqueFileName}";
        }

        public void DeleteFile(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;

            var fullPath = Path.Combine(_webHostEnvironment.WebRootPath, relativePath.TrimStart('/'));
            if (File.Exists(fullPath))
            {
                try
                {
                    File.Delete(fullPath);
                }
                catch (Exception ex)
                {
                    // Логирование ошибки удаления файла
                    Console.WriteLine($"Error deleting file {fullPath}: {ex.Message}");
                }
            }
        }
    }
}
