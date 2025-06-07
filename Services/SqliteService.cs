using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Linq;

namespace BulbaLib.Services
{
    public class SqliteService
    {
        private readonly string _connectionString;

        public SqliteService(string connectionString)
        {
            _connectionString = connectionString;
        }

        private SqliteConnection GetConnection()
        {
            var conn = new SqliteConnection(_connectionString);
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
                    Id = reader.GetInt32(0),
                    Login = reader.GetString(1),
                    Password = reader.GetString(2),
                    Role = reader.GetString(3),
                    Avatar = !reader.IsDBNull(4) ? (byte[])reader["Avatar"] : null
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
                    Id = reader.GetInt32(0),
                    Login = reader.GetString(1),
                    Avatar = !reader.IsDBNull(2) ? (byte[])reader["Avatar"] : null
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
        public List<Novel> GetNovels(string search = "")
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            if (!string.IsNullOrWhiteSpace(search))
            {
                cmd.CommandText = "SELECT Id, Title, Description, Image, Genres, Tags FROM Novels WHERE LOWER(Title) LIKE @search";
                cmd.Parameters.AddWithValue("@search", $"%{search.ToLower()}%");
            }
            else
            {
                cmd.CommandText = "SELECT Id, Title, Description, Image, Genres, Tags FROM Novels";
            }

            var novels = new List<Novel>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                novels.Add(new Novel
                {
                    Id = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Image = !reader.IsDBNull(3) ? (byte[])reader["Image"] : null,
                    Genres = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Tags = reader.IsDBNull(5) ? "" : reader.GetString(5),
                });
            }
            return novels;
        }

        public Novel GetNovel(int id)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Title, Description, Image, Genres, Tags FROM Novels WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new Novel
                {
                    Id = reader.GetInt32(0),
                    Title = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    Image = !reader.IsDBNull(3) ? (byte[])reader["Image"] : null,
                    Genres = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Tags = reader.IsDBNull(5) ? "" : reader.GetString(5),
                };
            }
            return null;
        }

        public void CreateNovel(Novel novel)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Novels (Title, Description, Image, Genres, Tags) VALUES (@title, @desc, @img, @genres, @tags)";
            cmd.Parameters.AddWithValue("@title", novel.Title);
            cmd.Parameters.AddWithValue("@desc", novel.Description ?? "");
            cmd.Parameters.AddWithValue("@img", novel.Image ?? new byte[0]);
            cmd.Parameters.AddWithValue("@genres", novel.Genres ?? "");
            cmd.Parameters.AddWithValue("@tags", novel.Tags ?? "");
            cmd.ExecuteNonQuery();
        }

        public void UpdateNovel(Novel novel)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE Novels SET Title=@title, Description=@desc, Image=@img, Genres=@genres, Tags=@tags WHERE Id=@id";
            cmd.Parameters.AddWithValue("@title", novel.Title);
            cmd.Parameters.AddWithValue("@desc", novel.Description ?? "");
            cmd.Parameters.AddWithValue("@img", novel.Image ?? new byte[0]);
            cmd.Parameters.AddWithValue("@genres", novel.Genres ?? "");
            cmd.Parameters.AddWithValue("@tags", novel.Tags ?? "");
            cmd.Parameters.AddWithValue("@id", novel.Id);
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
            cmd.CommandText = "SELECT Id, NovelId, Number, Title, Content, Date FROM Chapters WHERE NovelId = @novelId ORDER BY Number";
            cmd.Parameters.AddWithValue("@novelId", novelId);

            var chapters = new List<Chapter>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                chapters.Add(new Chapter
                {
                    Id = reader.GetInt32(0),
                    NovelId = reader.GetInt32(1),
                    Number = reader.GetInt32(2),
                    Title = reader.GetString(3),
                    Content = reader.GetString(4),
                    Date = reader.IsDBNull(5) ? 0 : reader.GetInt64(5)
                });
            }
            return chapters;
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
                    Id = reader.GetInt32(0),
                    NovelId = reader.GetInt32(1),
                    Number = reader.GetInt32(2),
                    Title = reader.GetString(3),
                    Content = reader.GetString(4),
                    Date = reader.IsDBNull(5) ? 0 : reader.GetInt64(5)
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
            cmd.Parameters.AddWithValue("@number", chapter.Number);
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
            cmd.Parameters.AddWithValue("@number", chapter.Number);
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
        public Dictionary<string, List<int>> GetBookmarks(int userId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT NovelId, ChapterId FROM Bookmarks WHERE UserId = @userId";
            cmd.Parameters.AddWithValue("@userId", userId);

            var result = new Dictionary<string, List<int>>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var novelId = reader.GetInt32(0).ToString();
                var chapterId = reader.GetInt32(1);
                if (!result.ContainsKey(novelId))
                    result[novelId] = new List<int>();
                result[novelId].Add(chapterId);
            }
            return result;
        }

        public void AddOrUpdateBookmark(int userId, int novelId, int chapterId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO Bookmarks (UserId, NovelId, ChapterId) VALUES (@userId, @novelId, @chapterId)";
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@novelId", novelId);
            cmd.Parameters.AddWithValue("@chapterId", chapterId);
            cmd.ExecuteNonQuery();
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
                return reader.IsDBNull(0) ? null : reader.GetString(0);
            return null;
        }

        public void UpdateUserNovelStatus(int userId, int novelId, string status)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            // Check existence
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
                        Id = reader.GetInt32(0),
                        Login = reader.GetString(1),
                        Avatar = !reader.IsDBNull(2) ? (byte[])reader["Avatar"] : null
                    };
                }
            }
            var statuses = new[] { "reading", "read", "favorites", "abandoned" };
            var result = new Dictionary<string, List<Novel>>();

            foreach (var status in statuses)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT n.Id, n.Title, n.Image 
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
                        Id = reader.GetInt32(0),
                        Title = reader.GetString(1),
                        Image = !reader.IsDBNull(2) ? (byte[])reader["Image"] : null
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
                    Id = reader.GetInt32(0),
                    Number = reader.GetInt32(1),
                    Title = reader.GetString(2),
                    Content = reader.GetString(3)
                });
            }
            return chapters;
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
        public byte[] Image { get; set; }
        public string Genres { get; set; }
        public string Tags { get; set; }
    }
    public class Chapter
    {
        public int Id { get; set; }
        public int NovelId { get; set; }
        public int Number { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public long Date { get; set; }
    }
}