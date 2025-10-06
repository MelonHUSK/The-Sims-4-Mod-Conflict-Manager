using System;
using System.IO;
using System.Text;

namespace The_Sims_4_Mod_Conflict_Manager
{
    public class DBPFReader
    {
        // DBPF file header constants
        private const string DBPF_SIGNATURE = "DBPF";
        private const int HEADER_SIZE = 96;

        public class DBPFHeader
        {
            public string Signature { get; set; } = string.Empty;
            public int MajorVersion { get; set; }
            public int MinorVersion { get; set; }
            public int IndexEntryCount { get; set; }
            public uint IndexOffset { get; set; }
            public uint IndexSize { get; set; }
        }

        public class PackageInfo
        {
            public bool IsValid { get; set; }
            public string FileName { get; set; } = string.Empty;
            public DBPFHeader Header { get; set; } = new DBPFHeader();
            public int ResourceCount { get; set; }
            public string Creator { get; set; } = "Unknown";
            public string ModName { get; set; } = string.Empty;
            public string GameVersion { get; set; } = "Unknown";
            public string Description { get; set; } = string.Empty;
        }

        /// <summary>
        /// Reads basic information from a DBPF .package file
        /// </summary>
        public static PackageInfo ReadPackageFile(string filePath)
        {
            var info = new PackageInfo
            {
                FileName = Path.GetFileName(filePath),
                ModName = Path.GetFileNameWithoutExtension(filePath),
                IsValid = false
            };

            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    // Read and validate DBPF signature
                    byte[] signatureBytes = reader.ReadBytes(4);
                    string signature = Encoding.ASCII.GetString(signatureBytes);

                    if (signature != DBPF_SIGNATURE)
                    {
                        return info; // Not a valid DBPF file
                    }

                    info.IsValid = true;
                    info.Header.Signature = signature;

                    // Read version info
                    info.Header.MajorVersion = reader.ReadInt32();
                    info.Header.MinorVersion = reader.ReadInt32();

                    // Skip to important header fields
                    reader.BaseStream.Seek(36, SeekOrigin.Begin);
                    info.Header.IndexEntryCount = reader.ReadInt32();

                    reader.BaseStream.Seek(64, SeekOrigin.Begin);
                    info.Header.IndexOffset = reader.ReadUInt32();
                    info.Header.IndexSize = reader.ReadUInt32();

                    info.ResourceCount = info.Header.IndexEntryCount;

                    // Try to extract additional metadata
                    ExtractMetadata(reader, info);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading package file {filePath}: {ex.Message}");
            }

            return info;
        }

        /// <summary>
        /// Attempts to extract metadata like creator name, description, etc.
        /// This is a simplified version - full parsing would be more complex
        /// </summary>
        private static void ExtractMetadata(BinaryReader reader, PackageInfo info)
        {
            try
            {
                // Jump to index table
                reader.BaseStream.Seek(info.Header.IndexOffset, SeekOrigin.Begin);

                // Read first few index entries to look for metadata
                // In Sims 4, tuning files often contain mod information
                for (int i = 0; i < Math.Min(10, info.Header.IndexEntryCount); i++)
                {
                    // Index entry structure (simplified)
                    uint typeId = reader.ReadUInt32();
                    uint groupId = reader.ReadUInt32();
                    uint instanceId = reader.ReadUInt32();
                    uint resourceOffset = reader.ReadUInt32();
                    uint resourceSize = reader.ReadUInt32();

                    // Look for XML tuning files (Type ID 0x0333406C is common for tuning)
                    if (typeId == 0x0333406C && resourceSize < 100000) // Reasonable size limit
                    {
                        long currentPos = reader.BaseStream.Position;

                        try
                        {
                            // Jump to resource data
                            reader.BaseStream.Seek(resourceOffset, SeekOrigin.Begin);

                            // Read a sample of the resource
                            byte[] resourceData = reader.ReadBytes(Math.Min((int)resourceSize, 2048));
                            string resourceText = Encoding.UTF8.GetString(resourceData);

                            // Look for common metadata patterns in XML
                            if (resourceText.Contains("creator") || resourceText.Contains("author"))
                            {
                                // Simple extraction (this is very basic - real parsing would use XML reader)
                                ExtractCreatorFromText(resourceText, info);
                            }
                        }
                        catch
                        {
                            // Skip problematic resources
                        }

                        // Return to index position
                        reader.BaseStream.Seek(currentPos, SeekOrigin.Begin);
                    }
                }
            }
            catch
            {
                // Metadata extraction is optional, don't fail the whole operation
            }
        }

        /// <summary>
        /// Simple text parsing to find creator information
        /// </summary>
        private static void ExtractCreatorFromText(string text, PackageInfo info)
        {
            // Look for common patterns
            string[] creatorTags = { "creator=\"", "author=\"", "creator:", "author:" };

            foreach (var tag in creatorTags)
            {
                int startIndex = text.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                if (startIndex != -1)
                {
                    startIndex += tag.Length;
                    int endIndex = text.IndexOfAny(new[] { '"', '<', '\n', '\r' }, startIndex);

                    if (endIndex > startIndex)
                    {
                        string creator = text.Substring(startIndex, endIndex - startIndex).Trim();
                        if (!string.IsNullOrWhiteSpace(creator) && creator.Length < 50)
                        {
                            info.Creator = creator;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Detects if a package might be a script mod (contains Python)
        /// Script mods are more likely to cause conflicts
        /// </summary>
        public static bool IsScriptMod(string filePath)
        {
            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    // Skip header
                    reader.BaseStream.Seek(64, SeekOrigin.Begin);
                    uint indexOffset = reader.ReadUInt32();
                    reader.BaseStream.Seek(36, SeekOrigin.Begin);
                    int entryCount = reader.ReadInt32();

                    reader.BaseStream.Seek(indexOffset, SeekOrigin.Begin);

                    // Look for Python script resources (Type ID 0x00000000 with .py extension pattern)
                    for (int i = 0; i < Math.Min(100, entryCount); i++)
                    {
                        uint typeId = reader.ReadUInt32();
                        reader.ReadUInt32(); // group
                        reader.ReadUInt32(); // instance
                        reader.ReadUInt32(); // offset
                        reader.ReadUInt32(); // size

                        // Check for script file type
                        if (typeId == 0x00000000)
                        {
                            return true; // Likely contains scripts
                        }
                    }
                }
            }
            catch
            {
                // If we can't read it, assume it's not a script mod
            }

            return false;
        }
    }
}