using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services; // Added
using BulbaLib.Models;   // Added
using System.Diagnostics; // Added for Debug.WriteLine
using System.Security.Claims; // Added
using Microsoft.AspNetCore.Authorization; // Added
using System; // For DateTimeOffset, DateTime
using System.Text.Json; // For JsonSerializer
using System.Linq; // Added for LINQ operations

namespace BulbaLib.Controllers
{
    public class NovelViewController : Controller
    {
        private static readonly List<string> AllGenres = new List<string>
        {
            "Арт", "Безумие", "Боевик", "Боевые искусства", "Вампиры", "Военное", "Гарем", "Гендерная интрига",
            "Героическое фентези", "Демоны", "Детектив", "Дзёсэй", "Драма", "Игра", "Исекай", "История",
            "Киберпанк", "Кодомо", "Комедия", "Космос", "Магия", "Махо-сёдзё", "Машины", "Меха", "Мистика",
            "Музыка", "Научная фантастика", "Омегаверс", "Пародия", "Повседневность", "Постапокалиптика",
            "Приключения", "Психология", "Романтика", "Самурайский боевик", "Сверхъестественное", "Сёдзё",
            "Сёдзё-ай", "Сёнэн", "Сёнэн-ай", "Спорт", "Супер сила", "Сэйнэн", "Трагедия", "Триллер", "Ужасы",
            "Фантастика", "Фэнтези", "Школа", "Эротика", "Этти", "Юри"
        };

        private static readonly List<string> AllTags = new List<string>
        {
            "Авантюристы", "Антигерой", "Бессмертные", "Боги", "Борьба за власть", "Брат и сестра", "Ведьма",
            "Видеоигры", "Виртуальная реальность", "Владыка демонов", "Военные", "Воспоминания из другого мира",
            "Выживание", "ГГ женщина", "ГГ имба", "ГГ мужчина", "ГГ не ояш", "ГГ не человек", "ГГ ояш",
            "Главный герой бог", "Глупый ГГ", "Горничные", "Гуро", "Гяру", "Демоны", "Драконы", "Древний мир",
            "Зверолюди", "Зомби", "Исторические", "Исторические фигуры", "Космос", "Кулинария", "Культивирование",
            "ЛитРПГ", "Лоли", "Магия", "Машинный перевод", "Медицина", "Межгалактическая война", "Монстр девушки",
            "Монстродевушки", "Монстры", "Мрачный мир", "Мурим", "Нетораре", "Ниндзя", "Обратный гарем",
            "Офисные работники", "Пираты", "Подземелья", "Политика", "Полиция", "Полностью CGI", "Полноцветный",
            "Преступники / Криминал", "Призраки / Духи", "Призыватели", "Прыжки между мирами",
            "Путешествие в другой мир", "Путешествие во времени", "Рабы", "Ранги силы", "Регрессия", "Реинкарнация",
            "Самураи", "Сёдзё-ай", "Сёнен-ай", "Скрытие личности", "Спортивное тело",
            "Средневековье", "Традиционные игры", "Умный ГГ", "Фермерство", "Характерный рост", "Хикимори",
            "Эволюция", "Элементы РПГ", "Эльфы", "Юри", "Якудза", "Яой"
        };

        private readonly MySqlService _mySqlService;
        private readonly PermissionService _permissionService;
        private readonly ICurrentUserService _currentUserService;
        private readonly FileService _fileService;

        public NovelViewController(MySqlService mySqlService, PermissionService permissionService, ICurrentUserService currentUserService, FileService fileService)
        {
            _mySqlService = mySqlService;
            _permissionService = permissionService;
            _currentUserService = currentUserService;
            _fileService = fileService;
        }

