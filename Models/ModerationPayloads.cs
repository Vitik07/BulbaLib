using System.Collections.Generic;
using BulbaLib.Models; // For NovelEditModel

namespace BulbaLib.Models
{
    public class EditNovelDataPayload
    {
        public int NovelId { get; set; }
        public NovelEditModel UpdatedFields { get; set; }
        public List<string> KeptExistingCovers { get; set; }
        public List<string> NewTempCoverPaths { get; set; }
    }
}
