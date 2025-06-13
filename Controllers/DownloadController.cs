using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;
using System.IO.Compression;
using System.Text;

namespace BulbaLib.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DownloadController : ControllerBase
    {
        private readonly MySqlService _db;

        public DownloadController(MySqlService db)
        {
            _db = db;
        }

        // POST /api/download
        [HttpPost]
        public IActionResult DownloadChapters([FromBody] DownloadRequest req)
        {
            if (req.ChapterIds == null || req.ChapterIds.Length == 0)
                return BadRequest(new { error = "Неверный формат данных" });

            var novelTitle = string.IsNullOrWhiteSpace(req.NovelTitle) ? "главы" : req.NovelTitle.Replace('/', '-');
            var chapters = _db.GetChaptersByIds(req.ChapterIds);

            if (chapters == null || chapters.Count == 0)
                return NotFound(new { error = "Главы не найдены" });

            if (chapters.Count == 1)
            {
                var ch = chapters[0];
                var content = $"# Глава {ch.Number} – {ch.Title}\n\n{ch.Content}";
                var filename = $"Глава {ch.Number} – {ch.Title}.txt";
                var safeFilename = MakeSafeFilename(filename);

                return File(Encoding.UTF8.GetBytes(content), "text/plain", safeFilename);
            }
            else
            {
                using var zipStream = new MemoryStream();
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
                {
                    foreach (var ch in chapters)
                    {
                        var filename = $"Глава {ch.Number} – {ch.Title}.txt";
                        var safeFilename = MakeSafeFilename(filename);
                        var entry = archive.CreateEntry(safeFilename);
                        using var entryStream = entry.Open();
                        var content = $"# Глава {ch.Number} – {ch.Title}\n\n{ch.Content}";
                        var buffer = Encoding.UTF8.GetBytes(content);
                        entryStream.Write(buffer, 0, buffer.Length);
                    }
                }
                zipStream.Seek(0, SeekOrigin.Begin);
                var safeZipTitle = MakeSafeFilename($"{novelTitle}.zip");
                return File(zipStream.ToArray(), "application/zip", safeZipTitle);
            }
        }

        private string MakeSafeFilename(string filename)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(filename.Where(c => !invalid.Contains(c)).ToArray());
        }
    }

    public class DownloadRequest
    {
        public int[] ChapterIds { get; set; }
        public string NovelTitle { get; set; }
    }
}