        [HttpGet("/novel/{id:int}")]
        public IActionResult Details(int id)
        {
            var novel = _mySqlService.GetNovel(id);
            if (novel == null) return NotFound();

            var currentUser = _currentUserService.GetCurrentUser();
            ViewData["CanEditNovel"] = currentUser != null && _permissionService.CanEditNovel(currentUser, novel);
            ViewData["CanDeleteNovel"] = currentUser != null && _permissionService.CanDeleteNovel(currentUser, novel);

            bool canAddChapter = false;
            if (currentUser != null && novel != null)
            {
                canAddChapter = _permissionService.CanAddChapterDirectly(currentUser) || _permissionService.CanSubmitChapterForModeration(currentUser, novel);
            }
            ViewData["CanAddChapter"] = canAddChapter;
            ViewData["NovelId"] = novel.Id;

            List<Chapter> chaptersFromDb = _mySqlService.GetChaptersByNovel(id);
            var chapterViewModels = new List<ChapterViewModel>();
            if (chaptersFromDb != null)
            {
                foreach (var chapter in chaptersFromDb)
                {
                    bool canEditChapter = false;
                    bool canDeleteChapter = false;
                    if (currentUser != null)
                    {
                        canEditChapter = _permissionService.CanEditChapter(currentUser, chapter, novel);
                        canDeleteChapter = _permissionService.CanDeleteChapter(currentUser, chapter, novel);
                    }
                    chapterViewModels.Add(new ChapterViewModel { Chapter = chapter, CanEdit = canEditChapter, CanDelete = canDeleteChapter });
                }
            }
            ViewData["ChapterViewModels"] = chapterViewModels;
            return View("~/Views/Novel/Novel.cshtml", novel);
        }

