using System;
using System.Collections.Generic;
using System.Data;
using MySql.Data.MySqlClient;
using System.IO;
using System.Linq;
using BulbaLib.Models;
using System.Text.Json; // Added for potential JSON operations, though not strictly needed for current RequestData handling
using Microsoft.Extensions.Logging; // Added for logging

namespace BulbaLib.Services
{
    public partial class MySqlService // Consider making this partial if GetUserIdsSubscribedToNovel is in a separate file
    {
        private readonly string _connectionString;
        private readonly FileService _fileService; // Added FileService
        private readonly ILogger<MySqlService> _logger;

        public MySqlService(string connectionString, FileService fileService, ILoggerFactory loggerFactory) // Modified constructor
        {
            _connectionString = connectionString;
            _fileService = fileService; // Initialize FileService
            _logger = loggerFactory.CreateLogger<MySqlService>();
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
                            FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE SET NULL,
                            FOREIGN KEY (NovelId) REFERENCES Novels(Id) ON DELETE SET NULL,
                            FOREIGN KEY (ChapterId) REFERENCES Chapters(Id) ON DELETE SET NULL,
                            FOREIGN KEY (ModeratorId) REFERENCES Users(Id) ON DELETE SET NULL
                        );";
                    cmdCreateTable.ExecuteNonQuery();
                }
                else // Table exists, check for RejectionReason column
                {
                    using var cmdCheckColumn = conn.CreateCommand();
                    cmdCheckColumn.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ModerationRequests' AND COLUMN_NAME = 'RejectionReason'";
                    var rejectionReasonColumnExists = Convert.ToInt32(cmdCheckColumn.ExecuteScalar()) > 0;
                    if (!rejectionReasonColumnExists)
                    {
                        using var cmdAlterTable = conn.CreateCommand();
                        cmdAlterTable.CommandText = "ALTER TABLE ModerationRequests ADD COLUMN RejectionReason TEXT NULL";
                        cmdAlterTable.ExecuteNonQuery();
                        Console.WriteLine("Successfully added RejectionReason column to ModerationRequests table.");
                    }
                }
            }
            catch (MySqlException ex)
            {
                // Log or handle exception
                Console.WriteLine($"Error creating or altering ModerationRequests table: {ex.Message}");
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
                            IsRead BOOLEAN NOT NULL DEFAULT FALSE, -- Будет удалено позже, если подтвердится ненадобность
                            CreatedAt TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                            Reason TEXT NULL, -- Новое поле для причины
                            FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
                        );";
                    cmdCreateNotificationTable.ExecuteNonQuery();
                }
                else // Таблица существует, проверяем наличие колонки Reason
                {
                    using var cmdCheckReasonColumn = conn.CreateCommand();
                    cmdCheckReasonColumn.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Notifications' AND COLUMN_NAME = 'Reason'";
                    var reasonColumnExists = Convert.ToInt32(cmdCheckReasonColumn.ExecuteScalar()) > 0;
                    if (!reasonColumnExists)
                    {
                        using var cmdAddReasonColumn = conn.CreateCommand();
                        cmdAddReasonColumn.CommandText = "ALTER TABLE Notifications ADD COLUMN Reason TEXT NULL";
                        cmdAddReasonColumn.ExecuteNonQuery();
                        Console.WriteLine("Successfully added Reason column to Notifications table.");
                    }
                }
            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"Error creating or altering Notifications table: {ex.Message}");
            }

            // Check and create NovelTranslators table
            try
            {
                using var cmdCheckNovelTranslatorsTable = conn.CreateCommand();
                cmdCheckNovelTranslatorsTable.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'NovelTranslators'";
                var novelTranslatorsTableExists = Convert.ToInt32(cmdCheckNovelTranslatorsTable.ExecuteScalar()) > 0;
                if (!novelTranslatorsTableExists)
                {
                    using var cmdCreateNovelTranslatorsTable = conn.CreateCommand();
                    cmdCreateNovelTranslatorsTable.CommandText = @"
                        CREATE TABLE NovelTranslators (
                            NovelId INT NOT NULL,
                            TranslatorId INT NOT NULL,
                            PRIMARY KEY (NovelId, TranslatorId),
                            FOREIGN KEY (NovelId) REFERENCES Novels(Id) ON DELETE CASCADE,
                            FOREIGN KEY (TranslatorId) REFERENCES Users(Id) ON DELETE CASCADE
                        );";
                    cmdCreateNovelTranslatorsTable.ExecuteNonQuery();
                }
            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"Error creating NovelTranslators table: {ex.Message}");
            }

            // Check and drop TranslatorId column from Novels table
            try
            {
                using var cmdCheckNovelColumn = conn.CreateCommand();
                cmdCheckNovelColumn.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Novels' AND COLUMN_NAME = 'TranslatorId'";
                var columnExists = Convert.ToInt32(cmdCheckNovelColumn.ExecuteScalar()) > 0;
                if (columnExists)
                {
                    using var cmdAlterNovel = conn.CreateCommand();
                    cmdAlterNovel.CommandText = "ALTER TABLE Novels DROP COLUMN TranslatorId";
                    cmdAlterNovel.ExecuteNonQuery();
                    Console.WriteLine("Successfully dropped TranslatorId column from Novels table.");
                }
            }
            catch (MySqlException ex)
            {
                // Log or handle exception
                Console.WriteLine($"Error checking/altering Novels table for TranslatorId column: {ex.Message}");
            }

            // Check and add Status column to Novels table
            try
            {
                using var cmdCheckNovelStatusColumn = conn.CreateCommand();
                cmdCheckNovelStatusColumn.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Novels' AND COLUMN_NAME = 'Status'";
                var statusColumnExists = Convert.ToInt32(cmdCheckNovelStatusColumn.ExecuteScalar()) > 0;
                if (!statusColumnExists)
                {
                    using var cmdAlterNovelStatus = conn.CreateCommand();
                    cmdAlterNovelStatus.CommandText = "ALTER TABLE Novels ADD COLUMN Status VARCHAR(50) NULL"; // Or NOT NULL DEFAULT 'Draft'
                    cmdAlterNovelStatus.ExecuteNonQuery();
                    Console.WriteLine("Successfully added Status column to Novels table.");
                }
                // If the column exists, ensure its type is compatible. This is harder to check programmatically for all cases.
                // We assume VARCHAR(50) is fine. If it was previously TEXT or a very different type, manual intervention might be needed.
            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"Error checking/altering Novels table for Status column: {ex.Message}");
            }

            // Check and add CreatorId column to Novels table
            try
            {
                using var cmdCheckNovelCreatorIdColumn = conn.CreateCommand();
                cmdCheckNovelCreatorIdColumn.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Novels' AND COLUMN_NAME = 'CreatorId'";
                var creatorIdColumnExists = Convert.ToInt32(cmdCheckNovelCreatorIdColumn.ExecuteScalar()) > 0;
                if (!creatorIdColumnExists)
                {
                    using var cmdAlterNovelCreatorId = conn.CreateCommand();
                    cmdAlterNovelCreatorId.CommandText = "ALTER TABLE Novels ADD COLUMN CreatorId INT NULL AFTER AuthorId";
                    cmdAlterNovelCreatorId.ExecuteNonQuery();
                    Console.WriteLine("Successfully added CreatorId column to Novels table.");
                }
            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"Error checking/altering Novels table for CreatorId column: {ex.Message}");
            }


            // Check and add CreatorId column to Chapters table
            try
            {
                using var cmdCheckChapterColumn = conn.CreateCommand();
                cmdCheckChapterColumn.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Chapters' AND COLUMN_NAME = 'CreatorId'";
                var columnExists = Convert.ToInt32(cmdCheckChapterColumn.ExecuteScalar()) > 0;
                if (!columnExists)
                {
                    using var cmdAlterChapter = conn.CreateCommand();
                    cmdAlterChapter.CommandText = "ALTER TABLE Chapters ADD COLUMN CreatorId INT NULL";
                    cmdAlterChapter.ExecuteNonQuery();
                    Console.WriteLine("Successfully added CreatorId column to Chapters table.");

                    // Optionally, add foreign key constraint
                    using var cmdAddConstraint = conn.CreateCommand();
                    cmdAddConstraint.CommandText = "ALTER TABLE Chapters ADD CONSTRAINT FK_Chapter_Creator FOREIGN KEY (CreatorId) REFERENCES Users(Id) ON DELETE SET NULL";
                    cmdAddConstraint.ExecuteNonQuery();
                    Console.WriteLine("Successfully added FK_Chapter_Creator constraint to Chapters table.");
                }
            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"Error checking/altering Chapters table for CreatorId column: {ex.Message}");
            }

            // Check and drop Content column from Chapters table
            try
            {
                using var cmdCheckChapterContentColumn = conn.CreateCommand();
                cmdCheckChapterContentColumn.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Chapters' AND COLUMN_NAME = 'Content'";
                var contentColumnExists = Convert.ToInt32(cmdCheckChapterContentColumn.ExecuteScalar()) > 0;
                if (contentColumnExists)
                {
                    using var cmdAlterChapterContent = conn.CreateCommand();
                    cmdAlterChapterContent.CommandText = "ALTER TABLE Chapters DROP COLUMN Content";
                    cmdAlterChapterContent.ExecuteNonQuery();
                    Console.WriteLine("Successfully dropped Content column from Chapters table.");
                }
            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"Error checking/altering Chapters table for Content column: {ex.Message}");
            }

            // Check and add ContentFilePath column to Chapters table
            try
            {
                using var cmdCheckChapterFilePathColumn = conn.CreateCommand();
                cmdCheckChapterFilePathColumn.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Chapters' AND COLUMN_NAME = 'ContentFilePath'";
                var filePathColumnExists = Convert.ToInt32(cmdCheckChapterFilePathColumn.ExecuteScalar()) > 0;
                if (!filePathColumnExists)
                {
                    using var cmdAlterChapterFilePath = conn.CreateCommand();
                    cmdAlterChapterFilePath.CommandText = "ALTER TABLE Chapters ADD COLUMN ContentFilePath VARCHAR(512) NULL";
                    cmdAlterChapterFilePath.ExecuteNonQuery();
                    Console.WriteLine("Successfully added ContentFilePath column to Chapters table.");
                }
            }
            catch (MySqlException ex)
            {
                Console.WriteLine($"Error checking/altering Chapters table for ContentFilePath column: {ex.Message}");
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

        public List<User> SearchUsersForAdmin(string searchTerm)
        {
            var users = new List<User>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            // Выбираем все необходимые поля для админ-панели
            cmd.CommandText = "SELECT Id, Login, Avatar, Role, IsBlocked FROM Users WHERE LOWER(Login) LIKE @searchTerm ORDER BY Login";
            cmd.Parameters.AddWithValue("@searchTerm", "%" + (searchTerm ?? "").ToLower() + "%");

            _logger.LogInformation("Executing SearchUsersForAdmin query with searchTerm: {SearchTerm}", searchTerm);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                users.Add(new User
                {
                    Id = reader.GetInt32("Id"),
                    Login = reader.GetString("Login"),
                    Avatar = !reader.IsDBNull(reader.GetOrdinal("Avatar")) ? (byte[])reader["Avatar"] : null,
                    Role = Enum.Parse<UserRole>(reader.GetString("Role"), true),
                    IsBlocked = reader.GetBoolean("IsBlocked")
                });
            }
            _logger.LogInformation("Found {UserCount} users for admin search term: {SearchTerm}", users.Count, searchTerm);
            return users;
        }

        public string GetUserLogin(int userId)
        {
            _logger.LogInformation("Entering GetUserLogin method for UserId: {UserId}", userId);
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Login FROM Users WHERE Id = @userId";
            cmd.Parameters.AddWithValue("@userId", userId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var login = reader.GetString("Login");
                _logger.LogInformation("User found for UserId: {UserId}. Login: {Login}", userId, login);
                return login;
            }
            else
            {
                _logger.LogWarning("User not found for UserId: {UserId}", userId);
                return null;
            }
        }

        // ---------- NOVEL TRANSLATORS ----------
        public void AddNovelTranslator(int novelId, int translatorId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            // Using IGNORE to prevent error if the pair already exists.
            cmd.CommandText = "INSERT IGNORE INTO NovelTranslators (NovelId, TranslatorId) VALUES (@novelId, @translatorId)";
            cmd.Parameters.AddWithValue("@novelId", novelId);
            cmd.Parameters.AddWithValue("@translatorId", translatorId);
            cmd.ExecuteNonQuery();
        }

        public void RemoveNovelTranslator(int novelId, int translatorId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM NovelTranslators WHERE NovelId = @novelId AND TranslatorId = @translatorId";
            cmd.Parameters.AddWithValue("@novelId", novelId);
            cmd.Parameters.AddWithValue("@translatorId", translatorId);
            cmd.ExecuteNonQuery();
        }

        public List<User> GetTranslatorsForNovel(int novelId)
        {
            var users = new List<User>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT u.Id, u.Login, u.Avatar, u.Role, u.IsBlocked 
                FROM Users u
                JOIN NovelTranslators nt ON u.Id = nt.TranslatorId
                WHERE nt.NovelId = @novelId";
            cmd.Parameters.AddWithValue("@novelId", novelId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                users.Add(new User
                {
                    Id = reader.GetInt32("Id"),
                    Login = reader.GetString("Login"),
                    Avatar = !reader.IsDBNull(reader.GetOrdinal("Avatar")) ? (byte[])reader["Avatar"] : null,
                    Role = Enum.Parse<UserRole>(reader.GetString("Role"), true),
                    IsBlocked = reader.GetBoolean("IsBlocked")
                });
            }
            return users;
        }

        public List<Novel> GetNovelsByTranslator(int translatorId)
        {
            var novels = new List<Novel>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT n.Id, n.Title, n.Covers, n.Status 
                FROM Novels n
                JOIN NovelTranslators nt ON n.Id = nt.NovelId
                WHERE nt.TranslatorId = @translatorId";
            cmd.Parameters.AddWithValue("@translatorId", translatorId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                novels.Add(new Novel
                {
                    Id = reader.GetInt32("Id"),
                    Title = reader.GetString("Title"),
                    Covers = reader.IsDBNull(reader.GetOrdinal("Covers")) ? null : reader.GetString("Covers"),
                    Status = reader.IsDBNull("Status") ? NovelStatus.Draft : Enum.Parse<NovelStatus>(reader.GetString("Status"), true)
                });
            }
            return novels;
        }

        // ---------- NOVELS ----------
        // --- Методы работы с Novel с учетом Covers ---

        public List<Novel> GetNovels(string search = "")
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            if (!string.IsNullOrWhiteSpace(search))
            {
                cmd.CommandText = @"SELECT Id, Title, Description, Covers, Genres, Tags, Type, Format, ReleaseYear, AuthorId, AlternativeTitles, RelatedNovelIds, Date, Status 
                            FROM Novels 
                            WHERE MATCH(Title, Genres, Tags) AGAINST (@search IN NATURAL LANGUAGE MODE)";
                cmd.Parameters.AddWithValue("@search", search);
            }
            else
            {
                cmd.CommandText = @"SELECT Id, Title, Description, Covers, Genres, Tags, Type, Format, ReleaseYear, AuthorId, AlternativeTitles, RelatedNovelIds, Date, Status 
                            FROM Novels";
            }

            var novels = new List<Novel>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var novel = new Novel
                {
                    Id = reader.GetInt32("Id"),
                    Title = reader.GetString("Title"),
                    Description = reader.IsDBNull("Description") ? "" : reader.GetString("Description"),
                    Covers = reader.IsDBNull("Covers") ? null : reader.GetString("Covers"),
                    // Genres and Tags are handled below
                    Type = reader.IsDBNull("Type") ? "" : reader.GetString("Type"),
                    Format = reader.IsDBNull("Format") ? "" : reader.GetString("Format"),
                    ReleaseYear = reader.IsDBNull("ReleaseYear") ? (int?)null : reader.GetInt32("ReleaseYear"),
                    AuthorId = reader.IsDBNull("AuthorId") ? (int?)null : reader.GetInt32("AuthorId"),
                    AlternativeTitles = reader.IsDBNull("AlternativeTitles") ? "" : reader.GetString("AlternativeTitles"),
                    RelatedNovelIds = reader.IsDBNull("RelatedNovelIds") ? "" : reader.GetString("RelatedNovelIds"),
                    Date = reader.IsDBNull("Date") ? 0 : reader.GetInt64("Date"),
                    Status = reader.IsDBNull("Status") ? NovelStatus.Draft : Enum.Parse<NovelStatus>(reader.GetString("Status"), true)
                };

                novel.Genres = reader.IsDBNull("Genres") ? null : reader.GetString("Genres");
                novel.Tags = reader.IsDBNull("Tags") ? null : reader.GetString("Tags");

                novels.Add(novel);
            }
            return novels;
        }

        public Novel GetNovel(int id)
        {
            _logger.LogInformation("Entering GetNovel method for Id: {NovelId}", id);
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Title, Description, Covers, Genres, Tags, Type, Format, ReleaseYear, AuthorId, CreatorId, AlternativeTitles, RelatedNovelIds, Date, Status FROM Novels WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var novel = new Novel
                {
                    Id = reader.GetInt32("Id"),
                    Title = reader.GetString("Title"),
                    Description = reader.IsDBNull("Description") ? "" : reader.GetString("Description"),
                    Covers = reader.IsDBNull("Covers") ? null : reader.GetString("Covers"),
                    // Genres and Tags are handled below
                    Type = reader.IsDBNull("Type") ? "" : reader.GetString("Type"),
                    Format = reader.IsDBNull("Format") ? "" : reader.GetString("Format"),
                    ReleaseYear = reader.IsDBNull("ReleaseYear") ? (int?)null : reader.GetInt32("ReleaseYear"),
                    AuthorId = reader.IsDBNull("AuthorId") ? (int?)null : reader.GetInt32("AuthorId"),
                    CreatorId = reader.IsDBNull("CreatorId") ? 0 : reader.GetInt32("CreatorId"),
                    AlternativeTitles = reader.IsDBNull("AlternativeTitles") ? "" : reader.GetString("AlternativeTitles"),
                    RelatedNovelIds = reader.IsDBNull("RelatedNovelIds") ? "" : reader.GetString("RelatedNovelIds"),
                    Status = reader.IsDBNull("Status") ? NovelStatus.Draft : Enum.Parse<NovelStatus>(reader.GetString("Status"), true)
                };

                novel.Genres = reader.IsDBNull("Genres") ? null : reader.GetString("Genres");
                novel.Tags = reader.IsDBNull("Tags") ? null : reader.GetString("Tags");

                _logger.LogInformation("Novel found for Id: {NovelId}. Title: {NovelTitle}", id, novel.Title);
                return novel;
            }
            _logger.LogWarning("Novel not found for Id: {NovelId}", id);
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
                    _logger.LogInformation("Executing SearchNovelsByTitle query: '{QueryText}' with query: {TitleQuery}, limit: {Limit}", command.CommandText, titleQuery, limit);
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
            _logger.LogInformation("Found {NovelCount} novels for title query: '{TitleQuery}' with limit: {Limit}", novels.Count, titleQuery, limit);
            return novels;
        }

        public int CreateNovel(Novel novel)
        {
            _logger.LogInformation("Entering CreateNovel method for novel Title: {NovelTitle}", novel.Title);
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO Novels 
                (Title, Description, Covers, Genres, Tags, Type, Format, ReleaseYear, AuthorId, CreatorId, AlternativeTitles, Date, RelatedNovelIds, Status)
                VALUES (@title, @desc, @covers, @genres, @tags, @type, @format, @releaseYear, @authorId, @creatorId, @altTitles, @date, @relatedNovelIds, @status);
                SELECT LAST_INSERT_ID();";
            _logger.LogDebug("CreateNovel parameters: Title={Title}, AuthorId={AuthorId}, CreatorId={CreatorId}, Status={Status}", novel.Title, novel.AuthorId, novel.CreatorId, novel.Status);

            List<string> parsedTags = ParseTagsStringForSavingInternal(novel.Tags);
            string tagsJsonToSave = parsedTags.Any() ? JsonSerializer.Serialize(parsedTags) : "[]";

            cmd.Parameters.AddWithValue("@title", novel.Title);
            cmd.Parameters.AddWithValue("@desc", novel.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@covers", novel.Covers ?? "[]");
            cmd.Parameters.AddWithValue("@genres", novel.Genres ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@tags", tagsJsonToSave);
            cmd.Parameters.AddWithValue("@type", novel.Type ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@format", novel.Format ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@releaseYear", novel.ReleaseYear.HasValue ? novel.ReleaseYear.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@authorId", novel.AuthorId.HasValue ? novel.AuthorId.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@creatorId", novel.CreatorId);
            cmd.Parameters.AddWithValue("@altTitles", novel.AlternativeTitles ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@relatedNovelIds", string.IsNullOrEmpty(novel.RelatedNovelIds) ? (object)DBNull.Value : novel.RelatedNovelIds);
            cmd.Parameters.AddWithValue("@date", novel.Date);
            cmd.Parameters.AddWithValue("@status", novel.Status.ToString());

            var result = cmd.ExecuteScalar();
            var newNovelId = Convert.ToInt32(result);
            _logger.LogInformation("Novel created with Id: {NovelId}, Title: {NovelTitle}", newNovelId, novel.Title);
            return newNovelId;
        }

        public void UpdateNovel(Novel novel)
        {
            _logger.LogInformation("Entering UpdateNovel method for novel Id: {NovelId}, Title: {NovelTitle}", novel.Id, novel.Title);
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
        AlternativeTitles=@altTitles,
        RelatedNovelIds=@relatedNovelIds,
        Status=@status 
        WHERE Id=@id";
            _logger.LogInformation("UpdateNovel: Received Novel Id {NovelId}, Title: \"{NovelTitle}\", Input novel.Tags: \"{InputTags}\"", novel.Id, novel.Title, novel.Tags);

            // The novel.Genres and novel.Tags are expected to be correctly formatted JSON array strings
            // by the time they reach this service method (e.g. "[]" or "[\"tag1\", \"tag2\"]"),
            // or null if that's the intended state.
            // The controller (NovelsController) is responsible for this preparation using SerializeTagsOrGenres.

            cmd.Parameters.AddWithValue("@title", novel.Title);
            cmd.Parameters.AddWithValue("@desc", novel.Description ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@covers", novel.Covers ?? "[]"); // Assumes novel.Covers is the final JSON string
            cmd.Parameters.AddWithValue("@genres", novel.Genres ?? (object)DBNull.Value); // Expects JSON array string or null
            cmd.Parameters.AddWithValue("@tags", novel.Tags ?? (object)DBNull.Value); // Expects JSON array string or null
            cmd.Parameters.AddWithValue("@type", novel.Type ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@format", novel.Format ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@releaseYear", novel.ReleaseYear.HasValue ? novel.ReleaseYear.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@id", novel.Id);
            cmd.Parameters.AddWithValue("@altTitles", novel.AlternativeTitles ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@authorId", novel.AuthorId.HasValue ? novel.AuthorId.Value : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@relatedNovelIds", string.IsNullOrEmpty(novel.RelatedNovelIds) ? (object)DBNull.Value : novel.RelatedNovelIds);
            cmd.Parameters.AddWithValue("@status", novel.Status.ToString());
            int rowsAffected = cmd.ExecuteNonQuery(); // Выполняем запрос
            _logger.LogInformation("UpdateNovel for Id: {NovelId} affected {RowsAffected} row(s). SQL: {SQL}", novel.Id, rowsAffected, cmd.CommandText);
        }

        public void DeleteNovel(int id)
        {
            _logger.LogInformation("Entering DeleteNovel method for novel Id: {NovelId}", id);
            // IMPORTANT: This method only deletes the novel entry itself.
            // For full data integrity, ensure that related chapters, favorites, and bookmarks
            // are deleted either via database CASCADE constraints (recommended)
            // or by adding explicit deletion logic here if cascades are not configured.
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Novels WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            int rowsAffected = cmd.ExecuteNonQuery();
            _logger.LogInformation("DeleteNovel for Id: {NovelId} affected {RowsAffected} row(s).", id, rowsAffected);
        }

        // ---------- CHAPTERS ----------
        public List<Chapter> GetChaptersByNovel(int novelId)
        {
            _logger.LogInformation("Entering GetChaptersByNovel method for NovelId: {NovelId}", novelId);
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            // Removed Content from SELECT, Added ContentFilePath
            cmd.CommandText = "SELECT Id, NovelId, Number, Title, Date, CreatorId, ContentFilePath FROM Chapters WHERE NovelId = @novelId";
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
                    // Content = reader.GetString("Content"), // Removed
                    Date = reader.IsDBNull("Date") ? 0 : reader.GetInt64("Date"),
                    CreatorId = reader.IsDBNull("CreatorId") ? (int?)null : reader.GetInt32("CreatorId"),
                    ContentFilePath = reader.IsDBNull("ContentFilePath") ? null : reader.GetString("ContentFilePath")
                });
            }
            chapters = chapters
                .OrderByDescending(ch => ExtractVolumeNumber(ch.Number).HasValue) // Главы с томом идут первыми
                .ThenBy(ch => ExtractVolumeNumber(ch.Number) ?? decimal.MaxValue) // Сортировка по номеру тома (если есть)
                .ThenBy(ch => ExtractChapterNumber(ch.Number) ?? decimal.MaxValue) // Сортировка по номеру главы
                .ThenBy(ch => ch.Id) // Для стабильности при одинаковых номерах
                .ToList();

            _logger.LogInformation("Found {ChapterCount} chapters for NovelId: {NovelId}", chapters.Count, novelId);
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

        // Новые методы для более точной сортировки
        private static decimal? ExtractVolumeNumber(string numberString)
        {
            if (string.IsNullOrWhiteSpace(numberString)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(numberString, @"Том\s*([0-9.,]+)");
            if (match.Success && decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var volume))
            {
                return volume;
            }
            return null;
        }

        private static decimal? ExtractChapterNumber(string numberString)
        {
            if (string.IsNullOrWhiteSpace(numberString)) return null;
            // Сначала ищем "Глава X.Y" или "Глава X"
            var chapterMatch = System.Text.RegularExpressions.Regex.Match(numberString, @"Глава\s*([0-9.,]+)");
            if (chapterMatch.Success && decimal.TryParse(chapterMatch.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var chapterNum))
            {
                return chapterNum;
            }

            // Если "Глава X" не найдено, но есть "Том X", ищем число после "Том X Глава Y" или просто "Том X Y"
            // Этот паттерн более сложный и может перекрываться с предыдущим, поэтому аккуратно.
            // Если есть "Том", но нет "Глава", ищем число в конце строки или перед нечисловыми символами (кроме точки/запятой).
            var volumeMatch = System.Text.RegularExpressions.Regex.Match(numberString, @"Том\s*[0-9.,]+\s*([0-9.,]+)");
            if (volumeMatch.Success && decimal.TryParse(volumeMatch.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var chapterNumAfterVolume))
            {
                return chapterNumAfterVolume;
            }

            // Если нет ни "Том", ни "Глава", ищем просто число в строке.
            // Это может быть просто "1", "2.5" и т.д.
            // Ищем число, которое может быть в начале строки или после не-цифрового символа (чтобы не брать часть другого числа)
            // и за которым может следовать не-цифровой символ или конец строки.
            var plainNumberMatch = System.Text.RegularExpressions.Regex.Match(numberString, @"(?:^|\D)([0-9.,]+)(?:\D|$)");
            if (plainNumberMatch.Success && plainNumberMatch.Groups[1].Value.Count(c => c == '.' || c == ',') <= 1) // Убедимся, что это похоже на число
            {
                if (decimal.TryParse(plainNumberMatch.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var plainNum))
                    return plainNum;
            }

            // Если в строке есть только "Том X" без явного номера главы, то номер главы можно считать 0 или null.
            // Для сортировки глав внутри одного тома, если номер главы не указан, они должны идти раньше глав с номером.
            // Однако, если номер главы не указан ВООБЩЕ (не только после "Том"), то это другое.
            // Текущая логика вернет null, если явный номер главы не найден.
            // При сортировке `?? decimal.MaxValue` такие главы (без явного номера) окажутся в конце, что может быть нежелательно, если они должны быть в начале.
            // Если главы типа "Том 1" без "Глава X" должны идти перед "Том 1 Глава 1", то нужно возвращать что-то вроде 0 или -1.
            // Пока оставляю null, что означает "неопределенный номер главы", и они будут в конце своей группы томов или в общей массе.

            return null; // Если номер главы не удалось извлечь
        }

        public void CreateChapter(Chapter chapter)
        {
            _logger.LogInformation("Entering CreateChapter method for NovelId: {NovelId}, Chapter Title: {ChapterTitle}", chapter.NovelId, chapter.Title);
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            // Added ContentFilePath to INSERT
            cmd.CommandText = "INSERT INTO Chapters (NovelId, Number, Title, Date, CreatorId, ContentFilePath) VALUES (@novelId, @number, @title, @date, @creatorId, @contentFilePath); SELECT LAST_INSERT_ID();";
            _logger.LogDebug("CreateChapter parameters: NovelId={NovelId}, Number={Number}, Title={Title}, CreatorId={CreatorId}", chapter.NovelId, chapter.Number, chapter.Title, chapter.CreatorId);
            cmd.Parameters.AddWithValue("@novelId", chapter.NovelId);
            cmd.Parameters.AddWithValue("@number", chapter.Number ?? "");
            cmd.Parameters.AddWithValue("@title", chapter.Title);
            cmd.Parameters.AddWithValue("@date", chapter.Date);
            cmd.Parameters.AddWithValue("@creatorId", chapter.CreatorId.HasValue ? (object)chapter.CreatorId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@contentFilePath", (object)chapter.ContentFilePath ?? DBNull.Value);
            var newChapterId = Convert.ToInt32(cmd.ExecuteScalar());
            chapter.Id = newChapterId; // Get new chapter ID
            _logger.LogInformation("Chapter created with Id: {ChapterId} for NovelId: {NovelId}, Title: {ChapterTitle}", newChapterId, chapter.NovelId, chapter.Title);
        }

        public void UpdateChapter(Chapter chapter)
        {
            _logger.LogInformation("Entering UpdateChapter method for Chapter Id: {ChapterId}, Title: {ChapterTitle}", chapter.Id, chapter.Title);
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            // Added ContentFilePath to UPDATE
            cmd.CommandText = "UPDATE Chapters SET Number=@number, Title=@title, Date=@date, CreatorId=@creatorId, ContentFilePath=@contentFilePath WHERE Id=@id";

            cmd.Parameters.AddWithValue("@id", chapter.Id);
            cmd.Parameters.AddWithValue("@number", chapter.Number ?? "");
            cmd.Parameters.AddWithValue("@title", chapter.Title);
            cmd.Parameters.AddWithValue("@date", chapter.Date);
            cmd.Parameters.AddWithValue("@creatorId", chapter.CreatorId.HasValue ? (object)chapter.CreatorId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@contentFilePath", (object)chapter.ContentFilePath ?? DBNull.Value);

            _logger.LogInformation("Executing UpdateChapter for Id: {ChapterId}. SQL: {SQLText}. Parameters: Id={@id}, Number='{Number}', Title='{Title}', Date={Date}, CreatorId={CreatorId}, ContentFilePath='{ContentFilePath}'",
                chapter.Id, cmd.CommandText, chapter.Id, chapter.Number ?? "", chapter.Title, chapter.Date, chapter.CreatorId.HasValue ? chapter.CreatorId.Value.ToString() : "NULL", chapter.ContentFilePath ?? "NULL");

            int rowsAffected = cmd.ExecuteNonQuery();
            _logger.LogInformation("UpdateChapter for Id: {ChapterId} affected {RowsAffected} row(s).", chapter.Id, rowsAffected);
        }

        public void DeleteChapter(int chapterId)
        {
            _logger.LogInformation("Entering DeleteChapter method for Chapter Id: {ChapterId}", chapterId);
            // IMPORTANT: This method only deletes the chapter entry itself.
            // For full data integrity, ensure that related bookmarks
            // are deleted either via database CASCADE constraints (recommended)
            // or by adding explicit deletion logic here if cascades are not configured.
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Chapters WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", chapterId);
            int rowsAffected = cmd.ExecuteNonQuery();
            _logger.LogInformation("DeleteChapter for Id: {ChapterId} affected {RowsAffected} row(s).", chapterId, rowsAffected);
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
                if (string.IsNullOrEmpty(status)) // Если статус пустой, удаляем запись
                {
                    using var deleteCmd = conn.CreateCommand();
                    deleteCmd.CommandText = "DELETE FROM Favorites WHERE UserId = @userId AND NovelId = @novelId";
                    deleteCmd.Parameters.AddWithValue("@userId", userId);
                    deleteCmd.Parameters.AddWithValue("@novelId", novelId);
                    deleteCmd.ExecuteNonQuery();
                }
                else // Иначе обновляем статус
                {
                    using var updateCmd = conn.CreateCommand();
                    updateCmd.CommandText = "UPDATE Favorites SET Status = @status WHERE UserId = @userId AND NovelId = @novelId";
                    updateCmd.Parameters.AddWithValue("@status", status);
                    updateCmd.Parameters.AddWithValue("@userId", userId);
                    updateCmd.Parameters.AddWithValue("@novelId", novelId);
                    updateCmd.ExecuteNonQuery();
                }
            }
            else if (!string.IsNullOrEmpty(status)) // Если записи нет и статус не пустой, создаем новую
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
            // Removed Content from SELECT
            cmd.CommandText = $"SELECT Id, Number, Title FROM Chapters WHERE Id IN ({string.Join(",", idList.Select((_, i) => $"@id{i}"))}) ORDER BY Number";
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
                    // Content = reader.GetString("Content") // Removed
                });
            }
            return chapters;
        }


        // ---------- CATALOG ----------
        // Получить все уникальные жанры
        public List<string> GetAllGenres()
        {
            _logger.LogInformation("Entering GetAllGenres method to collect all unique genres from novels.");
            var novels = GetNovels(); // This already logs internally if GetNovels has logging
            var uniqueGenres = new HashSet<string>();
            foreach (var n in novels)
            {
                if (string.IsNullOrWhiteSpace(n.Genres))
                {
                    // _logger.LogDebug("Novel Id {NovelId} has null or empty Genres string.", n.Id); // Optional: too verbose?
                    continue;
                }

                try
                {
                    var genreList = JsonSerializer.Deserialize<List<string>>(n.Genres);
                    if (genreList != null)
                    {
                        foreach (var g in genreList)
                        {
                            if (!string.IsNullOrWhiteSpace(g))
                            {
                                uniqueGenres.Add(g.Trim());
                            }
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning("Failed to deserialize Genres JSON for Novel Id {NovelId}. JSON: \"{GenreJson}\". Error: {ErrorMessage}. Treating as comma-separated string.", n.Id, n.Genres, ex.Message);
                    // Fallback: treat as comma-separated string
                    var genreParts = n.Genres.Split(',');
                    foreach (var part in genreParts)
                    {
                        var trimmedPart = part.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmedPart))
                        {
                            uniqueGenres.Add(trimmedPart);
                        }
                    }
                }
            }
            var result = uniqueGenres.OrderBy(g => g).ToList();
            _logger.LogInformation("Successfully collected {GenreCount} unique genres.", result.Count);
            return result;
        }

        // Получить все уникальные теги
        public List<string> GetAllTags()
        {
            _logger.LogInformation("Entering GetAllTags method to collect all unique tags from novels.");
            var novels = GetNovels();
            var uniqueTags = new HashSet<string>();
            foreach (var n in novels)
            {
                if (string.IsNullOrWhiteSpace(n.Tags))
                {
                    continue;
                }

                List<string> tagList = null;
                try
                {
                    // Прямая попытка десериализации, как в GetAllGenres
                    tagList = JsonSerializer.Deserialize<List<string>>(n.Tags);
                }
                catch (JsonException ex)
                {
                    // Логгируем ошибку JSON, но передаем n.Id и n.Tags как есть, без correctedTagsString
                    _logger.LogWarning(ex, "GetAllTags: Failed to deserialize Tags JSON for Novel Id {NovelId}. JSON: \"{TagJson}\". Treating as comma-separated string.", n.Id, n.Tags);
                    // Fallback: treat as comma-separated string, как в GetAllGenres
                    var tagParts = n.Tags.Split(',');
                    tagList = new List<string>();
                    foreach (var part in tagParts)
                    {
                        var trimmedPart = part.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmedPart))
                        {
                            tagList.Add(trimmedPart);
                        }
                    }
                }

                if (tagList != null)
                {
                    foreach (var t in tagList)
                    {
                        // Финальная очистка и добавление
                        var trimmedTag = t.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmedTag))
                        {
                            uniqueTags.Add(trimmedTag);
                        }
                    }
                }
            }
            var result = uniqueTags.OrderBy(g => g).ToList(); // Сортировка по алфавиту
            _logger.LogInformation("Successfully collected {TagCount} unique tags.", result.Count);
            return result;
        }

        // Вспомогательный метод для парсинга строки тегов перед сохранением (без ForceUTF8)
        private List<string> ParseTagsStringForSavingInternal(string tagsString)
        {
            if (string.IsNullOrWhiteSpace(tagsString))
            {
                return new List<string>();
            }

            List<string> tagList = null;
            try
            {
                // Попытка 1: Десериализовать как JSON-массив
                _logger.LogDebug("ParseTagsStringForSavingInternal: Attempting to deserialize as JSON array. Input: \"{InputString}\"", tagsString);
                tagList = JsonSerializer.Deserialize<List<string>>(tagsString);
                _logger.LogDebug("ParseTagsStringForSavingInternal: Successfully deserialized as JSON array.");
            }
            catch (JsonException ex1)
            {
                _logger.LogWarning(ex1, "ParseTagsStringForSavingInternal: Failed to deserialize as JSON array. Input: \"{InputString}\". Attempting as single JSON string or comma-separated.", tagsString);
                // Попытка 2: Если не JSON-массив, и строка в кавычках, попробовать как одиночную JSON-строку
                if (tagsString.StartsWith("\"") && tagsString.EndsWith("\"") && tagsString.Length > 1)
                {
                    try
                    {
                        _logger.LogDebug("ParseTagsStringForSavingInternal: Attempting to deserialize as single JSON string. Input: \"{InputString}\"", tagsString);
                        string singleJsonString = JsonSerializer.Deserialize<string>(tagsString);
                        _logger.LogDebug("ParseTagsStringForSavingInternal: Successfully deserialized as single JSON string. Content: \"{Content}\". Splitting by comma.", singleJsonString);
                        // Если внутри этой строки есть запятые, считаем их разделителями
                        tagList = singleJsonString.Split(',').Select(tag => tag.Trim()).Where(tag => !string.IsNullOrWhiteSpace(tag)).ToList();
                    }
                    catch (JsonException ex2)
                    {
                        _logger.LogWarning(ex2, "ParseTagsStringForSavingInternal: Failed to deserialize as single JSON string. Input: \"{InputString}\". Treating as raw comma-separated string.", tagsString);
                        // Если и это не удалось, значит это просто строка с запятыми (или без)
                        tagList = tagsString.Split(',').Select(tag => tag.Trim()).Where(tag => !string.IsNullOrWhiteSpace(tag)).ToList();
                    }
                }
                else
                {
                    _logger.LogDebug("ParseTagsStringForSavingInternal: Not a quoted string. Treating as raw comma-separated string. Input: \"{InputString}\"", tagsString);
                    // Попытка 3: Просто строка, разделенная запятыми
                    tagList = tagsString.Split(',').Select(tag => tag.Trim()).Where(tag => !string.IsNullOrWhiteSpace(tag)).ToList();
                }
            }

            // Финальная очистка: тримминг, удаление пустых строк и дубликатов
            var finalTagList = tagList?.Select(t => t.Trim()).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList() ?? new List<string>();
            _logger.LogDebug("ParseTagsStringForSavingInternal: Parsed tags for input \"{InputString}\": [{ParsedTags}]", tagsString, string.Join(", ", finalTagList));
            return finalTagList;
        }

        public List<Novel> GetNovelsByIds(List<int> ids)
        {
            _logger.LogInformation("Entering GetNovelsByIds method for {IdCount} IDs: {NovelIds}", ids?.Count ?? 0, ids != null ? string.Join(",", ids) : "null");
            var novels = new List<Novel>();
            if (ids == null || !ids.Any())
            {
                _logger.LogWarning("GetNovelsByIds called with null or empty ID list.");
                return novels;
            }

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                // Sanitize IDs by creating parameter placeholders to prevent SQL injection
                var parameters = new string[ids.Count];
                for (int i = 0; i < ids.Count; i++)
                {
                    parameters[i] = $"@id{i}";
                }
                string commandText = $"SELECT Id, Title, Description, Covers, Genres, Tags, Type, Format, ReleaseYear, AuthorId, AlternativeTitles, RelatedNovelIds, Date, Status FROM Novels WHERE Id IN ({string.Join(",", parameters)})";
                _logger.LogDebug("GetNovelsByIds executing query: {QueryText}", commandText);

                using (var command = new MySqlCommand(commandText, connection))
                {
                    for (int i = 0; i < ids.Count; i++)
                    {
                        command.Parameters.AddWithValue(parameters[i], ids[i]);
                    }

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var novel = new Novel
                            {
                                Id = reader.GetInt32("Id"),
                                Title = reader.IsDBNull(reader.GetOrdinal("Title")) ? null : reader.GetString("Title"),
                                Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString("Description"),
                                Covers = reader.IsDBNull(reader.GetOrdinal("Covers")) ? null : reader.GetString("Covers"),
                                // Genres and Tags are handled below
                                Type = reader.IsDBNull(reader.GetOrdinal("Type")) ? null : reader.GetString("Type"),
                                Format = reader.IsDBNull(reader.GetOrdinal("Format")) ? null : reader.GetString("Format"),
                                ReleaseYear = reader.IsDBNull(reader.GetOrdinal("ReleaseYear")) ? (int?)null : reader.GetInt32("ReleaseYear"),
                                AuthorId = reader.IsDBNull(reader.GetOrdinal("AuthorId")) ? (int?)null : reader.GetInt32("AuthorId"),
                                AlternativeTitles = reader.IsDBNull(reader.GetOrdinal("AlternativeTitles")) ? null : reader.GetString("AlternativeTitles"),
                                RelatedNovelIds = reader.IsDBNull(reader.GetOrdinal("RelatedNovelIds")) ? null : reader.GetString("RelatedNovelIds"),
                                Date = reader.IsDBNull(reader.GetOrdinal("Date")) ? 0 : reader.GetInt64("Date"),
                                Status = reader.IsDBNull("Status") ? NovelStatus.Draft : Enum.Parse<NovelStatus>(reader.GetString("Status"), true)
                            };

                            novel.Genres = reader.IsDBNull(reader.GetOrdinal("Genres")) ? null : reader.GetString(reader.GetOrdinal("Genres"));
                            novel.Tags = reader.IsDBNull(reader.GetOrdinal("Tags")) ? null : reader.GetString(reader.GetOrdinal("Tags"));

                            novels.Add(novel);
                        }
                    }
                }
            }
            _logger.LogInformation("Found {NovelCount} novels for the provided IDs.", novels.Count);
            return novels;
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
            _logger.LogInformation("Entering CreateModerationRequest method. RequestType: {RequestType}, UserId: {UserId}, NovelId: {NovelId}, ChapterId: {ChapterId}",
                request.RequestType, request.UserId, request.NovelId, request.ChapterId);
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO ModerationRequests 
                                (RequestType, UserId, NovelId, ChapterId, RequestData, Status, CreatedAt, UpdatedAt) 
                                VALUES (@requestType, @userId, @novelId, @chapterId, @requestData, @status, @createdAt, @updatedAt);
                                SELECT LAST_INSERT_ID();";
            _logger.LogDebug("CreateModerationRequest details: RequestData (first 100 chars): {RequestDataSnippet}, Status: {Status}",
                request.RequestData?.Substring(0, Math.Min(request.RequestData.Length, 100)), request.Status);

            cmd.Parameters.AddWithValue("@requestType", request.RequestType.ToString());
            cmd.Parameters.AddWithValue("@userId", request.UserId);
            cmd.Parameters.AddWithValue("@novelId", request.NovelId.HasValue ? (object)request.NovelId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@chapterId", request.ChapterId.HasValue ? (object)request.ChapterId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@requestData", string.IsNullOrEmpty(request.RequestData) ? DBNull.Value : (object)request.RequestData);
            cmd.Parameters.AddWithValue("@status", request.Status.ToString());
            cmd.Parameters.AddWithValue("@createdAt", request.CreatedAt);
            cmd.Parameters.AddWithValue("@updatedAt", request.UpdatedAt);
            // ModeratorId and ModerationComment are not set on creation

            var newRequestId = Convert.ToInt32(cmd.ExecuteScalar());
            _logger.LogInformation("ModerationRequest created with Id: {RequestId}", newRequestId);
            return newRequestId;
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
            _logger.LogDebug("UpdateModerationRequest parameters for Id={RequestId}: Status={Status}, ModeratorId={ModeratorId}",
                request.Id, request.Status, request.ModeratorId);

            cmd.Parameters.AddWithValue("@status", request.Status.ToString());
            cmd.Parameters.AddWithValue("@moderatorId", request.ModeratorId.HasValue ? (object)request.ModeratorId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@moderationComment", string.IsNullOrEmpty(request.ModerationComment) ? DBNull.Value : (object)request.ModerationComment);
            cmd.Parameters.AddWithValue("@updatedAt", request.UpdatedAt);
            cmd.Parameters.AddWithValue("@id", request.Id);

            var rowsAffected = cmd.ExecuteNonQuery() > 0;
            _logger.LogInformation("UpdateModerationRequest for Id: {RequestId} success: {Success}", request.Id, rowsAffected);
            return rowsAffected;
        }

        private ModerationRequest MapReaderToModerationRequest(System.Data.Common.DbDataReader reader)
        {
            // Get ordinals once
            int idOrdinal = reader.GetOrdinal("Id");
            int requestTypeOrdinal = reader.GetOrdinal("RequestType");
            int userIdOrdinal = reader.GetOrdinal("UserId");
            int novelIdOrdinal = reader.GetOrdinal("NovelId");
            int chapterIdOrdinal = reader.GetOrdinal("ChapterId");
            int requestDataOrdinal = reader.GetOrdinal("RequestData");
            int statusOrdinal = reader.GetOrdinal("Status");
            int createdAtOrdinal = reader.GetOrdinal("CreatedAt");
            int moderatorIdOrdinal = reader.GetOrdinal("ModeratorId");
            int moderationCommentOrdinal = reader.GetOrdinal("ModerationComment");
            int updatedAtOrdinal = reader.GetOrdinal("UpdatedAt");

            return new ModerationRequest
            {
                Id = reader.GetInt32(idOrdinal),
                RequestType = Enum.Parse<ModerationRequestType>(reader.GetString(requestTypeOrdinal)),
                UserId = reader.GetInt32(userIdOrdinal),
                NovelId = reader.IsDBNull(novelIdOrdinal) ? (int?)null : reader.GetInt32(novelIdOrdinal),
                ChapterId = reader.IsDBNull(chapterIdOrdinal) ? (int?)null : reader.GetInt32(chapterIdOrdinal),
                RequestData = reader.IsDBNull(requestDataOrdinal) ? null : reader.GetString(requestDataOrdinal),
                Status = Enum.Parse<ModerationStatus>(reader.GetString(statusOrdinal)),
                CreatedAt = reader.GetDateTime(createdAtOrdinal),
                ModeratorId = reader.IsDBNull(moderatorIdOrdinal) ? (int?)null : reader.GetInt32(moderatorIdOrdinal),
                ModerationComment = reader.IsDBNull(moderationCommentOrdinal) ? null : reader.GetString(moderationCommentOrdinal),
                UpdatedAt = reader.GetDateTime(updatedAtOrdinal)
            };
        }

        // ---------- NOTIFICATIONS ----------
        public int CreateNotification(Notification notification)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO Notifications 
                                (UserId, Type, Message, RelatedItemId, RelatedItemType, CreatedAt, Reason) 
                                VALUES (@userId, @type, @message, @relatedItemId, @relatedItemType, @createdAt, @reason);
                                SELECT LAST_INSERT_ID();";

            cmd.Parameters.AddWithValue("@userId", notification.UserId);
            cmd.Parameters.AddWithValue("@type", notification.Type.ToString());
            cmd.Parameters.AddWithValue("@message", notification.Message);
            cmd.Parameters.AddWithValue("@relatedItemId", notification.RelatedItemId.HasValue ? (object)notification.RelatedItemId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@relatedItemType", notification.RelatedItemType == RelatedItemType.None ? DBNull.Value : (object)notification.RelatedItemType.ToString());
            cmd.Parameters.AddWithValue("@createdAt", notification.CreatedAt);
            cmd.Parameters.AddWithValue("@reason", string.IsNullOrEmpty(notification.Reason) ? DBNull.Value : (object)notification.Reason); // Добавляем Reason

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public List<Notification> GetNotificationsByUserId(int userId, int limit, int offset)
        {
            var notifications = new List<Notification>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();

            string sql = "SELECT * FROM Notifications WHERE UserId = @userId ORDER BY CreatedAt DESC LIMIT @limit OFFSET @offset";

            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("@userId", userId);
            cmd.Parameters.AddWithValue("@limit", limit);
            cmd.Parameters.AddWithValue("@offset", offset);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                notifications.Add(MapReaderToNotification(reader));
            }
            return notifications;
        }

        private Notification MapReaderToNotification(System.Data.Common.DbDataReader reader)
        {
            // Get ordinals once
            int idOrdinal = reader.GetOrdinal("Id");
            int userIdOrdinal = reader.GetOrdinal("UserId");
            int typeOrdinal = reader.GetOrdinal("Type");
            int messageOrdinal = reader.GetOrdinal("Message");
            int relatedItemIdOrdinal = reader.GetOrdinal("RelatedItemId");
            int relatedItemTypeOrdinal = reader.GetOrdinal("RelatedItemType");
            // int isReadOrdinal = reader.GetOrdinal("IsRead"); // Removed
            int createdAtOrdinal = reader.GetOrdinal("CreatedAt");
            int reasonOrdinal = reader.GetOrdinal("Reason"); // Получаем индекс для Reason

            return new Notification
            {
                Id = reader.GetInt32(idOrdinal),
                UserId = reader.GetInt32(userIdOrdinal),
                Type = Enum.Parse<NotificationType>(reader.GetString(typeOrdinal)),
                Message = reader.GetString(messageOrdinal),
                RelatedItemId = reader.IsDBNull(relatedItemIdOrdinal) ? (int?)null : reader.GetInt32(relatedItemIdOrdinal),
                RelatedItemType = reader.IsDBNull(relatedItemTypeOrdinal) ? RelatedItemType.None : Enum.Parse<RelatedItemType>(reader.GetString(relatedItemTypeOrdinal)),
                // IsRead = reader.GetBoolean(isReadOrdinal), // Removed
                CreatedAt = reader.GetDateTime(createdAtOrdinal),
                Reason = reader.IsDBNull(reasonOrdinal) ? null : reader.GetString(reasonOrdinal) // Читаем Reason
            };
        }

        public List<ChapterModerationRequestViewModel> GetPendingChapterModerationRequestsWithDetails()
        {
            var requests = new List<ChapterModerationRequestViewModel>();
            using var conn = GetConnection();
            // Added N.Covers for NovelCoverImageUrl
            // Added C.Number as CurrentChapterNumber, C.Title as CurrentChapterTitle
            var query = @"
                SELECT 
                    mr.Id AS RequestId, 
                    mr.RequestType, 
                    u.Login AS UserLogin, 
                    mr.NovelId, 
                    N.Title AS NovelTitle, 
                    N.Covers AS NovelCovers,  -- Assuming Covers field stores JSON array with first image being primary
                    mr.ChapterId, 
                    C.Number AS CurrentChapterNumber, 
                    C.Title AS CurrentChapterTitle, 
                    mr.RequestData, 
                    mr.Status, -- Added Status
                    mr.UserId, -- Added UserId
                    mr.CreatedAt AS RequestedAt
                FROM ModerationRequests mr
                JOIN Users u ON mr.UserId = u.Id
                LEFT JOIN Novels N ON mr.NovelId = N.Id
                LEFT JOIN Chapters C ON mr.ChapterId = C.Id
                WHERE mr.Status = @Status AND mr.RequestType IN (@AddChapter, @EditChapter, @DeleteChapter)
                ORDER BY mr.CreatedAt DESC;
            ";

            using var cmd = new MySqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@Status", ModerationStatus.Pending.ToString());
            cmd.Parameters.AddWithValue("@AddChapter", ModerationRequestType.AddChapter.ToString());
            cmd.Parameters.AddWithValue("@EditChapter", ModerationRequestType.EditChapter.ToString());
            cmd.Parameters.AddWithValue("@DeleteChapter", ModerationRequestType.DeleteChapter.ToString());

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var requestType = Enum.Parse<ModerationRequestType>(reader.GetString("RequestType"));
                string requestDataJson = reader.IsDBNull("RequestData") ? null : reader.GetString("RequestData");

                string proposedNumber = null;
                string proposedTitle = null;

                if (!string.IsNullOrEmpty(requestDataJson) && (requestType == ModerationRequestType.AddChapter || requestType == ModerationRequestType.EditChapter))
                {
                    try
                    {
                        // For AddChapter, RequestData is Chapter (includes Number, Title, Content)
                        // For EditChapter, RequestData is Chapter (includes Id, Number, Title, Content)
                        // We only need Number and Title here for display.
                        var chapterDetails = JsonSerializer.Deserialize<Chapter>(requestDataJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        proposedNumber = chapterDetails?.Number;
                        proposedTitle = chapterDetails?.Title;
                    }
                    catch (JsonException ex)
                    {
                        // Log error or handle cases where RequestData might not be a valid Chapter JSON
                        Console.WriteLine($"Error deserializing RequestData for RequestId {reader.GetInt32("RequestId")}: {ex.Message}");
                        // For EditChapter, RequestData might also be ChapterEditModel in some older implementations,
                        // but the prompt and current controller logic points to `Chapter` for `EditChapter` moderation data.
                        // If it were ChapterEditModel or ChapterCreateModel, the deserialization target would change.
                        // For now, assuming Chapter is the correct target for both Add and Edit based on typical flow.
                    }
                }


                string novelCoversJson = reader.IsDBNull("NovelCovers") ? null : reader.GetString("NovelCovers");
                string firstCoverUrl = null;
                if (!string.IsNullOrEmpty(novelCoversJson))
                {
                    try
                    {
                        var covers = JsonSerializer.Deserialize<List<string>>(novelCoversJson);
                        if (covers != null && covers.Count > 0)
                        {
                            firstCoverUrl = covers[0]; // Assuming the first cover is the primary one
                            // Ensure it's a full URL or prepend base path if it's relative
                            if (!string.IsNullOrWhiteSpace(firstCoverUrl) && !firstCoverUrl.StartsWith("http"))
                            {
                                // This might need configuration for base URL if not stored as full URLs
                                // For now, assume it's either a full URL or a relative one that client can handle
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"Error deserializing NovelCovers for RequestId {reader.GetInt32("RequestId")}: {ex.Message}");
                    }
                }


                requests.Add(new ChapterModerationRequestViewModel
                {
                    RequestId = reader.GetInt32("RequestId"),
                    RequestType = requestType,
                    UserLogin = reader.GetString("UserLogin"),
                    NovelId = reader.IsDBNull("NovelId") ? 0 : reader.GetInt32("NovelId"), // Ensure non-null if possible
                    NovelTitle = reader.IsDBNull("NovelTitle") ? "N/A" : reader.GetString("NovelTitle"),
                    NovelCoverImageUrl = firstCoverUrl,
                    ChapterId = reader.IsDBNull(reader.GetOrdinal("ChapterId")) ? (int?)null : reader.GetInt32(reader.GetOrdinal("ChapterId")),

                    UserId = reader.GetInt32(reader.GetOrdinal("UserId")), // Added
                    RequesterLogin = reader.GetString(reader.GetOrdinal("UserLogin")), // Assuming UserLogin populates RequesterLogin for this list view
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("RequestedAt")), // RequestedAt is alias for mr.CreatedAt
                    RequestedAt = reader.GetDateTime(reader.GetOrdinal("RequestedAt")), // Explicitly map

                    RequestDataJson = requestDataJson, // Added
                    Status = reader.GetString(reader.GetOrdinal("Status")), // Added

                    // Populate generic ChapterNumber/Title based on context for the list
                    ChapterNumber = !string.IsNullOrEmpty(reader.IsDBNull(reader.GetOrdinal("CurrentChapterNumber")) ? null : reader.GetString(reader.GetOrdinal("CurrentChapterNumber")))
                                    ? reader.GetString(reader.GetOrdinal("CurrentChapterNumber"))
                                    : proposedNumber,
                    ChapterTitle = !string.IsNullOrEmpty(reader.IsDBNull(reader.GetOrdinal("CurrentChapterTitle")) ? null : reader.GetString(reader.GetOrdinal("CurrentChapterTitle")))
                                   ? reader.GetString(reader.GetOrdinal("CurrentChapterTitle"))
                                   : proposedTitle,

                    CurrentChapterNumber = reader.IsDBNull(reader.GetOrdinal("CurrentChapterNumber")) ? null : reader.GetString(reader.GetOrdinal("CurrentChapterNumber")),
                    CurrentChapterTitle = reader.IsDBNull(reader.GetOrdinal("CurrentChapterTitle")) ? null : reader.GetString(reader.GetOrdinal("CurrentChapterTitle")),

                    ProposedChapterNumber = proposedNumber,
                    ProposedChapterTitle = proposedTitle
                    // ExistingChapterData, ProposedChapterData, Content fields are typically too heavy for a list view and are omitted here.
                });
            }
            return requests;
        }

        public async Task<ModerationRequest> GetModerationRequestByIdAsync(int requestId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand(); // Assuming GetConnection opens the connection
            cmd.CommandText = "SELECT * FROM ModerationRequests WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", requestId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapReaderToModerationRequest(reader); // Existing helper method
            }
            return null;
        }

        public async Task<bool> UpdateModerationRequestStatusAsync(int requestId, ModerationStatus status, int moderatorId, string rejectionReason = null)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE ModerationRequests SET 
                                Status = @status, 
                                ModeratorId = @moderatorId, 
                                RejectionReason = @rejectionReason, 
                                UpdatedAt = @updatedAt 
                                WHERE Id = @id";

            cmd.Parameters.AddWithValue("@status", status.ToString());
            cmd.Parameters.AddWithValue("@moderatorId", moderatorId);
            cmd.Parameters.AddWithValue("@rejectionReason", (object)rejectionReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@id", requestId);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }


        public async Task<ChapterModerationRequestViewModel> GetChapterModerationRequestDetailsByIdAsync(int requestId)
        {
            ChapterModerationRequestViewModel requestDetails = null;
            using var conn = GetConnection();
            var query = @"
                SELECT 
                    mr.Id AS RequestId, 
                    mr.RequestType, 
                    u.Login AS UserLogin, 
                    mr.NovelId, 
                    N.Title AS NovelTitle, 
                    N.Covers AS NovelCovers,
                    mr.ChapterId, 
                    C.Number AS CurrentChapterNumber, 
                    C.Title AS CurrentChapterTitle,
                    C.ContentFilePath AS CurrentChapterContentFilePath,
                    mr.RequestData,
                    mr.Status, -- Added Status
                    mr.UserId, -- Added UserId
                    mr.CreatedAt AS RequestedAt
                FROM ModerationRequests mr
                JOIN Users u ON mr.UserId = u.Id
                LEFT JOIN Novels N ON mr.NovelId = N.Id
                LEFT JOIN Chapters C ON mr.ChapterId = C.Id
                WHERE mr.Id = @RequestId;
            ";

            using var cmd = new MySqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@RequestId", requestId);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var requestType = Enum.Parse<ModerationRequestType>(reader.GetString("RequestType"));
                string requestDataJson = reader.IsDBNull("RequestData") ? null : reader.GetString("RequestData");

                string proposedNumber = null;
                string proposedTitle = null;
                string proposedContent = null;
                Chapter deserializedProposedChapter = null; // Declare here

                if (!string.IsNullOrEmpty(requestDataJson) && (requestType == ModerationRequestType.AddChapter || requestType == ModerationRequestType.EditChapter))
                {
                    try
                    {
                        deserializedProposedChapter = JsonSerializer.Deserialize<Chapter>(requestDataJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (deserializedProposedChapter != null)
                        {
                            proposedNumber = deserializedProposedChapter.Number;
                            proposedTitle = deserializedProposedChapter.Title;
                            proposedContent = deserializedProposedChapter.Content; // Content is part of Chapter model when serialized
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"Error deserializing RequestData for RequestId {reader.GetInt32(reader.GetOrdinal("RequestId"))}: {ex.Message}");
                    }
                }

                string currentChapterContent = null;
                string currentContentFilePath = reader.IsDBNull("CurrentChapterContentFilePath") ? null : reader.GetString("CurrentChapterContentFilePath");
                if (requestType == ModerationRequestType.EditChapter || requestType == ModerationRequestType.DeleteChapter)
                {
                    if (!string.IsNullOrEmpty(currentContentFilePath))
                    {
                        // Ensure _fileService is available here. This requires _fileService to be passed to MySqlService constructor.
                        if (_fileService.ChapterFileExists(currentContentFilePath)) // Assuming ChapterFileExists takes a path relative to wwwroot or a full path
                        {
                            currentChapterContent = await _fileService.ReadChapterContentAsync(currentContentFilePath);
                        }
                        else
                        {
                            Console.WriteLine($"Current chapter content file not found at: {currentContentFilePath} for request ID {requestId}");
                        }
                    }
                    else if (requestType == ModerationRequestType.EditChapter && reader.IsDBNull("ChapterId")) // Edit request but no existing chapter (should not happen ideally)
                    {
                        Console.WriteLine($"Edit request {requestId} has no ChapterId, cannot fetch current content.");
                    }
                }

                string novelCoversJson = reader.IsDBNull("NovelCovers") ? null : reader.GetString("NovelCovers");
                string firstCoverUrl = null;
                if (!string.IsNullOrEmpty(novelCoversJson))
                {
                    try
                    {
                        var covers = JsonSerializer.Deserialize<List<string>>(novelCoversJson);
                        if (covers != null && covers.Count > 0) firstCoverUrl = covers[0];
                    }
                    catch (JsonException ex)
                    {
                        Console.WriteLine($"Error deserializing NovelCovers for RequestId {reader.GetInt32("RequestId")}: {ex.Message}");
                    }
                }

                requestDetails = new ChapterModerationRequestViewModel
                {
                    RequestId = reader.GetInt32("RequestId"),
                    RequestType = requestType,
                    UserLogin = reader.GetString("UserLogin"),
                    NovelId = reader.IsDBNull("NovelId") ? 0 : reader.GetInt32("NovelId"),
                    NovelTitle = reader.IsDBNull("NovelTitle") ? "N/A" : reader.GetString("NovelTitle"),
                    NovelCoverImageUrl = firstCoverUrl,
                    ChapterId = reader.IsDBNull("ChapterId") ? (int?)null : reader.GetInt32("ChapterId"),
                    CurrentChapterNumber = reader.IsDBNull("CurrentChapterNumber") ? null : reader.GetString("CurrentChapterNumber"),
                    CurrentChapterTitle = reader.IsDBNull(reader.GetOrdinal("CurrentChapterTitle")) ? null : reader.GetString(reader.GetOrdinal("CurrentChapterTitle")),
                    ProposedChapterNumber = proposedNumber,
                    ProposedChapterTitle = proposedTitle,
                    RequestedAt = reader.GetDateTime(reader.GetOrdinal("RequestedAt")), // Map from alias

                    // Populate additional fields required by the ViewModel
                    UserId = reader.GetInt32(reader.GetOrdinal("UserId")), // Added
                    RequesterLogin = reader.GetString(reader.GetOrdinal("UserLogin")), // Assuming UserLogin populates RequesterLogin
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("RequestedAt")), // RequestedAt is alias for mr.CreatedAt
                    RequestDataJson = requestDataJson, // Already available
                    Status = reader.GetString(reader.GetOrdinal("Status")), // Added

                    ChapterNumber = !string.IsNullOrEmpty(reader.IsDBNull(reader.GetOrdinal("CurrentChapterNumber")) ? null : reader.GetString(reader.GetOrdinal("CurrentChapterNumber")))
                                    ? reader.GetString(reader.GetOrdinal("CurrentChapterNumber"))
                                    : proposedNumber,
                    ChapterTitle = !string.IsNullOrEmpty(reader.IsDBNull(reader.GetOrdinal("CurrentChapterTitle")) ? null : reader.GetString(reader.GetOrdinal("CurrentChapterTitle")))
                                   ? reader.GetString(reader.GetOrdinal("CurrentChapterTitle"))
                                   : proposedTitle,

                    // Content fields
                    CurrentChapterContent = currentChapterContent, // Already populated from FileService
                    ExistingContent = currentChapterContent, // Assign to ExistingContent as well if it represents the same

                    ProposedChapterContent = proposedContent, // Already populated from RequestData
                    ProposedContent = proposedContent, // Assign to ProposedContent as well

                    // Complex type fields
                    // ExistingChapterData could be more fully populated if needed, here it's implicitly used for CurrentChapterContent etc.
                    // For now, not creating a full Chapter object for it unless essential for this method's direct output.
                    ProposedChapterData = deserializedProposedChapter // Use the correctly scoped variable
                };
            }
            return requestDetails;
        }

        public async Task AddNovelTranslatorIfNotExistsAsync(int novelId, int userId)
        {
            using var conn = GetConnection();
            // Check if already exists
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM NovelTranslators WHERE NovelId = @novelId AND TranslatorId = @userId";
            checkCmd.Parameters.AddWithValue("@novelId", novelId);
            checkCmd.Parameters.AddWithValue("@userId", userId);
            var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;

            if (!exists)
            {
                using var insertCmd = conn.CreateCommand();
                insertCmd.CommandText = "INSERT INTO NovelTranslators (NovelId, TranslatorId) VALUES (@novelId, @userId)";
                insertCmd.Parameters.AddWithValue("@novelId", novelId);
                insertCmd.Parameters.AddWithValue("@userId", userId);
                await insertCmd.ExecuteNonQueryAsync();
            }
        }

        public async Task RemoveTranslatorIfLastChapterAsync(int novelId, int creatorId)
        {
            using var conn = GetConnection();
            // Check for other chapters by this creator for this novel
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT COUNT(*) FROM Chapters WHERE NovelId = @novelId AND CreatorId = @creatorId";
            checkCmd.Parameters.AddWithValue("@novelId", novelId);
            checkCmd.Parameters.AddWithValue("@creatorId", creatorId);
            // This count is checked *after* a chapter is potentially deleted by the calling code.
            // So, if count is 0, it means the deleted chapter was the last one by this creator for this novel.
            var remainingChaptersCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

            if (remainingChaptersCount == 0)
            {
                // No more chapters by this creator for this novel, remove from NovelTranslators
                using var deleteCmd = conn.CreateCommand();
                deleteCmd.CommandText = "DELETE FROM NovelTranslators WHERE NovelId = @novelId AND TranslatorId = @creatorId";
                deleteCmd.Parameters.AddWithValue("@novelId", novelId);
                deleteCmd.Parameters.AddWithValue("@creatorId", creatorId);
                await deleteCmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<Chapter> GetChapterAsync(int chapterId)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, NovelId, Number, Title, Date, CreatorId, ContentFilePath FROM Chapters WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", chapterId);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var chapter = new Chapter
                {
                    Id = reader.GetInt32("Id"),
                    NovelId = reader.GetInt32("NovelId"),
                    Number = reader.IsDBNull("Number") ? "" : reader.GetString("Number"),
                    Title = reader.GetString("Title"),
                    Date = reader.IsDBNull("Date") ? 0 : reader.GetInt64("Date"),
                    CreatorId = reader.IsDBNull("CreatorId") ? (int?)null : reader.GetInt32("CreatorId"),
                    ContentFilePath = reader.IsDBNull("ContentFilePath") ? null : reader.GetString("ContentFilePath")
                };

                if (!string.IsNullOrEmpty(chapter.ContentFilePath))
                {
                    chapter.Content = await _fileService.ReadChapterContentAsync(chapter.ContentFilePath);
                }
                return chapter;
            }
            return null;
        }

        public async Task CreateChapterAsync(Chapter chapter)
        {
            _logger.LogInformation("Entering CreateChapterAsync for NovelId: {NovelId}, Chapter Title: {ChapterTitle}", chapter.NovelId, chapter.Title);
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO Chapters (NovelId, Number, Title, Date, CreatorId, ContentFilePath) VALUES (@novelId, @number, @title, @date, @creatorId, @contentFilePath); SELECT LAST_INSERT_ID();";
            _logger.LogDebug("CreateChapterAsync parameters: NovelId={NovelId}, Number={Number}, Title={Title}, CreatorId={CreatorId}", chapter.NovelId, chapter.Number, chapter.Title, chapter.CreatorId);
            cmd.Parameters.AddWithValue("@novelId", chapter.NovelId);
            cmd.Parameters.AddWithValue("@number", chapter.Number ?? "");
            cmd.Parameters.AddWithValue("@title", chapter.Title);
            cmd.Parameters.AddWithValue("@date", chapter.Date);
            cmd.Parameters.AddWithValue("@creatorId", chapter.CreatorId.HasValue ? (object)chapter.CreatorId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@contentFilePath", (object)chapter.ContentFilePath ?? DBNull.Value);
            var newId = await cmd.ExecuteScalarAsync(); // Returns object, ensure it's not DBNull before converting
            if (newId != null && newId != DBNull.Value)
            {
                chapter.Id = Convert.ToInt32(newId);
                _logger.LogInformation("Chapter created asynchronously with Id: {ChapterId} for NovelId: {NovelId}", chapter.Id, chapter.NovelId);
            }
            else
            {
                _logger.LogError("Failed to create chapter asynchronously for NovelId: {NovelId}, Title: {ChapterTitle}. ExecuteScalar returned null or DBNull.", chapter.NovelId, chapter.Title);
            }
        }

        public async Task UpdateChapterAsync(Chapter chapter)
        {
            _logger.LogInformation("Entering UpdateChapterAsync for Chapter Id: {ChapterId}, Title: {ChapterTitle}", chapter.Id, chapter.Title);
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Chapters SET NovelId=@novelId, Number=@number, Title=@title, Date=@date, CreatorId=@creatorId, ContentFilePath=@contentFilePath WHERE Id=@id";
            _logger.LogDebug("UpdateChapterAsync parameters for Id={ChapterId}: Number={Number}, Title={Title}, CreatorId={CreatorId}", chapter.Id, chapter.Number, chapter.Title, chapter.CreatorId);
            cmd.Parameters.AddWithValue("@id", chapter.Id);
            cmd.Parameters.AddWithValue("@novelId", chapter.NovelId);
            cmd.Parameters.AddWithValue("@number", chapter.Number ?? "");
            cmd.Parameters.AddWithValue("@title", chapter.Title);
            cmd.Parameters.AddWithValue("@date", chapter.Date);
            cmd.Parameters.AddWithValue("@creatorId", chapter.CreatorId.HasValue ? (object)chapter.CreatorId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@contentFilePath", (object)chapter.ContentFilePath ?? DBNull.Value);
            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("UpdateChapterAsync for Id: {ChapterId} affected {RowsAffected} row(s).", chapter.Id, rowsAffected);
        }

        public async Task DeleteChapterAsync(int chapterId)
        {
            _logger.LogInformation("Entering DeleteChapterAsync for Chapter Id: {ChapterId}", chapterId);
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Chapters WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", chapterId);
            var rowsAffected = await cmd.ExecuteNonQueryAsync();
            _logger.LogInformation("DeleteChapterAsync for Id: {ChapterId} affected {RowsAffected} row(s).", chapterId, rowsAffected);
        }

        public List<ChapterWithNovelInfo> GetRecentChapterUpdates(int count)
        {
            var result = new List<ChapterWithNovelInfo>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    c.Id, c.NovelId, c.Number, c.Title AS ChapterTitle, c.Date AS ChapterDate, c.ContentFilePath, c.CreatorId,
                    n.Title AS NovelTitle, n.Covers AS NovelCoversJson
                FROM Chapters c
                JOIN Novels n ON c.NovelId = n.Id
                ORDER BY c.Date DESC
                LIMIT @count;";
            cmd.Parameters.AddWithValue("@count", count);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new ChapterWithNovelInfo
                {
                    Id = reader.GetInt32("Id"),
                    NovelId = reader.GetInt32("NovelId"),
                    Number = reader.IsDBNull(reader.GetOrdinal("Number")) ? null : reader.GetString("Number"),
                    Title = reader.IsDBNull(reader.GetOrdinal("ChapterTitle")) ? null : reader.GetString("ChapterTitle"),
                    Date = reader.GetInt64("ChapterDate"),
                    ContentFilePath = reader.IsDBNull(reader.GetOrdinal("ContentFilePath")) ? null : reader.GetString("ContentFilePath"),
                    CreatorId = reader.IsDBNull(reader.GetOrdinal("CreatorId")) ? (int?)null : reader.GetInt32("CreatorId"),
                    NovelTitle = reader.GetString("NovelTitle"),
                    NovelCoversJson = reader.IsDBNull(reader.GetOrdinal("NovelCoversJson")) ? null : reader.GetString("NovelCoversJson")
                });
            }
            return result;
        }
    }
}