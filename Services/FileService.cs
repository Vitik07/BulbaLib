using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions; // Added
using System.Threading.Tasks;

namespace BulbaLib.Services
{
    public class FileService
    {
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly string _uploadsBaseFolder = "uploads";
        private readonly string _coversFolder = "covers";
        private readonly string _tempCoversFolder = "temp_covers"; // Added
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

        public async Task<string> SaveTempNovelCoverAsync(IFormFile file, string subFolder = "temp_covers")
        {
            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("[SaveTempNovelCoverAsync] File is null or empty. Subfolder: {Subfolder}", subFolder);
                return null;
            }

            _logger.LogInformation("[SaveTempNovelCoverAsync] Attempting to save temp cover. FileName: {FileName}, Length: {Length}, Subfolder: {Subfolder}", file.FileName, file.Length, subFolder);

            var tempDirectory = Path.Combine(_webHostEnvironment.WebRootPath, _uploadsBaseFolder, subFolder);
            _logger.LogInformation("[SaveTempNovelCoverAsync] Target directory for temp cover: {DirectoryPath}", tempDirectory);

            try
            {
                if (!Directory.Exists(tempDirectory))
                {
                    _logger.LogInformation("[SaveTempNovelCoverAsync] Directory does not exist, attempting to create: {DirectoryPath}", tempDirectory);
                    Directory.CreateDirectory(tempDirectory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SaveTempNovelCoverAsync] Exception during directory creation for {DirectoryPath}.", tempDirectory);
                return null;
            }

            var fileExtension = Path.GetExtension(file.FileName);
            var uniqueFileName = $"temp_{Guid.NewGuid()}{fileExtension}"; // Ensure more uniqueness for temp files
            var filePath = Path.Combine(tempDirectory, uniqueFileName);
            _logger.LogInformation("[SaveTempNovelCoverAsync] Full file path for saving: {FilePath}", filePath);

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
                _logger.LogInformation("[SaveTempNovelCoverAsync] Temp file successfully saved: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SaveTempNovelCoverAsync] Exception during CopyToAsync for file {FilePath}. FileName: {FileName}", filePath, file.FileName);
                return null;
            }

            var relativePathToSave = $"/{_uploadsBaseFolder}/{subFolder}/{uniqueFileName}";
            _logger.LogInformation("[SaveTempNovelCoverAsync] Successfully saved temp cover. Returning relative path: {RelativePath}", relativePathToSave);
            return relativePathToSave;
        }

