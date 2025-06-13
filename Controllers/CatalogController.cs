using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services;
using System.Collections.Generic;
using System.Linq;

namespace BulbaLib.Controllers
{
    [ApiController]
    [Route("api/catalog")]
    public class CatalogController : ControllerBase
    {
        private readonly MySqlService _db;

        public CatalogController(MySqlService db)
        {
            _db = db;
        }

        // GET /api/catalog?search=&sort=&genre=&tag=
        [HttpGet]
        public IActionResult GetCatalog(
            [FromQuery] string search = "",
            [FromQuery] string sort = "title_asc",
            [FromQuery] string genre = null,
            [FromQuery] string tag = null)
        {
            var novels = _db.GetNovels(search);

            // Фильтрация по жанру
            if (!string.IsNullOrWhiteSpace(genre))
                novels = novels.Where(n => (n.Genres ?? "").Split(',').Select(g => g.Trim()).Contains(genre)).ToList();

            // Фильтрация по тегу
            if (!string.IsNullOrWhiteSpace(tag))
                novels = novels.Where(n => (n.Tags ?? "").Split(',').Select(t => t.Trim()).Contains(tag)).ToList();

            // Сортировка
            switch (sort)
            {
                case "title_asc":
                    novels = novels.OrderBy(n => n.Title).ToList();
                    break;
                case "title_desc":
                    novels = novels.OrderByDescending(n => n.Title).ToList();
                    break;
            }

            return Ok(novels.Select(n => new {
                id = n.Id,
                title = n.Title,
                covers = n.CoversList,
                genres = n.Genres,
                tags = n.Tags,
                type = n.Type,
                format = n.Format,
                releaseYear = n.ReleaseYear,
                description = n.Description
            }));
        }

        // GET /api/catalog/genres — список всех уникальных жанров
        [HttpGet("genres")]
        public IActionResult GetGenres()
        {
            var genres = _db.GetAllGenres();
            return Ok(genres);
        }

        // GET /api/catalog/tags — список всех уникальных тегов
        [HttpGet("tags")]
        public IActionResult GetTags()
        {
            var tags = _db.GetAllTags();
            return Ok(tags);
        }
    }
}