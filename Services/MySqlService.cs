using System;
using System.Collections.Generic;
using System.Data;
using MySql.Data.MySqlClient;
using System.IO;
using System.Linq;
using BulbaLib.Models;
using System.Text.Json; // Added for potential JSON operations, though not strictly needed for current RequestData handling

namespace BulbaLib.Services
{
    public partial class MySqlService // Consider making this partial if GetUserIdsSubscribedToNovel is in a separate file
    {
        private readonly string _connectionString;

        public MySqlService(string connectionString)
        {
            _connectionString = connectionString;
            InitializeDatabaseSchema();
        }

        private void InitializeDatabaseSchema()
        {
            using var conn = GetConnection();
            // Check and add IsBlocked column to Users table
            try
            {
                using var cmdCheckUserColumn = conn.CreateCommand();
                cmdCheckUserColumn.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Users' AND COLUMN_NAME = 'IsBlocked'";
                var columnExists = Convert.ToInt32(cmdCheckUserColumn.ExecuteScalar()) > 0;
                if (!columnExists)
                {
                    using var cmdAlterUser = conn.CreateCommand();
                    cmdAlterUser.CommandText = "ALTER TABLE Users ADD COLUMN IsBlocked BOOLEAN DEFAULT FALSE";
                    cmdAlterUser.ExecuteNonQuery();
                }
            }
            catch (MySqlException ex)
            {
                // Log or handle exception (e.g., if permissions are insufficient)
                Console.WriteLine($"Error checking/altering Users table: {ex.Message}");
            }

            // Check and create ModerationRequests table
            try
            {
                using var cmdCheckTable = conn.CreateCommand();
                cmdCheckTable.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ModerationRequests'";
                var tableExists = Convert.ToInt32(cmdCheckTable.ExecuteScalar()) > 0;
                if (!tableExists)
                {
                    using var cmdCreateTable = conn.CreateCommand();
                    cmdCreateTable.CommandText = @"
                        CREATE TABLE ModerationRequests (
                            Id INT PRIMARY KEY AUTO_INCREMENT,
                            RequestType VARCHAR(50) NOT NULL,
                            UserId INT NOT NULL,
                            NovelId INT NULL,
                            ChapterId INT NULL,
                            RequestData TEXT NULL,
                            Status VARCHAR(50) NOT NULL,
                            CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                            ModeratorId INT NULL,
                            ModerationComment TEXT NULL,
                            UpdatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                            FOREIGN KEY (UserId) REFERENCES Users(Id),
                            FOREIGN KEY (NovelId) REFERENCES Novels(Id) ON DELETE SET NULL,
                            FOREIGN KEY (ChapterId) REFERENCES Chapters(Id) ON DELETE SET NULL,
                            FOREIGN KEY (ModeratorId) REFERENCES Users(Id) ON DELETE SET NULL
                        );";
                    cmdCreateTable.ExecuteNonQuery();
                }
            }
            catch (MySqlException ex)
            {
                // Log or handle exception
                Console.WriteLine($"Error creating ModerationRequests table: {ex.Message}");
            }

            // Check and create Notifications table
            try
            {
                using var cmdCheckNotificationTable = conn.CreateCommand();
                cmdCheckNotificationTable.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Notifications'";
                var notificationTableExists = Convert.ToInt32(cmdCheckNotificationTable.ExecuteScalar()) > 0;
                if (!notificationTableExists)
                {
                    using var cmdCreateNotificationTable = conn.CreateCommand();
                    cmdCreateNotificationTable.CommandText = @"
                        CREATE TABLE Notifications (
                            Id INT PRIMARY KEY AUTO_INCREMENT,
                            UserId INT NOT NULL,
                            Type VARCHAR(50) NOT NULL,
                            Message TEXT NOT NULL,
                            RelatedItemId INT NULL,
                            RelatedItemType VARCHAR(50) NULL,
                            IsRead BOOLEAN NOT NULL DEFAULT FALSE,
                            CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                            FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
                        );";
                    cmdCreateNotificationTable.ExecuteNonQuery();
                }
            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"Error creating Notifications table: {ex.Message}");
            }
        }

        internal MySqlConnection GetConnection()
        {
            var conn = new MySqlConnection(_connectionString);
            conn.Open();
            return conn;
        }