        public async Task<string> CommitTempCoverAsync(string tempRelativePath, int novelId, string coverFileName = null)
        {
            if (string.IsNullOrEmpty(tempRelativePath))
            {
                _logger.LogWarning("[CommitTempCoverAsync] tempRelativePath is null or empty. NovelId: {NovelId}", novelId);
                return null;
            }

            _logger.LogInformation("[CommitTempCoverAsync] Attempting to commit temp cover. TempPath: {TempPath}, NovelId: {NovelId}, FileName: {CoverFileName}", tempRelativePath, novelId, coverFileName);

            string webRootPath = _webHostEnvironment.WebRootPath;
            string fullTempPath = Path.Combine(webRootPath, tempRelativePath.TrimStart('/'));

            if (!File.Exists(fullTempPath))
            {
                _logger.LogWarning("[CommitTempCoverAsync] Temp file does not exist at {FullTempPath}. NovelId: {NovelId}", fullTempPath, novelId);
                return null;
            }

            var novelCoverDirectory = Path.Combine(webRootPath, _uploadsBaseFolder, _coversFolder, novelId.ToString());
            _logger.LogInformation("[CommitTempCoverAsync] Target directory for final cover: {DirectoryPath}", novelCoverDirectory);

            try
            {
                if (!Directory.Exists(novelCoverDirectory))
                {
                    _logger.LogInformation("[CommitTempCoverAsync] Directory does not exist, attempting to create: {DirectoryPath}", novelCoverDirectory);
                    Directory.CreateDirectory(novelCoverDirectory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CommitTempCoverAsync] Exception during final directory creation for {DirectoryPath}. NovelId: {NovelId}", novelCoverDirectory, novelId);
                return null;
            }

            var finalFileName = string.IsNullOrEmpty(coverFileName) ? Path.GetFileName(fullTempPath) : coverFileName;
            // Ensure unique name in final destination as well, or decide on a strategy (e.g., if coverFileName is specific)
            if (string.IsNullOrEmpty(coverFileName)) // If no specific name, ensure uniqueness
            {
                finalFileName = $"cover_{DateTime.UtcNow.Ticks}{Path.GetExtension(fullTempPath)}";
            }
            var finalFilePath = Path.Combine(novelCoverDirectory, finalFileName);
            _logger.LogInformation("[CommitTempCoverAsync] Full file path for final cover: {FilePath}", finalFilePath);

            try
            {
                File.Move(fullTempPath, finalFilePath); // Move the file
                _logger.LogInformation("[CommitTempCoverAsync] File successfully moved from {FullTempPath} to {FinalFilePath}", fullTempPath, finalFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CommitTempCoverAsync] Exception during File.Move from {FullTempPath} to {FinalFilePath}. NovelId: {NovelId}", fullTempPath, finalFilePath, novelId);
                // Attempt to delete temp file if move fails and it still exists? Or leave it for manual cleanup?
                // For now, just return null.
                return null;
            }

            var relativePathToSave = $"/{_uploadsBaseFolder}/{_coversFolder}/{novelId}/{finalFileName}";
            _logger.LogInformation("[CommitTempCoverAsync] Successfully committed cover. Returning relative path: {RelativePath}", relativePathToSave);
            return relativePathToSave;
        }

        public async Task DeleteCoverAsync(string relativePath)
        {
            _logger.LogInformation("[DeleteCoverAsync] Attempting to delete cover at relative path: {RelativePath}", relativePath);
            if (string.IsNullOrEmpty(relativePath))
            {
                _logger.LogWarning("[DeleteCoverAsync] Relative path is null or empty. No action taken.");
                return;
            }

            var fullPath = Path.Combine(_webHostEnvironment.WebRootPath, relativePath.TrimStart('/'));
            if (File.Exists(fullPath))
            {
                try
                {
                    File.Delete(fullPath);
                    _logger.LogInformation("[DeleteCoverAsync] File successfully deleted: {FullPath}", fullPath);
                }
                catch (IOException ex)
                {
                    _logger.LogError(ex, "[DeleteCoverAsync] IOException occurred while deleting file {FullPath}.", fullPath);
                    // Optionally, rethrow or handle specific scenarios like file in use
                }
            }
            else
            {
                _logger.LogWarning("[DeleteCoverAsync] File not found at {FullPath}. No action taken.", fullPath);
            }
            // Added await Task.CompletedTask for methods not having any await call, to satisfy async
            // but File.Delete is synchronous, so this is effectively a synchronous method marked async.
            // If true async file operations were available and needed, they would be used.
            await Task.CompletedTask;
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

        public async Task<string> SaveChapterContentAsync(int novelId, string chapterNumber, string chapterTitle, string textContent)
        {
            // Get base path for wwwroot/uploads/content
            var contentUploadsDir = Path.Combine(_webHostEnvironment.WebRootPath, _uploadsBaseFolder, "content");
            var novelContentDir = Path.Combine(contentUploadsDir, novelId.ToString());

            // Create directory for the novel's content if it doesn't exist
            if (!Directory.Exists(novelContentDir))
            {
                Directory.CreateDirectory(novelContentDir);
            }

            // Sanitize chapterNumber and chapterTitle for filename
            // Remove invalid file name characters. Replace sequences of invalid chars with a single underscore.
            string invalidCharsRegex = string.Format(@"([{0}]*\.+$)|([{0}]+)", Regex.Escape(new string(Path.GetInvalidFileNameChars())));
            string sanitizedNumber = string.IsNullOrWhiteSpace(chapterNumber) ? "Chapter" : Regex.Replace(chapterNumber, invalidCharsRegex, "_");
            string sanitizedTitle = string.IsNullOrWhiteSpace(chapterTitle) ? "Untitled" : Regex.Replace(chapterTitle, invalidCharsRegex, "_");

            // Truncate parts if too long to avoid overly long filenames
            const int maxPartLength = 50; // Max length for number part and title part
            sanitizedNumber = sanitizedNumber.Length > maxPartLength ? sanitizedNumber.Substring(0, maxPartLength).Trim() : sanitizedNumber.Trim();
            sanitizedTitle = sanitizedTitle.Length > maxPartLength ? sanitizedTitle.Substring(0, maxPartLength).Trim() : sanitizedTitle.Trim();

            // Refined filename logic
            string fileName;
            bool numIsEmpty = string.IsNullOrWhiteSpace(chapterNumber) || sanitizedNumber == "_" || sanitizedNumber.Equals("Chapter", StringComparison.OrdinalIgnoreCase);
            bool titleIsEmpty = string.IsNullOrWhiteSpace(chapterTitle) || sanitizedTitle == "_" || sanitizedTitle.Equals("Untitled", StringComparison.OrdinalIgnoreCase);

            if (numIsEmpty && titleIsEmpty)
            {
                fileName = "Глава без номера и названия.txt";
            }
            else if (titleIsEmpty)
            {
                fileName = $"{sanitizedNumber}.txt";
            }
            else if (numIsEmpty) // Number is "Chapter" or empty, but title is present
            {
                fileName = $"{sanitizedTitle}.txt"; // Prioritize title if number is generic/empty
            }
            else
            {
                fileName = $"{sanitizedNumber} - {sanitizedTitle}.txt";
            }

            // Final sanitization pass on the whole filename just in case, though previous steps should handle most.
            fileName = Regex.Replace(fileName, invalidCharsRegex, "_");
            // Ensure it doesn't start or end with problematic characters like underscore or space after previous logic.
            fileName = fileName.Trim().Trim(new[] { '_', '.' });
            if (string.IsNullOrWhiteSpace(fileName) || fileName.Equals(".txt", StringComparison.OrdinalIgnoreCase)) // if all was stripped
            {
                fileName = "DefaultChapterName.txt"; // Ultimate fallback
            }
            else if (!fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".txt";
            }


            string filePath = Path.Combine(novelContentDir, fileName);

            _logger.LogInformation("[SaveChapterContentAsync] Input - NovelId: {NovelId}, ChapterNumber: '{ChapterNumber}', ChapterTitle: '{ChapterTitle}'", novelId, chapterNumber, chapterTitle);
            _logger.LogInformation("[SaveChapterContentAsync] Generated fileName: '{GeneratedFileName}', Full filePath: '{FullFilePath}'", fileName, filePath);

            // Save the textContent to the file
            try
            {
                await File.WriteAllTextAsync(filePath, textContent);
                _logger.LogInformation("[SaveChapterContentAsync] Successfully wrote content to: {FilePath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SaveChapterContentAsync] Error writing chapter content to file: {FilePath}", filePath);
                return null; // Or throw, depending on desired error handling
            }

            // Return the relative web path
            var relativeWebPath = $"/{_uploadsBaseFolder}/content/{novelId}/{fileName}";
            _logger.LogInformation("[SaveChapterContentAsync] Returning relative web path: '{RelativeWebPath}'", relativeWebPath);
            return relativeWebPath;
        }

        public async Task<string> ReadChapterContentAsync(string relativeFilePath)
        {
            if (string.IsNullOrEmpty(relativeFilePath))
            {
                _logger.LogWarning("[ReadChapterContentAsync] Input relativeFilePath is null or empty.");
                return null;
            }

            var fullPath = Path.Combine(_webHostEnvironment.WebRootPath, relativeFilePath.TrimStart('/'));
            _logger.LogInformation("[ReadChapterContentAsync] Attempting to read content from fullPath: {FullPath}", fullPath);

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("[ReadChapterContentAsync] File not found at: {FullPath}", fullPath);
                return null;
            }

            try
            {
                var content = await File.ReadAllTextAsync(fullPath);
                _logger.LogInformation("[ReadChapterContentAsync] Successfully read content from: {FullPath}. Content length: {ContentLength}", fullPath, content?.Length ?? 0);
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ReadChapterContentAsync] Error reading file content from: {FullPath}", fullPath);
                return null;
            }
        }