        [Authorize(Roles = "Admin,Author")]
        [HttpGet]
        public IActionResult Create()
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null || !(_permissionService.CanAddNovelDirectly(currentUser) || _permissionService.CanSubmitNovelForModeration(currentUser)))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }
            ViewData["AllGenres"] = AllGenres;
            ViewData["AllTags"] = AllTags;
            return View("~/Views/Novel/Create.cshtml", new NovelCreateModel());
        }

        [Authorize(Roles = "Admin,Author")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(NovelCreateModel model)
        {
            Debug.WriteLine($"Received NovelCreateModel: Title='{model.Title}', Description='{model.Description}', AuthorId='{model.AuthorId}', CoverFile='{(model.CoverFile != null ? model.CoverFile.FileName : "null")}', Genres='{model.Genres}', Tags='{model.Tags}', Type='{model.Type}', Format='{model.Format}', ReleaseYear='{model.ReleaseYear}', AlternativeTitles='{model.AlternativeTitles}', RelatedNovelIds='{model.RelatedNovelIds}'");

            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return Unauthorized();

            if (!(_permissionService.CanAddNovelDirectly(currentUser) || _permissionService.CanSubmitNovelForModeration(currentUser)))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            if (!ModelState.IsValid)
            {
                Debug.WriteLine("ModelState is invalid. Errors:");
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    Debug.WriteLine($"- {error.ErrorMessage}");
                }
                ViewData["AllGenres"] = AllGenres;
                ViewData["AllTags"] = AllTags;
                return View("~/Views/Novel/Create.cshtml", model);
            }

            if (!model.AuthorId.HasValue)
            {
                ModelState.AddModelError(nameof(model.AuthorId), "Необходимо указать автора.");
            }
            else
            {
                var authorUser = _mySqlService.GetUser(model.AuthorId.Value);
                if (authorUser == null)
                {
                    ModelState.AddModelError(nameof(model.AuthorId), "Выбранный автор не существует.");
                }
            }

            if (!ModelState.IsValid)
            {
                Debug.WriteLine("ModelState became invalid after custom AuthorId validation. Errors:");
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    Debug.WriteLine($"- {error.ErrorMessage}");
                }
                ViewData["AllGenres"] = AllGenres;
                ViewData["AllTags"] = AllTags;
                return View("~/Views/Novel/Create.cshtml", model);
            }

            bool hasCoverFileToProcess = model.CoverFile != null && model.CoverFile.Length > 0;

            var novelToCreate = new Novel
            {
                Title = model.Title,
                Description = model.Description,
                Genres = model.Genres,
                Tags = model.Tags,
                Type = model.Type,
                Format = model.Format,
                ReleaseYear = model.ReleaseYear,
                AlternativeTitles = string.IsNullOrWhiteSpace(model.AlternativeTitles) ?
                                    null :
                                    JsonSerializer.Serialize(model.AlternativeTitles.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()),
                RelatedNovelIds = model.RelatedNovelIds,
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                AuthorId = model.AuthorId
            };

            if (_permissionService.CanAddNovelDirectly(currentUser))
            {
                novelToCreate.Covers = JsonSerializer.Serialize(new List<string>());
                int newNovelId = _mySqlService.CreateNovel(novelToCreate);

                if (hasCoverFileToProcess)
                {
                    string coverPath = await _fileService.SaveNovelCoverAsync(model.CoverFile, newNovelId);
                    if (!string.IsNullOrEmpty(coverPath))
                    {
                        var createdNovel = _mySqlService.GetNovel(newNovelId);
                        if (createdNovel != null)
                        {
                            createdNovel.Covers = JsonSerializer.Serialize(new List<string> { coverPath });
                            _mySqlService.UpdateNovel(createdNovel);
                        }
                    }
                }
                TempData["SuccessMessage"] = "Новелла успешно добавлена.";
                return RedirectToAction("Details", "NovelView", new { id = newNovelId });
            }
            else if (_permissionService.CanSubmitNovelForModeration(currentUser))
            {
                novelToCreate.Covers = JsonSerializer.Serialize(new List<string>());

                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.AddNovel,
                    UserId = currentUser.Id,
                    RequestData = JsonSerializer.Serialize(novelToCreate),
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);
                TempData["SuccessMessage"] = "Запрос на добавление новеллы отправлен на модерацию.";
                return RedirectToAction("Index", "CatalogView");
            }

            // If neither CanAddNovelDirectly nor CanSubmitNovelForModeration is true,
            // it implies a permission issue or an unexpected state.
            // Redirect to Access Denied as per subtask requirement.
            return RedirectToAction("AccessDenied", "AuthView");
        }

        [Authorize(Roles = "Admin,Author")]
        [HttpGet]
        public IActionResult Edit(int id)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return RedirectToAction("Login", "AuthView");

            Novel novel = _mySqlService.GetNovel(id);
            if (novel == null) return NotFound();

            if (!_permissionService.CanEditNovel(currentUser, novel))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            var novelEditModel = new NovelEditModel
            {
                Id = novel.Id,
                Title = novel.Title,
                Description = novel.Description,
                Covers = novel.Covers,
                Genres = novel.Genres,
                Tags = novel.Tags,
                Type = novel.Type,
                Format = novel.Format,
                ReleaseYear = novel.ReleaseYear,
                AlternativeTitles = string.IsNullOrWhiteSpace(novel.AlternativeTitles) ?
                                    null :
                                    string.Join("\n", JsonSerializer.Deserialize<List<string>>(novel.AlternativeTitles) ?? new List<string>()),
                RelatedNovelIds = novel.RelatedNovelIds,
                AuthorId = novel.AuthorId
            };
            if (novel.AuthorId.HasValue)
            {
                var authorUser = _mySqlService.GetUser(novel.AuthorId.Value);
                novelEditModel.AuthorLogin = authorUser?.Login;
            }
            ViewData["AllGenres"] = AllGenres;
            ViewData["AllTags"] = AllTags;
            return View("~/Views/NovelView/Edit.cshtml", novelEditModel);
        }

        [Authorize(Roles = "Admin,Author")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, NovelEditModel model)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return Unauthorized();

            if (id != model.Id) return BadRequest();

            Novel originalNovel = _mySqlService.GetNovel(id);
            if (originalNovel == null) return NotFound("Оригинальная новелла не найдена.");

            if (!_permissionService.CanEditNovel(currentUser, originalNovel))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            if (ModelState.IsValid)
            {
                var novelWithChanges = new Novel
                {
                    Id = originalNovel.Id,
                    Title = model.Title,
                    Description = model.Description,
                    Genres = model.Genres,
                    Tags = model.Tags,
                    Type = model.Type,
                    Format = model.Format,
                    ReleaseYear = model.ReleaseYear,
                    AlternativeTitles = string.IsNullOrWhiteSpace(model.AlternativeTitles) ?
                                        null :
                                        JsonSerializer.Serialize(model.AlternativeTitles.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()),
                    RelatedNovelIds = model.RelatedNovelIds,
                    AuthorId = originalNovel.AuthorId,
                    Date = originalNovel.Date,
                    TranslatorId = originalNovel.TranslatorId
                };

                if (_permissionService.CanAddNovelDirectly(currentUser))
                {
                    if (model.NewCoverFile != null && model.NewCoverFile.Length > 0)
                    {
                        if (!string.IsNullOrWhiteSpace(originalNovel.Covers))
                        {
                            try
                            {
                                var existingCovers = JsonSerializer.Deserialize<List<string>>(originalNovel.Covers);
                                if (existingCovers != null)
                                {
                                    foreach (var oldCoverPath in existingCovers)
                                    {
                                        _fileService.DeleteFile(oldCoverPath);
                                    }
                                }
                            }
                            catch (JsonException ex)
                            {
                                Debug.WriteLine($"Error parsing originalNovel.Covers JSON: {ex.Message}");
                            }
                        }

                        string newCoverPath = await _fileService.SaveNovelCoverAsync(model.NewCoverFile, originalNovel.Id);
                        if (!string.IsNullOrEmpty(newCoverPath))
                        {
                            novelWithChanges.Covers = JsonSerializer.Serialize(new List<string> { newCoverPath });
                        }
                        else
                        {
                            novelWithChanges.Covers = originalNovel.Covers;
                        }
                    }
                    else
                    {
                        novelWithChanges.Covers = originalNovel.Covers;
                    }

                    originalNovel.Title = novelWithChanges.Title;
                    originalNovel.Description = novelWithChanges.Description;
                    originalNovel.Covers = novelWithChanges.Covers;
                    originalNovel.Genres = novelWithChanges.Genres;
                    originalNovel.Tags = novelWithChanges.Tags;
                    originalNovel.Type = novelWithChanges.Type;
                    originalNovel.Format = novelWithChanges.Format;
                    originalNovel.ReleaseYear = novelWithChanges.ReleaseYear;
                    originalNovel.AlternativeTitles = novelWithChanges.AlternativeTitles;
                    originalNovel.RelatedNovelIds = novelWithChanges.RelatedNovelIds;

                    _mySqlService.UpdateNovel(originalNovel);
                    TempData["SuccessMessage"] = "Новелла успешно обновлена.";
                    return RedirectToAction("Details", "NovelView", new { id = originalNovel.Id });
                }
                else if (currentUser.Role == UserRole.Author && originalNovel.AuthorId == currentUser.Id)
                {
                    novelWithChanges.Covers = originalNovel.Covers;

                    var moderationRequest = new ModerationRequest
                    {
                        RequestType = ModerationRequestType.EditNovel,
                        UserId = currentUser..Id,
                        NovelId = originalNovel.Id,
                        RequestData = JsonSerializer.Serialize(novelWithChanges),
                        Status = ModerationStatus.Pending,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _mySqlService.CreateModerationRequest(moderationRequest);
                    TempData["SuccessMessage"] = "Запрос на редактирование новеллы отправлен на модерацию.";
                    return RedirectToAction("Details", "NovelView", new { id = originalNovel.Id });
                }
                else
                {
                    return RedirectToAction("AccessDenied", "AuthView");
                }
            }

            model.AuthorLogin = _mySqlService.GetUser(model.AuthorId ?? 0)?.Login;
            ViewData["AllGenres"] = AllGenres;
            ViewData["AllTags"] = AllTags;
            return View("~/Views/NovelView/Edit.cshtml", model);
        }

        [Authorize(Roles = "Admin,Author")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return Unauthorized();

            Novel novel = _mySqlService.GetNovel(id);
            if (novel == null) return NotFound();

            if (!_permissionService.CanDeleteNovel(currentUser, novel))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            if (_permissionService.CanAddNovelDirectly(currentUser))
            {
                _mySqlService.DeleteNovel(id);
                TempData["SuccessMessage"] = "Новелла успешно удалена.";
                return RedirectToAction("Index", "CatalogView");
            }
            else if (currentUser.Role == UserRole.Author && novel.AuthorId == currentUser.Id)
            {
                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.DeleteNovel,
                    UserId = currentUser.Id,
                    NovelId = novel.Id,
                    RequestData = JsonSerializer.Serialize(new { novel.Title }),
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _mySqlService.CreateModerationRequest(moderationRequest);
                TempData["SuccessMessage"] = "Запрос на удаление новеллы отправлен на модерацию.";
                return RedirectToAction("Details", "NovelView", new { id = novel.Id });
            }
            else
            {
                return Forbid();
            }
        }
    } // End of NovelViewController class
} // End of BulbaLib.Controllers namespace
