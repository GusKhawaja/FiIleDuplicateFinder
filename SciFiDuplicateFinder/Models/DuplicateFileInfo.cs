using System.IO;

namespace SciFiDuplicateFinder.Models
{
    public class DuplicateFileInfo
    {
        public string FolderPath { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }

        public string FullPath => Path.Combine(FolderPath, FileName);
    }
}