        public async Task<bool> DeleteChapterContentAsync(string relativeFilePath)
        {
            if (string.IsNullOrEmpty(relativeFilePath))
            {
                _logger.LogWarning("[DeleteChapterContentAsync] Input relativeFilePath is null or empty.");
                return false;
            }

            var fullPath = Path.Combine(_webHostEnvironment.WebRootPath, relativeFilePath.TrimStart('/'));
            _logger.LogInformation("[DeleteChapterContentAsync] Attempting to delete file at fullPath: {FullPath}", fullPath);

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("[DeleteChapterContentAsync] File not found at: {FullPath}. Cannot delete.", fullPath);
                return false;
            }

            try
            {
                File.Delete(fullPath); // Synchronous, but often acceptable. Async version not standard.
                _logger.LogInformation("[DeleteChapterContentAsync] File successfully deleted: {FullPath}", fullPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DeleteChapterContentAsync] Error deleting file: {FullPath}", fullPath);
                return false;
            }
            // To make it truly async if File.Delete had an async counterpart, it would be:
            // await Task.Run(() => File.Delete(fullPath)); 
            // However, for this operation, the overhead of Task.Run might not be worth it unless contention is high.
        }

        public bool ChapterFileExists(string relativeFilePath)
        {
            if (string.IsNullOrEmpty(relativeFilePath))
            {
                _logger.LogWarning("[ChapterFileExists] Relative file path is null or empty.");
                return false;
            }

            // Ensure relativeFilePath does not lead to path traversal issues by normalizing.
            // Path.Combine correctly handles combining paths, and TrimStart('/') ensures it's treated as relative to WebRootPath.
            var safeRelativePath = relativeFilePath.TrimStart('/');
            // Further checks can be added here if paths are user-generated and complex. For now, assume it's system-generated or validated elsewhere.

            var fullPath = Path.Combine(_webHostEnvironment.WebRootPath, safeRelativePath);
            _logger.LogInformation("[ChapterFileExists] Checking for file existence at physical path: {FullPath}", fullPath);

            bool exists = File.Exists(fullPath);
            _logger.LogInformation("[ChapterFileExists] File at {FullPath} exists: {Exists}", fullPath, exists);

            return exists;
        }
    }
}
