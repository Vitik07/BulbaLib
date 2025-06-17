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
        private readonly ILogger<FileService> _logger;

        public FileService(IWebHostEnvironment webHostEnvironment, ILogger<FileService> logger)
        {
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
        }

        public async Task<string> SaveNovelCoverAsync(IFormFile coverFile, int novelId)
        {
            if (coverFile == null)
            {
                _logger.LogWarning("[SaveNovelCoverAsync] coverFile is null. NovelId: {NovelId}", novelId);
                return null;
            }
            if (coverFile.Length == 0)
            {
                _logger.LogWarning("[SaveNovelCoverAsync] coverFile is empty (Length is 0). FileName: {FileName}, NovelId: {NovelId}", coverFile.FileName, novelId);
                return null;
            }

            _logger.LogInformation("[SaveNovelCoverAsync] Attempting to save cover. FileName: {FileName}, Length: {Length}, NovelId: {NovelId}", coverFile.FileName, coverFile.Length, novelId);

            // Папка для обложек конкретной новеллы: wwwroot/uploads/covers/{novelId}
            var novelCoverDirectory = Path.Combine(_webHostEnvironment.WebRootPath, _uploadsBaseFolder, _coversFolder, novelId.ToString());
            _logger.LogInformation("[SaveNovelCoverAsync] Target directory for novel cover: {DirectoryPath}", novelCoverDirectory);

            try
            {
                if (!Directory.Exists(novelCoverDirectory))
                {
                    _logger.LogInformation("[SaveNovelCoverAsync] Directory does not exist, attempting to create: {DirectoryPath}", novelCoverDirectory);
                    Directory.CreateDirectory(novelCoverDirectory);
                    _logger.LogInformation("[SaveNovelCoverAsync] Directory creation result for {DirectoryPath}: Exists = {Exists}", novelCoverDirectory, Directory.Exists(novelCoverDirectory));
                }
                else
                {
                    _logger.LogInformation("[SaveNovelCoverAsync] Directory already exists: {DirectoryPath}", novelCoverDirectory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SaveNovelCoverAsync] Exception during directory creation for {DirectoryPath}. NovelId: {NovelId}", novelCoverDirectory, novelId);
                return null; // Cannot proceed if directory cannot be created/accessed
            }


            // Генерируем уникальное имя файла, чтобы избежать коллизий и проблем с кэшированием
            var fileExtension = Path.GetExtension(coverFile.FileName);
            var uniqueFileName = $"cover_{DateTime.UtcNow.Ticks}{fileExtension}";
            var filePath = Path.Combine(novelCoverDirectory, uniqueFileName);
            _logger.LogInformation("[SaveNovelCoverAsync] Full file path for saving: {FilePath}", filePath);

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await coverFile.CopyToAsync(stream);
                }
                _logger.LogInformation("[SaveNovelCoverAsync] File successfully saved: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SaveNovelCoverAsync] Exception during CopyToAsync for file {FilePath}. NovelId: {NovelId}, FileName: {FileName}", filePath, novelId, coverFile.FileName);
                return null; // Return null if file saving fails
            }

            // Возвращаем относительный путь для сохранения в БД
            var relativePathToSave = $"/{_uploadsBaseFolder}/{_coversFolder}/{novelId}/{uniqueFileName}";
            _logger.LogInformation("[SaveNovelCoverAsync] Successfully saved cover. Returning relative path: {RelativePath}", relativePathToSave);
            return relativePathToSave;
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
