using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json; // Required for JsonDocument

namespace BulbaLib.Models
{
    public class NovelRequestPreviewViewModel
    {
        // Fields from Novel model or similar structure expected by Novel.cshtml
        public int RequestId { get; set; } // ID of the ModerationRequest
        public int Id { get; set; } // Original Novel Id, if applicable (for Edit/Delete)
        public string Title { get; set; }
        public string Description { get; set; }
        public List<string> CoversList { get; set; } = new List<string>(); // Ensure initialized
        public List<string> GenresList { get; set; } = new List<string>(); // Parsed from JSON string
        public List<string> TagsList { get; set; } = new List<string>();   // Parsed from JSON string
        public string Type { get; set; } // e.g., "Япония", "Корея"
        public string Format { get; set; } // e.g., "Веб", "Лайт-новел"
        public int? ReleaseYear { get; set; }
        public string AuthorLogin { get; set; } // To be fetched based on AuthorId
        public int? AuthorId { get; set; }
        public string AlternativeTitles { get; set; } // Assuming this is a string, if it's list, change type
        public string RelatedNovelIds { get; set; } // Assuming this is a string, if it's list, change type
        public long Date { get; set; } // Unix timestamp
        public NovelStatus Status { get; set; } // Or string, depending on Novel.cshtml

        // Request-specific information
        public ModerationRequestType RequestType { get; set; }
        public string RequestTypeDisplayName { get; set; }
        public string RequesterLogin { get; set; } // User who made the request
        public bool IsPendingDeletion { get; set; } = false;
        public string OriginalNovelTitle { get; set; } // For Edit/Delete, to show what is being changed/deleted

        // Helper to parse JSON strings for Genres and Tags
        public static List<string> ParseJsonStringToList(string jsonString)
        {
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                return new List<string>();
            }
            try
            {
                // Check if the string is already a "[]" or similar empty representation
                if (jsonString.Trim() == "[]" || jsonString.Trim() == "\"[]\"") return new List<string>();

                // Sometimes the string might be double-encoded JSON string (e.g. "\"[\"Genre1\",\"Genre2\"]\"")
                // If it starts and ends with a quote, and the inner content looks like a JSON array, parse the inner.
                if (jsonString.StartsWith("\"") && jsonString.EndsWith("\"") && jsonString.Length > 2)
                {
                    string innerJson = jsonString.Substring(1, jsonString.Length - 2).Replace("\\\"", "\"");
                    if (innerJson.StartsWith("[") && innerJson.EndsWith("]"))
                    {
                        return JsonSerializer.Deserialize<List<string>>(innerJson) ?? new List<string>();
                    }
                }
                // Standard case: "[\"Genre1\",\"Genre2\"]"
                if (jsonString.StartsWith("[") && jsonString.EndsWith("]"))
                {
                    return JsonSerializer.Deserialize<List<string>>(jsonString) ?? new List<string>();
                }
                // Case: "Genre1,Genre2" (comma-separated string, not JSON array) - this should ideally not happen if source is JSON
                // If it can happen, add logic here. For now, assume valid JSON array string or empty/null.
            }
            catch (JsonException)
            {
                // If parsing fails, return empty list or handle error
                return new List<string>();
            }
            return new List<string>(); // Fallback
        }

        public string GetFriendlyRequestTypeName()
        {
            switch (RequestType)
            {
                case ModerationRequestType.AddNovel: return "Предпросмотр добавления новеллы";
                case ModerationRequestType.EditNovel: return "Предпросмотр редактирования новеллы";
                case ModerationRequestType.DeleteNovel: return "Предпросмотр удаления новеллы";
                default: return "Предпросмотр запроса";
            }
        }
    }
}
