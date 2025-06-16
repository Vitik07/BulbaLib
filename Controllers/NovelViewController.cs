using Microsoft.AspNetCore.Mvc;
using BulbaLib.Services; // Added
using BulbaLib.Models;   // Added
using System.Diagnostics; // Added for Debug.WriteLine
using Microsoft.Extensions.Logging; // Added
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
        private readonly ILogger<NovelViewController> _logger;

        public NovelViewController(MySqlService mySqlService, PermissionService permissionService, ICurrentUserService currentUserService, FileService fileService, ILogger<NovelViewController> logger)
        {
            _mySqlService = mySqlService;
            _permissionService = permissionService;
            _currentUserService = currentUserService;
            _fileService = fileService;
            _logger = logger;
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
            _logger.LogInformation("[Create Novel POST] Received Model:");
            _logger.LogInformation($"  Title: {model.Title}");
            _logger.LogInformation($"  Description: {model.Description}");
            _logger.LogInformation($"  AuthorId: {model.AuthorId}");
            _logger.LogInformation($"  CoverFile: {(model.CoverFile != null ? model.CoverFile.FileName : "null")}");
            _logger.LogInformation($"  Genres: {model.Genres}");
            _logger.LogInformation($"  Tags: {model.Tags}");
            _logger.LogInformation($"  Type: {model.Type}");
            _logger.LogInformation($"  Format: {model.Format}");
            _logger.LogInformation($"  ReleaseYear: {model.ReleaseYear}");
            _logger.LogInformation($"  AlternativeTitles: {model.AlternativeTitles}");
            _logger.LogInformation($"  RelatedNovelIds: {model.RelatedNovelIds}");
            // Assuming NovelCreateModel might have more properties, add them here if necessary.

            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser != null)
            {
                _logger.LogInformation($"[Create Novel POST] Current User: Id={currentUser.Id}, Role={currentUser.Role}");
            }
            else
            {
                _logger.LogInformation("[Create Novel POST] Current User: null");
            }

            if (currentUser == null) return Unauthorized();

            if (!(_permissionService.CanAddNovelDirectly(currentUser) || _permissionService.CanSubmitNovelForModeration(currentUser)))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("[Create Novel POST] ModelState is invalid. Errors:");
                foreach (var state in ModelState)
                {
                    foreach (var error in state.Value.Errors)
                    {
                        _logger.LogWarning($"  Property: {state.Key}, Error: {error.ErrorMessage}");
                    }
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
                _logger.LogWarning("[Create Novel POST] ModelState became invalid after custom AuthorId validation. Errors:");
                foreach (var state in ModelState)
                {
                    foreach (var error in state.Value.Errors)
                    {
                        _logger.LogWarning($"  Property: {state.Key}, Error: {error.ErrorMessage}");
                    }
                }
                ViewData["AllGenres"] = AllGenres;
                ViewData["AllTags"] = AllTags;
                return View("~/Views/Novel/Create.cshtml", model);
            }

            bool hasCoverFileToProcess = model.CoverFile != null && model.CoverFile.Length > 0;

            _logger.LogInformation("[Create Novel POST] Preparing novelToCreate object.");
            _logger.LogInformation($"  Incoming Model.Genres: {model.Genres}");
            _logger.LogInformation($"  Incoming Model.Tags: {model.Tags}");

            string ProcessJsonStringToList(string jsonInput, string fieldNameForLogging)
            {
                if (string.IsNullOrWhiteSpace(jsonInput) || jsonInput == "[]")
                {
                    _logger.LogInformation($"[ProcessJsonStringToList] Input for {fieldNameForLogging} is null, empty, whitespace, or '[]'. Returning null.");
                    return null;
                }
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(jsonInput);
                    if (list != null && list.Any())
                    {
                        string result = string.Join(",", list);
                        _logger.LogInformation($"[ProcessJsonStringToList] Successfully processed {fieldNameForLogging}. Input: '{jsonInput}', Output: '{result}'");
                        return result;
                    }
                    else
                    {
                        _logger.LogInformation($"[ProcessJsonStringToList] Deserialized list for {fieldNameForLogging} is null or empty. Input: '{jsonInput}'. Returning null.");
                        return null;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, $"[ProcessJsonStringToList] Error deserializing {fieldNameForLogging} JSON: {jsonInput}. Returning null.");
                    return null;
                }
            }

            string processedGenres = ProcessJsonStringToList(model.Genres, "Genres");
            string processedTags = ProcessJsonStringToList(model.Tags, "Tags");

            _logger.LogInformation($"  Processed Genres for Novel object: {processedGenres}");
            _logger.LogInformation($"  Processed Tags for Novel object: {processedTags}");
            _logger.LogInformation($"  Title: {model.Title}");
            _logger.LogInformation($"  Description: {model.Description}");
            _logger.LogInformation($"  Type: {model.Type}");
            _logger.LogInformation($"  Format: {model.Format}");
            _logger.LogInformation($"  ReleaseYear: {model.ReleaseYear}");
            string serializedAlternativeTitles = string.IsNullOrWhiteSpace(model.AlternativeTitles) ?
                                    null :
                                    JsonSerializer.Serialize(model.AlternativeTitles.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList());
            _logger.LogInformation($"  SerializedAlternativeTitles: {serializedAlternativeTitles}");
            _logger.LogInformation($"  RelatedNovelIds: {model.RelatedNovelIds}");
            _logger.LogInformation($"  AuthorId: {model.AuthorId}");

            var novelToCreate = new Novel
            {
                Title = model.Title,
                Description = model.Description,
                Genres = processedGenres,
                Tags = processedTags,
                Type = model.Type,
                Format = model.Format,
                ReleaseYear = model.ReleaseYear,
                AlternativeTitles = serializedAlternativeTitles, // Use the already prepared variable
                RelatedNovelIds = model.RelatedNovelIds,
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                AuthorId = model.AuthorId
            };

            if (_permissionService.CanAddNovelDirectly(currentUser))
            {
                _logger.LogInformation("[Create Novel POST] Path taken: CanAddNovelDirectly");
                novelToCreate.Covers = JsonSerializer.Serialize(new List<string>()); // Initialize Covers before logging
                _logger.LogInformation($"[Create Novel POST] novelToCreate for direct add: {JsonSerializer.Serialize(novelToCreate)}");
                int newNovelId = _mySqlService.CreateNovel(novelToCreate);
                _logger.LogInformation($"[Create Novel POST] newNovelId: {newNovelId}");

                if (hasCoverFileToProcess)
                {
                    _logger.LogInformation("[Create Novel POST] Starting cover file processing.");
                    string coverPath = await _fileService.SaveNovelCoverAsync(model.CoverFile, newNovelId);
                    _logger.LogInformation($"[Create Novel POST] coverPath: {coverPath}");
                    if (!string.IsNullOrEmpty(coverPath))
                    {
                        var createdNovel = _mySqlService.GetNovel(newNovelId);
                        if (createdNovel != null)
                        {
                            _logger.LogInformation($"[Create Novel POST] Found createdNovel with Id: {createdNovel.Id}. Updating with cover path.");
                            createdNovel.Covers = JsonSerializer.Serialize(new List<string> { coverPath });
                            _mySqlService.UpdateNovel(createdNovel);
                        }
                        else
                        {
                            _logger.LogError($"[Create Novel POST] ERROR: createdNovel is null after GetNovel({newNovelId}). Cannot update cover path.");
                        }
                    }
                    else
                    {
                        _logger.LogError("[Create Novel POST] ERROR: coverPath is null or empty after SaveNovelCoverAsync.");
                    }
                }
                TempData["SuccessMessage"] = "Новелла успешно добавлена.";
                return RedirectToAction("Details", "NovelView", new { id = newNovelId });
            }
            else if (_permissionService.CanSubmitNovelForModeration(currentUser))
            {
                _logger.LogInformation("[Create Novel POST] Path taken: CanSubmitNovelForModeration");
                novelToCreate.Covers = JsonSerializer.Serialize(new List<string>()); // Initialize Covers before logging
                _logger.LogInformation($"[Create Novel POST] novelToCreate for moderation: {JsonSerializer.Serialize(novelToCreate)}");

                var moderationRequest = new ModerationRequest
                {
                    RequestType = ModerationRequestType.AddNovel,
                    UserId = currentUser.Id,
                    RequestData = JsonSerializer.Serialize(novelToCreate), // Serialize the final novelToCreate
                    Status = ModerationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _logger.LogInformation($"[Create Novel POST] moderationRequest for submission: {JsonSerializer.Serialize(moderationRequest)}");
                _mySqlService.CreateModerationRequest(moderationRequest);
                // Assuming CreateModerationRequest doesn't return ID, or it's not needed for logging here.
                // If it does, it could be logged: _logger.LogInformation($"[Create Novel POST] Created ModerationRequest with ID: {requestId}");
                _logger.LogInformation("[Create Novel POST] ModerationRequest created successfully.");
                TempData["SuccessMessage"] = "Запрос на добавление новеллы отправлен на модерацию.";
                return RedirectToAction("Index", "CatalogView");
            }

            // If neither CanAddNovelDirectly nor CanSubmitNovelForModeration is true,
            // it implies a permission issue or an unexpected state.
            // Redirect to Access Denied as per subtask requirement.
            _logger.LogWarning("[Create Novel POST] Fallback: Neither CanAddNovelDirectly nor CanSubmitNovelForModeration is true. Redirecting to AccessDenied.");
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
                // Covers = novel.Covers, // To be replaced
                Genres = novel.Genres,
                Tags = novel.Tags,
                Type = novel.Type,
                Format = novel.Format,
                ReleaseYear = novel.ReleaseYear,
                // AlternativeTitles = string.IsNullOrWhiteSpace(novel.AlternativeTitles) ?
                //                    null :
                //                    string.Join("\n", JsonSerializer.Deserialize<List<string>>(novel.AlternativeTitles) ?? new List<string>()),
                RelatedNovelIds = novel.RelatedNovelIds, // This will be handled in a later step
                AuthorId = novel.AuthorId
            };

            // Handle Covers separately with error catching and deserialization
            List<string> currentCovers = new List<string>();
            if (!string.IsNullOrWhiteSpace(novel.Covers))
            {
                try
                {
                    currentCovers = JsonSerializer.Deserialize<List<string>>(novel.Covers) ?? new List<string>();
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Error deserializing novel.Covers JSON in Edit GET: {CoversJson}", novel.Covers);
                    // Initialize with empty list if JSON is malformed
                }
            }
            novelEditModel.Covers = currentCovers;

            // Handle AlternativeTitles separately with error catching
            if (string.IsNullOrWhiteSpace(novel.AlternativeTitles))
            {
                novelEditModel.AlternativeTitles = null;
            }
            else
            {
                try
                {
                    // Try to deserialize as a JSON list of strings
                    var altTitlesList = JsonSerializer.Deserialize<List<string>>(novel.AlternativeTitles);
                    novelEditModel.AlternativeTitles = string.Join("\n", altTitlesList ?? new List<string>());
                }
                catch (JsonException)
                {
                    // If deserialization fails, assume it's a plain multi-line string
                    // The NovelEditModel.AlternativeTitles expects a single string where newlines separate titles.
                    novelEditModel.AlternativeTitles = novel.AlternativeTitles;
                }
            }
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
                    // Initialize a list with covers that the user decided to keep (not removed via UI)
                    List<string> updatedCoverPaths = new List<string>();
                    if (model.Covers != null) // model.Covers contains paths from hidden inputs for existing covers
                    {
                        updatedCoverPaths.AddRange(model.Covers);
                    }

                    // Process newly uploaded files and add their paths to the list
                    if (model.NewCoverFiles != null && model.NewCoverFiles.Any(f => f != null && f.Length > 0))
                    {
                        foreach (var file in model.NewCoverFiles)
                        {
                            if (file != null && file.Length > 0)
                            {
                                string newPath = await _fileService.SaveNovelCoverAsync(file, originalNovel.Id);
                                if (!string.IsNullOrEmpty(newPath))
                                {
                                    updatedCoverPaths.Add(newPath);
                                }
                            }
                        }
                    }

                    // Assign the consolidated list of unique cover paths to the novel object being prepared for update.
                    // Using .Distinct() to avoid duplicate paths if any somehow occur.
                    novelWithChanges.Covers = JsonSerializer.Serialize(updatedCoverPaths.Distinct().ToList());

                    // Ensure originalNovel fields are updated before calling UpdateNovel
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
                        UserId = currentUser.Id,
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
