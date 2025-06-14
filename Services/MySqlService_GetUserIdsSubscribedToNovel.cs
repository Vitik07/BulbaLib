// This is a partial class or a snippet to be added to MySqlService.cs
/*
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Linq; // Required for Enumerable.Any()

public partial class MySqlService // Assuming MySqlService is a partial class or this is added to it
{
    public List<int> GetUserIdsSubscribedToNovel(int novelId, List<string> statuses)
    {
        var userIds = new List<int>();
        if (statuses == null || !statuses.Any())
        {
            return userIds; // No statuses to filter by, return empty list
        }

        using var conn = GetConnection(); // Assumes GetConnection() is available in MySqlService
        using var cmd = conn.CreateCommand();

        // Build the IN clause for statuses
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
        return userIds.Distinct().ToList(); // Ensure distinct user IDs
    }
}
*/