        // ---------- USERS ----------
        public List<User> SearchUsersByLogin(string nameQuery)
        {
            if (string.IsNullOrWhiteSpace(nameQuery))
            {
                return new List<User>();
            }
            var users = new List<User>();
            using (var connection = GetConnection()) // GetConnection() is your method to get MySqlConnection
            {
                // connection.Open(); // GetConnection already opens it
                // IMPORTANT: Use parameterized queries to prevent SQL injection.
                string query = "SELECT Id, Login, Avatar FROM Users WHERE LOWER(Login) LIKE @query LIMIT 10";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@query", "%" + nameQuery.ToLower() + "%");
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            users.Add(new User
                            {
                                Id = reader.GetInt32("Id"),
                                Login = reader.GetString("Login"),
                                // Avatar in User model is byte[], but in JS we need a URL.
                                // This method returns User model, controller will adapt it.
                                Avatar = reader.IsDBNull(reader.GetOrdinal("Avatar")) ? null : (byte[])reader["Avatar"]
                            });
                        }
                    }
                }
            }
            return users;
        }

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
            cmd.CommandText = "INSERT INTO Users (Login, Password, Role, Avatar, IsBlocked) VALUES (@login, @password, 'User', @avatar, FALSE)";
            cmd.Parameters.AddWithValue("@login", login);
            cmd.Parameters.AddWithValue("@password", password);
            cmd.Parameters.AddWithValue("@avatar", avatar);
            cmd.ExecuteNonQuery();
        }

        public User AuthenticateUser(string login, string password)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Login, Password, Role, Avatar, IsBlocked FROM Users WHERE Login = @login AND Password = @password";
            cmd.Parameters.AddWithValue("@login", login);
            cmd.Parameters.AddWithValue("@password", password);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var user = new User
                {
                    Id = reader.GetInt32("Id"),
                    Login = reader.GetString("Login"),
                    Password = reader.GetString("Password"),
                    Role = Enum.Parse<UserRole>(reader.GetString("Role"), true),
                    Avatar = !reader.IsDBNull("Avatar") ? (byte[])reader["Avatar"] : null,
                    IsBlocked = reader.GetBoolean("IsBlocked")
                };

                if (user.IsBlocked)
                {
                    return null; // Authentication failed for blocked user
                }
                return user;
            }
            return null;
        }

        public User GetUser(int userId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Login, Avatar, Role, IsBlocked FROM Users WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", userId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new User
                {
                    Id = reader.GetInt32("Id"),
                    Login = reader.GetString("Login"),
                    Avatar = !reader.IsDBNull("Avatar") ? (byte[])reader["Avatar"] : null,
                    Role = Enum.Parse<UserRole>(reader.GetString("Role"), true),
                    IsBlocked = reader.GetBoolean("IsBlocked")
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

        public void UpdateUserRole(int userId, string newRole)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET Role = @newRole WHERE Id = @userId";
            cmd.Parameters.AddWithValue("@newRole", newRole);
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.ExecuteNonQuery();
        }

        public void SetUserBlockedStatus(int userId, bool isBlocked)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Users SET IsBlocked = @isBlocked WHERE Id = @userId";
            cmd.Parameters.AddWithValue("@isBlocked", isBlocked);
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.ExecuteNonQuery();
        }

        public List<User> GetAllUsers()
        {
            var users = new List<User>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Login, Avatar, Role, IsBlocked FROM Users ORDER BY Login";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                users.Add(new User
                {
                    Id = reader.GetInt32("Id"),
                    Login = reader.GetString("Login"),
                    Avatar = !reader.IsDBNull("Avatar") ? (byte[])reader["Avatar"] : null,
                    Role = Enum.Parse<UserRole>(reader.GetString("Role"), true),
                    IsBlocked = reader.GetBoolean("IsBlocked")
                });
            }
            return users;
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
                            WHERE MATCH(Title, Genres, Tags) AGAINST (@search IN NATURAL LANGUAGE MODE)";
                cmd.Parameters.AddWithValue("@search", search);
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

        public List<Novel> SearchNovelsByTitle(string titleQuery, int limit)
        {
            if (string.IsNullOrWhiteSpace(titleQuery))
            {
                return new List<Novel>();
            }
            var novels = new List<Novel>();
            using (var connection = GetConnection())
            {
                string query = "SELECT Id, Title, Covers FROM Novels WHERE LOWER(Title) LIKE @query ORDER BY Title LIMIT @limit";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@query", "%" + titleQuery.ToLower() + "%");
                    command.Parameters.AddWithValue("@limit", limit);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            novels.Add(new Novel
                            {
                                Id = reader.GetInt32("Id"),
                                Title = reader.GetString("Title"),
                                Covers = reader.IsDBNull(reader.GetOrdinal("Covers")) ? null : reader.GetString("Covers")
                                // Other properties will be default/null
                            });
                        }
                    }
                }
            }
            return novels;
        }

        public int CreateNovel(Novel novel)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO Novels 
                (Title, Description, Covers, Genres, Tags, Type, Format, ReleaseYear, AuthorId, TranslatorId, AlternativeTitles, Date, RelatedNovelIds)
                VALUES (@title, @desc, @covers, @genres, @tags, @type, @format, @releaseYear, @authorId, @translatorId, @altTitles, @date, @relatedNovelIds);
                SELECT LAST_INSERT_ID();";
            cmd.Parameters.AddWithValue("@title", novel.Title);
            cmd.Parameters.AddWithValue("@desc", novel.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@covers", novel.Covers ?? "[]");
            cmd.Parameters.AddWithValue("@genres", novel.Genres ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@tags", novel.Tags ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@type", novel.Type ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@format", novel.Format ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@releaseYear", novel.ReleaseYear.HasValue ? novel.ReleaseYear.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@authorId", novel.AuthorId.HasValue ? novel.AuthorId.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@translatorId", string.IsNullOrEmpty(novel.TranslatorId) ? (object)DBNull.Value : novel.TranslatorId);
            cmd.Parameters.AddWithValue("@altTitles", novel.AlternativeTitles ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@relatedNovelIds", string.IsNullOrEmpty(novel.RelatedNovelIds) ? (object)DBNull.Value : novel.RelatedNovelIds);
            cmd.Parameters.AddWithValue("@date", novel.Date);

            var result = cmd.ExecuteScalar();
            return Convert.ToInt32(result);
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
            // IMPORTANT: This method only deletes the novel entry itself.
            // For full data integrity, ensure that related chapters, favorites, and bookmarks
            // are deleted either via database CASCADE constraints (recommended)
            // or by adding explicit deletion logic here if cascades are not configured.
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
            // IMPORTANT: This method only deletes the chapter entry itself.
            // For full data integrity, ensure that related bookmarks
            // are deleted either via database CASCADE constraints (recommended)
            // or by adding explicit deletion logic here if cascades are not configured.
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

        public List<int> GetUserIdsSubscribedToNovel(int novelId, List<string> statuses)
        {
            var userIds = new List<int>();
            if (statuses == null || !statuses.Any())
            {
                return userIds;
            }

            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();

            var statusParams = new List<string>();
            for (int i = 0; i < statuses.Count; i++)
            {
                var paramName = $"@status{i}";
                cmd.Parameters.AddWithValue(paramName, statuses[i]);
                statusParams.Add(paramName);
            }
            var statusInClause = string.Join(",", statusParams);

            cmd.CommandText = $"SELECT UserId FROM Favorites WHERE NovelId = @novelId AND Status IN ({statusInClause})";
            cmd.Parameters.AddWithValue("@novelId", novelId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                userIds.Add(reader.GetInt32("UserId"));
            }
            return userIds.Distinct().ToList();
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

        // ---------- MODERATION REQUESTS ----------

        public List<ModerationRequest> GetPendingModerationRequestsByType(List<ModerationRequestType> types, int limit, int offset)
        {
            var requests = new List<ModerationRequest>();
            if (types == null || !types.Any()) return requests;

            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();

            var typeParams = new List<string>();
            for (int i = 0; i < types.Count; i++)
            {
                typeParams.Add($"@type{i}");
                cmd.Parameters.AddWithValue($"@type{i}", types[i].ToString());
            }

            // The UserLogin currently is not fetched by MapReaderToModerationRequest without model change.
            // For this step, we proceed, and if UserLogin is needed in view, it has to be fetched separately or model adjusted.
            // The SQL query includes u.Login as UserLogin, so it's available if the mapper/model handles it.
            cmd.CommandText = $@"SELECT mr.*, u.Login as UserLogin 
                                FROM ModerationRequests mr
                                JOIN Users u ON mr.UserId = u.Id
                                WHERE mr.Status = @status AND mr.RequestType IN ({string.Join(",", typeParams)}) 
                                ORDER BY mr.CreatedAt DESC LIMIT @limit OFFSET @offset";
            cmd.Parameters.AddWithValue("@status", ModerationStatus.Pending.ToString());
            cmd.Parameters.AddWithValue("@limit", limit);
            cmd.Parameters.AddWithValue("@offset", offset);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var req = MapReaderToModerationRequest(reader);
                // Example if ModerationRequest had a UserLogin property (not adding it in this step per constraints):
                // if (reader.HasColumn("UserLogin")) req.UserLogin = reader.GetString("UserLogin");
                requests.Add(req);
            }
            return requests;
        }

        public int CountPendingModerationRequestsByType(List<ModerationRequestType> types)
        {
            if (types == null || !types.Any()) return 0;

            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();

            var typeParams = new List<string>();
            for (int i = 0; i < types.Count; i++)
            {
                typeParams.Add($"@type{i}");
                cmd.Parameters.AddWithValue($"@type{i}", types[i].ToString());
            }

            cmd.CommandText = $"SELECT COUNT(*) FROM ModerationRequests WHERE Status = @status AND RequestType IN ({string.Join(",", typeParams)})";
            cmd.Parameters.AddWithValue("@status", ModerationStatus.Pending.ToString());

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public int CreateModerationRequest(ModerationRequest request)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO ModerationRequests 
                                (RequestType, UserId, NovelId, ChapterId, RequestData, Status, CreatedAt, UpdatedAt) 
                                VALUES (@requestType, @userId, @novelId, @chapterId, @requestData, @status, @createdAt, @updatedAt);
                                SELECT LAST_INSERT_ID();";

            cmd.Parameters.AddWithValue("@requestType", request.RequestType.ToString());
            cmd.Parameters.AddWithValue("@userId", request.UserId);
            cmd.Parameters.AddWithValue("@novelId", request.NovelId.HasValue ? (object)request.NovelId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@chapterId", request.ChapterId.HasValue ? (object)request.ChapterId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@requestData", string.IsNullOrEmpty(request.RequestData) ? DBNull.Value : (object)request.RequestData);
            cmd.Parameters.AddWithValue("@status", request.Status.ToString());
            cmd.Parameters.AddWithValue("@createdAt", request.CreatedAt);
            cmd.Parameters.AddWithValue("@updatedAt", request.UpdatedAt);
            // ModeratorId and ModerationComment are not set on creation

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public ModerationRequest GetModerationRequestById(int id)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM ModerationRequests WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return MapReaderToModerationRequest(reader);
            }
            return null;
        }

        public List<ModerationRequest> GetPendingModerationRequests(int limit, int offset)
        {
            var requests = new List<ModerationRequest>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM ModerationRequests WHERE Status = @status ORDER BY CreatedAt DESC LIMIT @limit OFFSET @offset";
            cmd.Parameters.AddWithValue("@status", ModerationStatus.Pending.ToString());
            cmd.Parameters.AddWithValue("@limit", limit);
            cmd.Parameters.AddWithValue("@offset", offset);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                requests.Add(MapReaderToModerationRequest(reader));
            }
            return requests;
        }

        public List<ModerationRequest> GetModerationRequestsByUserId(int userId, int limit, int offset)
        {
            var requests = new List<ModerationRequest>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM ModerationRequests WHERE UserId = @userId ORDER BY CreatedAt DESC LIMIT @limit OFFSET @offset";
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@limit", limit);
            cmd.Parameters.AddWithValue("@offset", offset);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                requests.Add(MapReaderToModerationRequest(reader));
            }
            return requests;
        }

        public bool UpdateModerationRequest(ModerationRequest request)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE ModerationRequests SET 
                                Status = @status, 
                                ModeratorId = @moderatorId, 
                                ModerationComment = @moderationComment, 
                                UpdatedAt = @updatedAt 
                                WHERE Id = @id";

            cmd.Parameters.AddWithValue("@status", request.Status.ToString());
            cmd.Parameters.AddWithValue("@moderatorId", request.ModeratorId.HasValue ? (object)request.ModeratorId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@moderationComment", string.IsNullOrEmpty(request.ModerationComment) ? DBNull.Value : (object)request.ModerationComment);
            cmd.Parameters.AddWithValue("@updatedAt", request.UpdatedAt);
            cmd.Parameters.AddWithValue("@id", request.Id);

            return cmd.ExecuteNonQuery() > 0;
        }

        private ModerationRequest MapReaderToModerationRequest(MySqlDataReader reader)
        {
            return new ModerationRequest
            {
                Id = reader.GetInt32("Id"),
                RequestType = Enum.Parse<ModerationRequestType>(reader.GetString("RequestType")),
                UserId = reader.GetInt32("UserId"),
                NovelId = reader.IsDBNull("NovelId") ? (int?)null : reader.GetInt32("NovelId"),
                ChapterId = reader.IsDBNull("ChapterId") ? (int?)null : reader.GetInt32("ChapterId"),
                RequestData = reader.IsDBNull("RequestData") ? null : reader.GetString("RequestData"),
                Status = Enum.Parse<ModerationStatus>(reader.GetString("Status")),
                CreatedAt = reader.GetDateTime("CreatedAt"),
                ModeratorId = reader.IsDBNull("ModeratorId") ? (int?)null : reader.GetInt32("ModeratorId"),
                ModerationComment = reader.IsDBNull("ModerationComment") ? null : reader.GetString("ModerationComment"),
                UpdatedAt = reader.GetDateTime("UpdatedAt")
            };
        }
    }
}