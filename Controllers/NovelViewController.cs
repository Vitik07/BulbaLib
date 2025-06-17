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
    [Route("Novel")]
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

            var model = new NovelCreateModel();
            if (currentUser.Role == UserRole.Author)
            {
                model.AuthorId = currentUser.Id;
                ViewData["AuthorLoginForForm"] = currentUser.Login;
            }
            return View("~/Views/Novel/Create.cshtml", model);
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
            _logger.LogInformation($"  Incoming Model.Genres (should be JSON string): {model.Genres}");
            _logger.LogInformation($"  Incoming Model.Tags (should be JSON string): {model.Tags}");

            // The ProcessJsonStringToList function is removed as per requirement.
            // Genres and Tags from the model are now used directly.

            _logger.LogInformation($"  Using Model.Genres directly for Novel object: {model.Genres}");
            _logger.LogInformation($"  Using Model.Tags directly for Novel object: {model.Tags}");
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
                Genres = model.Genres, // Directly use the JSON string from the model
                Tags = model.Tags,     // Directly use the JSON string from the model
                Type = model.Type,
                Format = model.Format,
                ReleaseYear = model.ReleaseYear,
                AlternativeTitles = serializedAlternativeTitles, // Use the already prepared variable
                RelatedNovelIds = model.RelatedNovelIds,
                Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                AuthorId = currentUser.Role == UserRole.Admin ? model.AuthorId : currentUser.Id,
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

                string tempCoverPathJson = "[]";
                if (model.CoverFile != null && model.CoverFile.Length > 0)
                {
                    string savedTempPath = await _fileService.SaveTempNovelCoverAsync(model.CoverFile);
                    if (!string.IsNullOrEmpty(savedTempPath))
                    {
                        tempCoverPathJson = JsonSerializer.Serialize(new List<string> { savedTempPath });
                    }
                }
                novelToCreate.Covers = tempCoverPathJson; // Сохраняем JSON массив с временным путем
                // AuthorId is already correctly set for Author role earlier in novelToCreate initialization.

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
                AuthorId = novel.AuthorId,
                TranslatorId = novel.TranslatorId,
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
            novelEditModel.KeptCovers = currentCovers;

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
            return View("~/Views/Novel/Edit.cshtml", novelEditModel);
        }

        [Authorize(Roles = "Admin,Author")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, NovelEditModel model)
        {
            _logger.LogInformation($"[Edit Novel POST] Received Model ID: {model.Id}");
            _logger.LogInformation($"  Model Title: {model.Title}");
            _logger.LogInformation($"  Model Description: {model.Description}");
            _logger.LogInformation($"  Model Genres: {model.Genres}");
            _logger.LogInformation($"  Model Tags: {model.Tags}");
            _logger.LogInformation($"  Model Type: {model.Type}");
            _logger.LogInformation($"  Model Format: {model.Format}");
            _logger.LogInformation($"  Model ReleaseYear: {model.ReleaseYear}");
            _logger.LogInformation($"  Model AlternativeTitles: {model.AlternativeTitles}");
            _logger.LogInformation($"  Model RelatedNovelIds: {model.RelatedNovelIds}");
            _logger.LogInformation($"  Model KeptCovers (existing, kept by user): {(model.KeptCovers == null ? "null" : string.Join(", ", model.KeptCovers))}");
            _logger.LogInformation($"  Model NewCoverFiles count: {(model.NewCoverFiles?.Count(f => f != null && f.Length > 0) ?? 0)}");
            // TODO: Log other relevant properties from NovelEditModel if any are added later e.g. AuthorLogin if it were part of POST

            var currentUser = _currentUserService.GetCurrentUser();
            if (currentUser == null) return Unauthorized();

            if (id != model.Id) return BadRequest();

            Novel originalNovel = _mySqlService.GetNovel(id);
            if (originalNovel == null) return NotFound("Оригинальная новелла не найдена.");

            if (!_permissionService.CanEditNovel(currentUser, originalNovel))
            {
                return RedirectToAction("AccessDenied", "AuthView");
            }

            // Explicitly remove any model state errors for NewCoverFiles before custom validation runs
            _logger.LogInformation($"[Edit Novel POST] Перед ModelState.Remove. NewCoverFiles is null: {model.NewCoverFiles == null}");
            if (model.NewCoverFiles != null)
            {
                _logger.LogInformation($"[Edit Novel POST] Перед ModelState.Remove. NewCoverFiles count: {model.NewCoverFiles.Count}");
                for (int i = 0; i < model.NewCoverFiles.Count; i++)
                {
                    if (model.NewCoverFiles[i] == null)
                    {
                        _logger.LogInformation($"[Edit Novel POST] File {i} is null");
                    }
                    else
                    {
                        _logger.LogInformation($"[Edit Novel POST] File {i} Name: {model.NewCoverFiles[i].FileName}, Length: {model.NewCoverFiles[i].Length}");
                    }
                }
            }

            var newCoverFilesEntry = ModelState[nameof(NovelEditModel.NewCoverFiles)];
            if (newCoverFilesEntry != null && newCoverFilesEntry.Errors.Any())
            {
                foreach (var error in newCoverFilesEntry.Errors)
                {
                    _logger.LogWarning($"[Edit Novel POST] Ошибка валидации для NewCoverFiles ПЕРЕД REMOVE: {error.ErrorMessage}");
                }
            }
            else if (newCoverFilesEntry == null || !newCoverFilesEntry.Errors.Any())
            {
                _logger.LogInformation("[Edit Novel POST] Нет ошибок валидации для NewCoverFiles ПЕРЕД REMOVE.");
            }

            ModelState.Remove(nameof(NovelEditModel.NewCoverFiles));

            var newCoverFilesEntryAfterRemove = ModelState[nameof(NovelEditModel.NewCoverFiles)];
            if (newCoverFilesEntryAfterRemove != null && newCoverFilesEntryAfterRemove.Errors.Any())
            {
                foreach (var error in newCoverFilesEntryAfterRemove.Errors)
                {
                    _logger.LogWarning($"[Edit Novel POST] Ошибка валидации для NewCoverFiles ПОСЛЕ REMOVE: {error.ErrorMessage}");
                }
            }
            else
            {
                _logger.LogInformation("[Edit Novel POST] Запись ModelState для NewCoverFiles успешно удалена или не содержит ошибок после Remove.");
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
                    AuthorId = (currentUser.Role == UserRole.Admin) ? model.AuthorId : currentUser.Id, // Admin can change, Author forced to own ID
                    Date = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), // Update date on edit
                    TranslatorId = originalNovel.TranslatorId // TranslatorId is now managed automatically
                    // Covers will be set below based on role
                };

                if (_permissionService.CanAddNovelDirectly(currentUser)) // Admin Path
                {
                    _logger.LogInformation($"[Edit Novel POST - Admin Path] Processing covers. KeptCovers count: {model.KeptCovers?.Count ?? 0}, NewCoverFiles count: {model.NewCoverFiles?.Count(f => f != null && f.Length > 0) ?? 0}");
                    List<string> finalCoverPathsForNovel = new List<string>();

                    // Add covers that user decided to keep
                    if (model.KeptCovers != null)
                    {
                        finalCoverPathsForNovel.AddRange(model.KeptCovers);
                    }
                    _logger.LogInformation($"[Edit Novel POST - Admin Path] Paths after adding KeptCovers: {JsonSerializer.Serialize(finalCoverPathsForNovel)}");

                    // Process newly uploaded files by Admin, save them directly to permanent location
                    if (model.NewCoverFiles != null)
                    {
                        foreach (var file in model.NewCoverFiles.Where(f => f != null && f.Length > 0))
                        {
                            _logger.LogInformation($"[Edit Novel POST - Admin Path] Saving new cover directly: {file.FileName}");
                            string newPermanentPath = await _fileService.SaveNovelCoverAsync(file, originalNovel.Id);
                            if (!string.IsNullOrEmpty(newPermanentPath))
                            {
                                finalCoverPathsForNovel.Add(newPermanentPath);
                                _logger.LogInformation($"[Edit Novel POST - Admin Path] Saved new cover to: {newPermanentPath}");
                            }
                            else
                            {
                                _logger.LogWarning($"[Edit Novel POST - Admin Path] Failed to save new cover: {file.FileName}");
                            }
                        }
                    }
                    _logger.LogInformation($"[Edit Novel POST - Admin Path] Paths after processing NewCoverFiles: {JsonSerializer.Serialize(finalCoverPathsForNovel)}");

                    // Determine and delete covers that were removed by the Admin
                    List<string> originalCoversList = new List<string>();
                    if (!string.IsNullOrWhiteSpace(originalNovel.Covers))
                    {
                        try { originalCoversList = JsonSerializer.Deserialize<List<string>>(originalNovel.Covers) ?? new List<string>(); }
                        catch (JsonException ex) { _logger.LogWarning(ex, $"[Edit Novel POST - Admin Path] Error deserializing originalNovel.Covers JSON: {originalNovel.Covers}"); }
                    }

                    foreach (var oldCoverPath in originalCoversList)
                    {
                        if (!finalCoverPathsForNovel.Contains(oldCoverPath))
                        {
                            _logger.LogInformation($"[Edit Novel POST - Admin Path] Deleting cover marked for removal: {oldCoverPath}");
                            await _fileService.DeleteCoverAsync(oldCoverPath); // Use the new async delete method
                        }
                    }

                    // Update originalNovel instance with all changes
                    originalNovel.Title = novelWithChanges.Title; // novelWithChanges has form data
                    originalNovel.Description = novelWithChanges.Description;
                    originalNovel.Genres = novelWithChanges.Genres;
                    originalNovel.Tags = novelWithChanges.Tags;
                    originalNovel.Type = novelWithChanges.Type;
                    originalNovel.Format = novelWithChanges.Format;
                    originalNovel.ReleaseYear = novelWithChanges.ReleaseYear;
                    originalNovel.AlternativeTitles = novelWithChanges.AlternativeTitles;
                    originalNovel.RelatedNovelIds = novelWithChanges.RelatedNovelIds;
                    originalNovel.AuthorId = novelWithChanges.AuthorId; // This was correctly set in novelWithChanges for Admin
                    // originalNovel.TranslatorId = novelWithChanges.TranslatorId; // This line is removed as TranslatorId is auto-managed
                    originalNovel.Date = novelWithChanges.Date; // Ensure date is updated
                    originalNovel.Covers = JsonSerializer.Serialize(finalCoverPathsForNovel.Distinct().ToList());

                    _logger.LogInformation("[Edit Novel POST - Admin Path] Logging final originalNovel data before UpdateNovel:");
                    _logger.LogInformation($"  ID: {originalNovel.Id}");
                    _logger.LogInformation($"  Title: {originalNovel.Title}");
                    _logger.LogInformation($"  Description: {originalNovel.Description}");
                    _logger.LogInformation($"  Covers (JSON): {originalNovel.Covers}"); // This will be the same as novelWithChanges.Covers logged above
                    _logger.LogInformation($"  Genres: {originalNovel.Genres}");
                    _logger.LogInformation($"  Tags: {originalNovel.Tags}");
                    _logger.LogInformation($"  Type: {originalNovel.Type}");
                    _logger.LogInformation($"  Format: {originalNovel.Format}");
                    _logger.LogInformation($"  ReleaseYear: {originalNovel.ReleaseYear}");
                    _logger.LogInformation($"  AlternativeTitles (JSON): {originalNovel.AlternativeTitles}");
                    _logger.LogInformation($"  RelatedNovelIds: {originalNovel.RelatedNovelIds}");
                    _logger.LogInformation($"  AuthorId: {originalNovel.AuthorId}");
                    // TODO: Log other relevant properties from Novel if any are added later e.g. TranslatorId

                    _mySqlService.UpdateNovel(originalNovel);
                    TempData["SuccessMessage"] = "Новелла успешно обновлена.";
                    return RedirectToAction("Details", "NovelView", new { id = originalNovel.Id });
                }
                else if (currentUser.Role == UserRole.Author && originalNovel.AuthorId == currentUser.Id)
                {
                    _logger.LogInformation($"[Edit Novel POST - Author Path] Processing covers for author. KeptCovers count: {model.KeptCovers?.Count ?? 0}, NewCoverFiles count: {model.NewCoverFiles?.Count(f => f != null && f.Length > 0) ?? 0}");
                    List<string> allCoverPaths = new List<string>();
                    if (model.KeptCovers != null) // Пути к существующим обложкам, которые автор решил оставить
                    {
                        allCoverPaths.AddRange(model.KeptCovers);
                    }

                    if (model.NewCoverFiles != null)
                    {
                        foreach (var file in model.NewCoverFiles.Where(f => f != null && f.Length > 0))
                        {
                            _logger.LogInformation($"[Edit Novel POST - Author Path] Saving temp cover for file: {file.FileName}");
                            string tempPath = await _fileService.SaveTempNovelCoverAsync(file);
                            if (!string.IsNullOrEmpty(tempPath))
                            {
                                allCoverPaths.Add(tempPath);
                                _logger.LogInformation($"[Edit Novel POST - Author Path] Saved temp cover to: {tempPath}");
                            }
                            else
                            {
                                _logger.LogWarning($"[Edit Novel POST - Author Path] Failed to save temp cover for file: {file.FileName}");
                            }
                        }
                    }

                    novelWithChanges.Covers = JsonSerializer.Serialize(allCoverPaths.Distinct().ToList());
                    _logger.LogInformation($"[Edit Novel POST - Author Path] Serialized allCoverPaths for novelWithChanges.Covers: {novelWithChanges.Covers}");
                    // novelWithChanges.AuthorId is already set to currentUser.Id
                    // novelWithChanges.TranslatorId is already set to originalNovel.TranslatorId

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

                    _logger.LogInformation("[Edit Novel POST - Author Path] Logging novel data for ModerationRequest:");
                    _logger.LogInformation($"  ID: {novelWithChanges.Id}");
                    _logger.LogInformation($"  Title: {novelWithChanges.Title}");
                    _logger.LogInformation($"  Description: {novelWithChanges.Description}");
                    _logger.LogInformation($"  Covers (JSON): {novelWithChanges.Covers}");
                    _logger.LogInformation($"  Genres: {novelWithChanges.Genres}");
                    _logger.LogInformation($"  Tags: {novelWithChanges.Tags}");
                    _logger.LogInformation($"  Type: {novelWithChanges.Type}");
                    _logger.LogInformation($"  Format: {novelWithChanges.Format}");
                    _logger.LogInformation($"  ReleaseYear: {novelWithChanges.ReleaseYear}");
                    _logger.LogInformation($"  AlternativeTitles (JSON): {novelWithChanges.AlternativeTitles}");
                    _logger.LogInformation($"  RelatedNovelIds: {novelWithChanges.RelatedNovelIds}");
                    _logger.LogInformation($"  AuthorId: {novelWithChanges.AuthorId}");
                    // TODO: Log other relevant properties from Novel if any are added later e.g. TranslatorId

                    _mySqlService.CreateModerationRequest(moderationRequest);
                    TempData["SuccessMessage"] = "Запрос на редактирование новеллы отправлен на модерацию.";
                    return RedirectToAction("Details", "NovelView", new { id = originalNovel.Id });
                }
                else
                {
                    return RedirectToAction("AccessDenied", "AuthView");
                }
            }

            _logger.LogWarning("[Edit Novel POST] ModelState НЕ валиден. Ошибки:");
            foreach (var stateKey in ModelState.Keys)
            {
                var stateEntry = ModelState[stateKey];
                if (stateEntry.Errors.Any())
                {
                    foreach (var error in stateEntry.Errors)
                    {
                        _logger.LogWarning($"  Свойство: {stateKey}, Ошибка: {error.ErrorMessage}");
                    }
                }
            }
            model.AuthorLogin = _mySqlService.GetUser(model.AuthorId ?? 0)?.Login;
            ViewData["AllGenres"] = AllGenres;
            ViewData["AllTags"] = AllTags;
            return View("~/Views/Novel/Edit.cshtml", model);
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
