using System;
using System.Collections.Generic;

namespace BulbaLib.Models
{
    public class ChapterWithNovelInfo
    {
        // From Chapter table
        public int Id { get; set; }
        public int NovelId { get; set; }
        public string? Number { get; set; }
        public string? Title { get; set; }
        public long Date { get; set; }
        public string? ContentFilePath { get; set; }
        public int? CreatorId { get; set; }

        // From Novel table
        public string NovelTitle { get; set; }
        public string? NovelCoversJson { get; set; }

        // Optional: Add the original Chapter object if it's easier for mapping in MySqlService
        // public Chapter Chapter {get; set;} 
        // If using this, the individual chapter properties above might not be needed here,
        // but the SQL query would still need to select them for Dapper mapping.
        // For now, keeping individual properties as per the plan.
    }
}
