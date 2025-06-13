using System;
using System.Collections.Generic;
using System.Data;
using MySql.Data.MySqlClient;
using System.IO;
using System.Linq;
using BulbaLib.Models;

namespace BulbaLib.Services
{
    public class MySqlService
    {
        private readonly string _connectionString;

        public MySqlService(string connectionString)
        {
            _connectionString = connectionString;
        }

        private MySqlConnection GetConnection()
        {
            var conn = new MySqlConnection(_connectionString);
            conn.Open();
            return conn;
        }

        // ---------- USERS ----------
        public bool UserExists(string login)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Login = @login";
            cmd.Parameters.AddWithValue("@login", login);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        public void CreateUser(string login, string password, byte[] avatar)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Users (Login, Password, Role, Avatar) VALUES (@login, @password, 'User', @avatar)";
            cmd.Parameters.AddWithValue("@login", login);
            cmd.Parameters.AddWithValue("@password", password);
            cmd.Parameters.AddWithValue("@avatar", avatar);
            cmd.ExecuteNonQuery();
        }

        public User AuthenticateUser(string login, string password)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Login, Password, Role, Avatar FROM Users WHERE Login = @login AND Password = @password";
            cmd.Parameters.AddWithValue("@login", login);
            cmd.Parameters.AddWithValue("@password", password);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new User
                {
                    Id = reader.GetInt32("Id"),
                    Login = reader.GetString("Login"),
                    Password = reader.GetString("Password"),
                    Role = reader.GetString("Role"),
                    Avatar = !reader.IsDBNull("Avatar") ? (byte[])reader["Avatar"] : null
                };
            }
            return null;
        }

        public User GetUser(int userId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Login, Avatar FROM Users WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", userId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new User
                {
                    Id = reader.GetInt32("Id"),
                    Login = reader.GetString("Login"),
                    Avatar = !reader.IsDBNull("Avatar") ? (byte[])reader["Avatar"] : null
                };
            }
            return null;
        }

        public void UpdateUserAvatar(int userId, byte[] avatar)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET Avatar = @avatar WHERE Id = @id";
            cmd.Parameters.AddWithValue("@avatar", avatar);
            cmd.Parameters.AddWithValue("@id", userId);
            cmd.ExecuteNonQuery();
        }

        // ---------- NOVELS ----------
        // --- Методы работы с Novel с учетом Covers ---

        public List<Novel> GetNovels(string search = "")
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            if (!string.IsNullOrWhiteSpace(search))
            {
                cmd.CommandText = @"SELECT Id, Title, Description, Covers, Genres, Tags, Type, Format, ReleaseYear, AuthorId, TranslatorId, AlternativeTitles, RelatedNovelIds, Date 
                            FROM Novels 
                            WHERE LOWER(Title) LIKE @search OR LOWER(Genres) LIKE @search OR LOWER(Tags) LIKE @search";
                cmd.Parameters.AddWithValue("@search", $"%{search.ToLower()}%");
            }
            else
            {
                cmd.CommandText = @"SELECT Id, Title, Description, Covers, Genres, Tags, Type, Format, ReleaseYear, AuthorId, TranslatorId, AlternativeTitles, RelatedNovelIds, Date 
                            FROM Novels";
            }

            var novels = new List<Novel>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                novels.Add(new Novel
                {
                    Id = reader.GetInt32("Id"),
                    Title = reader.GetString("Title"),
                    Description = reader.IsDBNull("Description") ? "" : reader.GetString("Description"),
                    Covers = reader.IsDBNull("Covers") ? null : reader.GetString("Covers"),
                    Genres = reader.IsDBNull("Genres") ? "" : reader.GetString("Genres"),
                    Tags = reader.IsDBNull("Tags") ? "" : reader.GetString("Tags"),
                    Type = reader.IsDBNull("Type") ? "" : reader.GetString("Type"),
                    Format = reader.IsDBNull("Format") ? "" : reader.GetString("Format"),
                    ReleaseYear = reader.IsDBNull("ReleaseYear") ? (int?)null : reader.GetInt32("ReleaseYear"),
                    AuthorId = reader.IsDBNull("AuthorId") ? (int?)null : reader.GetInt32("AuthorId"),
                    TranslatorId = reader.IsDBNull("TranslatorId") ? "" : reader.GetString("TranslatorId"),
                    AlternativeTitles = reader.IsDBNull("AlternativeTitles") ? "" : reader.GetString("AlternativeTitles"),
                    RelatedNovelIds = reader.IsDBNull("RelatedNovelIds") ? "" : reader.GetString("RelatedNovelIds"),
                    Date = reader.IsDBNull("Date") ? 0 : reader.GetInt64("Date") // если ты добавил поле Date
                });
            }
            return novels;
        }

        public Novel GetNovel(int id)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Title, Description, Covers, Genres, Tags, Type, Format, ReleaseYear, AuthorId, TranslatorId, AlternativeTitles, RelatedNovelIds, Date FROM Novels WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new Novel
                {
                    Id = reader.GetInt32("Id"),
                    Title = reader.GetString("Title"),
                    Description = reader.IsDBNull("Description") ? "" : reader.GetString("Description"),
                    Covers = reader.IsDBNull("Covers") ? null : reader.GetString("Covers"),
                    Genres = reader.IsDBNull("Genres") ? "" : reader.GetString("Genres"),
                    Tags = reader.IsDBNull("Tags") ? "" : reader.GetString("Tags"),
                    Type = reader.IsDBNull("Type") ? "" : reader.GetString("Type"),
                    Format = reader.IsDBNull("Format") ? "" : reader.GetString("Format"),
                    ReleaseYear = reader.IsDBNull("ReleaseYear") ? (int?)null : reader.GetInt32("ReleaseYear"),
                    AuthorId = reader.IsDBNull("AuthorId") ? (int?)null : reader.GetInt32("AuthorId"),
                    TranslatorId = reader.IsDBNull("TranslatorId") ? "" : reader.GetString("TranslatorId"),
                    AlternativeTitles = reader.IsDBNull("AlternativeTitles") ? "" : reader.GetString("AlternativeTitles"),
                    RelatedNovelIds = reader.IsDBNull("RelatedNovelIds") ? "" : reader.GetString("RelatedNovelIds")
                };
            }
            return null;
        }

        public void CreateNovel(Novel novel)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO Novels 
        (Title, Description, Covers, Genres, Tags, Type, Format, ReleaseYear, AuthorId, TranslatorId, AlternativeTitles)
        VALUES (@title, @desc, @covers, @genres, @tags, @type, @format, @releaseYear, @authorId, @translatorId, @altTitles)";
            cmd.Parameters.AddWithValue("@title", novel.Title);
            cmd.Parameters.AddWithValue("@desc", novel.Description ?? "");
            cmd.Parameters.AddWithValue("@covers", novel.Covers ?? "[]");
            cmd.Parameters.AddWithValue("@genres", novel.Genres ?? "");
            cmd.Parameters.AddWithValue("@tags", novel.Tags ?? "");
            cmd.Parameters.AddWithValue("@type", novel.Type ?? "");
            cmd.Parameters.AddWithValue("@format", novel.Format ?? "");
            cmd.Parameters.AddWithValue("@releaseYear", novel.ReleaseYear.HasValue ? novel.ReleaseYear.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@authorId", novel.AuthorId.HasValue ? novel.AuthorId.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@translatorId", string.IsNullOrEmpty(novel.TranslatorId) ? (object)DBNull.Value : novel.TranslatorId);
            cmd.Parameters.AddWithValue("@altTitles", novel.AlternativeTitles ?? "");
            cmd.ExecuteNonQuery();
        }

        public void UpdateNovel(Novel novel)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE Novels SET 
        Title=@title, 
        Description=@desc, 
        Covers=@covers, 
        Genres=@genres, 
        Tags=@tags,
        Type=@type,
        Format=@format,
        ReleaseYear=@releaseYear,
        AuthorId=@authorId,
        TranslatorId=@translatorId,
        AlternativeTitles=@altTitles
        WHERE Id=@id";
            cmd.Parameters.AddWithValue("@title", novel.Title);
            cmd.Parameters.AddWithValue("@desc", novel.Description ?? "");
            cmd.Parameters.AddWithValue("@covers", novel.Covers ?? "[]");
            cmd.Parameters.AddWithValue("@genres", novel.Genres ?? "");
            cmd.Parameters.AddWithValue("@tags", novel.Tags ?? "");
            cmd.Parameters.AddWithValue("@type", novel.Type ?? "");
            cmd.Parameters.AddWithValue("@format", novel.Format ?? "");
            cmd.Parameters.AddWithValue("@releaseYear", novel.ReleaseYear.HasValue ? novel.ReleaseYear.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@id", novel.Id);
            cmd.Parameters.AddWithValue("@altTitles", novel.AlternativeTitles ?? "");
            cmd.Parameters.AddWithValue("@authorId", novel.AuthorId.HasValue ? novel.AuthorId.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@translatorId", string.IsNullOrEmpty(novel.TranslatorId) ? (object)DBNull.Value : novel.TranslatorId);
            cmd.ExecuteNonQuery();
        }

        public void DeleteNovel(int id)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Novels WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ---------- CHAPTERS ----------
        public List<Chapter> GetChaptersByNovel(int novelId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, NovelId, Number, Title, Content, Date FROM Chapters WHERE NovelId = @novelId";
            cmd.Parameters.AddWithValue("@novelId", novelId);

            var chapters = new List<Chapter>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                chapters.Add(new Chapter
                {
                    Id = reader.GetInt32("Id"),
                    NovelId = reader.GetInt32("NovelId"),
                    Number = reader.IsDBNull("Number") ? "" : reader.GetString("Number"),
                    Title = reader.GetString("Title"),
                    Content = reader.GetString("Content"),
                    Date = reader.IsDBNull("Date") ? 0 : reader.GetInt64("Date")
                });
            }
            chapters = chapters
                .OrderBy(ch => ParseVolume(ch.Number))
                .ThenBy(ch => ParseChapterNumber(ch.Number))
                .ThenBy(ch => ch.Id)
                .ToList();

            return chapters;
        }

        private static decimal ParseVolume(string number)
        {
            if (string.IsNullOrEmpty(number)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(number, @"Том\s*([0-9.,]+)");
            if (m.Success && decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
                return v;
            return 0;
        }
        private static decimal ParseChapterNumber(string number)
        {
            if (string.IsNullOrEmpty(number)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(number, @"Глава\s*([0-9.,]+)");
            if (m.Success && decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
                return v;
            m = System.Text.RegularExpressions.Regex.Match(number, @"\b([0-9.,]+)\b");
            if (m.Success && decimal.TryParse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v2))
                return v2;
            return 9999;
        }

        public Chapter GetChapter(int chapterId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, NovelId, Number, Title, Content, Date FROM Chapters WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", chapterId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new Chapter
                {
                    Id = reader.GetInt32("Id"),
                    NovelId = reader.GetInt32("NovelId"),
                    Number = reader.IsDBNull("Number") ? "" : reader.GetString("Number"),
                    Title = reader.GetString("Title"),
                    Content = reader.GetString("Content"),
                    Date = reader.IsDBNull("Date") ? 0 : reader.GetInt64("Date")
                };
            }
            return null;
        }

        public void CreateChapter(Chapter chapter)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Chapters (NovelId, Number, Title, Content, Date) VALUES (@novelId, @number, @title, @content, @date)";
            cmd.Parameters.AddWithValue("@novelId", chapter.NovelId);
            cmd.Parameters.AddWithValue("@number", chapter.Number ?? "");
            cmd.Parameters.AddWithValue("@title", chapter.Title);
            cmd.Parameters.AddWithValue("@content", chapter.Content ?? "");
            cmd.Parameters.AddWithValue("@date", chapter.Date);
            cmd.ExecuteNonQuery();
        }

        public void UpdateChapter(Chapter chapter)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Chapters SET Number=@number, Title=@title, Content=@content WHERE Id=@id";
            cmd.Parameters.AddWithValue("@number", chapter.Number ?? "");
            cmd.Parameters.AddWithValue("@title", chapter.Title);
            cmd.Parameters.AddWithValue("@content", chapter.Content ?? "");
            cmd.Parameters.AddWithValue("@id", chapter.Id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteChapter(int chapterId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Chapters WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", chapterId);
            cmd.ExecuteNonQuery();
        }

        // ---------- BOOKMARKS ----------
        public Dictionary<string, List<Bookmark>> GetBookmarks(int userId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT NovelId, ChapterId, Date FROM Bookmarks WHERE UserId = @userId";
            cmd.Parameters.AddWithValue("@userId", userId);

            var result = new Dictionary<string, List<Bookmark>>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var novelId = reader.GetInt32("NovelId").ToString();
                var chapterId = reader.GetInt32("ChapterId");
                var date = reader.IsDBNull("Date") ? 0 : reader.GetInt64("Date");
                if (!result.ContainsKey(novelId))
                    result[novelId] = new List<Bookmark>();
                result[novelId].Add(new Bookmark
                {
                    UserId = userId,
                    NovelId = int.Parse(novelId),
                    ChapterId = chapterId,
                    Date = date
                });
            }
            return result;
        }

        public void AddOrUpdateBookmark(int userId, int novelId, int chapterId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        INSERT INTO Bookmarks (UserId, NovelId, ChapterId, Date)
        VALUES (@userId, @novelId, @chapterId, @date)
        ON DUPLICATE KEY UPDATE ChapterId = @chapterId, Date = @date";
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@novelId", novelId);
            cmd.Parameters.AddWithValue("@chapterId", chapterId);
            cmd.Parameters.AddWithValue("@date", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            cmd.ExecuteNonQuery();
        }

        public List<Bookmark> GetLastBookmarks(int userId, int count = 10)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT NovelId, ChapterId, Date 
                        FROM Bookmarks 
                        WHERE UserId = @userId 
                        ORDER BY Date DESC 
                        LIMIT @count";
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@count", count);

            var list = new List<Bookmark>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new Bookmark
                {
                    UserId = userId,
                    NovelId = reader.GetInt32("NovelId"),
                    ChapterId = reader.GetInt32("ChapterId"),
                    Date = reader.IsDBNull("Date") ? 0 : reader.GetInt64("Date")
                });
            }
            return list;
        }

        public void RemoveBookmark(int userId, int novelId, int chapterId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Bookmarks WHERE UserId = @userId AND NovelId = @novelId AND ChapterId = @chapterId";
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@novelId", novelId);
            cmd.Parameters.AddWithValue("@chapterId", chapterId);
            cmd.ExecuteNonQuery();
        }

        // ---------- FAVORITES / STATUS ----------
        public string GetUserNovelStatus(int userId, int novelId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Status FROM Favorites WHERE UserId = @userId AND NovelId = @novelId";
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@novelId", novelId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return reader.IsDBNull("Status") ? null : reader.GetString("Status");
            return null;
        }

        public void UpdateUserNovelStatus(int userId, int novelId, string status)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Favorites WHERE UserId = @userId AND NovelId = @novelId";
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@novelId", novelId);
            var exists = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

            if (exists)
            {
                using var updateCmd = conn.CreateCommand();
                updateCmd.CommandText = "UPDATE Favorites SET Status = @status WHERE UserId = @userId AND NovelId = @novelId";
                updateCmd.Parameters.AddWithValue("@status", status);
                updateCmd.Parameters.AddWithValue("@userId", userId);
                updateCmd.Parameters.AddWithValue("@novelId", novelId);
                updateCmd.ExecuteNonQuery();
            }
            else
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = "INSERT INTO Favorites (UserId, NovelId, Status) VALUES (@userId, @novelId, @status)";
                insertCmd.Parameters.AddWithValue("@userId", userId);
                insertCmd.Parameters.AddWithValue("@novelId", novelId);
                insertCmd.Parameters.AddWithValue("@status", status);
                insertCmd.ExecuteNonQuery();
            }
        }

        // ---------- PROFILE ----------
        public (User user, Dictionary<string, List<Novel>> novelsByStatus) GetProfile(int userId)
        {
            using var conn = GetConnection();
            User user = null;
            using (var cmdUser = conn.CreateCommand())
            {
                cmdUser.CommandText = "SELECT Id, Login, Avatar FROM Users WHERE Id = @id";
                cmdUser.Parameters.AddWithValue("@id", userId);
                using var reader = cmdUser.ExecuteReader();
                if (reader.Read())
                {
                    user = new User
                    {
                        Id = reader.GetInt32("Id"),
                        Login = reader.GetString("Login"),
                        Avatar = !reader.IsDBNull("Avatar") ? (byte[])reader["Avatar"] : null
                    };
                }
            }
            var statuses = new[] { "reading", "read", "favorites", "abandoned" };
            var result = new Dictionary<string, List<Novel>>();

            foreach (var status in statuses)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT n.Id, n.Title, n.Covers 
                            FROM Novels n
                            JOIN Favorites f ON n.Id = f.NovelId
                            WHERE f.UserId = @userId AND f.Status = @status";
                cmd.Parameters.AddWithValue("@userId", userId);
                cmd.Parameters.AddWithValue("@status", status);

                var novels = new List<Novel>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    novels.Add(new Novel
                    {
                        Id = reader.GetInt32("Id"),
                        Title = reader.GetString("Title"),
                        Covers = reader.IsDBNull("Covers") ? null : reader.GetString("Covers")
                    });
                }
                result[status] = novels;
            }
            return (user, result);
        }

        // ---------- DOWNLOAD ----------
        public List<Chapter> GetChaptersByIds(IEnumerable<int> chapterIds)
        {
            if (chapterIds == null || !chapterIds.Any()) return new List<Chapter>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            var idList = chapterIds.ToList();
            cmd.CommandText = $"SELECT Id, Number, Title, Content FROM Chapters WHERE Id IN ({string.Join(",", idList.Select((_, i) => $"@id{i}"))}) ORDER BY Number";
            for (int i = 0; i < idList.Count; i++)
                cmd.Parameters.AddWithValue($"@id{i}", idList[i]);
            var chapters = new List<Chapter>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                chapters.Add(new Chapter
                {
                    Id = reader.GetInt32("Id"),
                    Number = reader.IsDBNull("Number") ? "" : reader.GetString("Number"),
                    Title = reader.GetString("Title"),
                    Content = reader.GetString("Content")
                });
            }
            return chapters;
        }


        // ---------- CATALOG ----------
        // Получить все уникальные жанры
        public List<string> GetAllGenres()
        {
            var novels = GetNovels();
            var genres = new HashSet<string>();
            foreach (var n in novels)
            {
                if (!string.IsNullOrWhiteSpace(n.Genres))
                {
                    foreach (var g in n.Genres.Split(','))
                        if (!string.IsNullOrWhiteSpace(g))
                            genres.Add(g.Trim());
                }
            }
            return genres.OrderBy(g => g).ToList();
        }

        // Получить все уникальные теги
        public List<string> GetAllTags()
        {
            var novels = GetNovels();
            var tags = new HashSet<string>();
            foreach (var n in novels)
            {
                if (!string.IsNullOrWhiteSpace(n.Tags))
                {
                    foreach (var t in n.Tags.Split(','))
                        if (!string.IsNullOrWhiteSpace(t))
                            tags.Add(t.Trim());
                }
            }
            return tags.OrderBy(t => t).ToList();
        }
    }

    // --- Модели (можно вынести отдельно) ---
    public class User
    {
        public int Id { get; set; }
        public string Login { get; set; }
        public string Password { get; set; }
        public string Role { get; set; }
        public byte[] Avatar { get; set; }
    }
    public class Novel
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Covers { get; set; }
        public string Genres { get; set; }
        public string Tags { get; set; }
        public string Type { get; set; }
        public string Format { get; set; }
        public int? ReleaseYear { get; set; }
        public int? AuthorId { get; set; }
        public string TranslatorId { get; set; }
        public string AlternativeTitles { get; set; }
        public string RelatedNovelIds { get; set; }
        public long Date { get; set; }

        public List<string> CoversList
        {
            get => string.IsNullOrWhiteSpace(Covers)
                ? new List<string>()
                : System.Text.Json.JsonSerializer.Deserialize<List<string>>(Covers);
            set => Covers = System.Text.Json.JsonSerializer.Serialize(value ?? new List<string>());
        }
    }
    public class Chapter
    {
        public int Id { get; set; }
        public int NovelId { get; set; }
        public string Number { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public long Date { get; set; }
    }
}