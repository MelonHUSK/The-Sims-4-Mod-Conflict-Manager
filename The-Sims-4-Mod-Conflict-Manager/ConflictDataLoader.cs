using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace The_Sims_4_Mod_Conflict_Manager
{
    public class ConflictDataLoader
    {
        private const string GOOGLE_SHEETS_CSV_URL = "https://docs.google.com/spreadsheets/d/e/2PACX-1vQumrBlYAHnfw_YTaHohrQeQZcpqVatmqhqUCaHRiIVaWUtxz8yQrXN2Mfr9S9btjto2LvHbJ_RDyCC/pub?gid=119778444&single=true&range=A:I&output=csv";

        public class ModConflictInfo
        {
            public string ModName { get; set; } = string.Empty;
            public string Creator { get; set; } = string.Empty;
            public string Link { get; set; } = string.Empty;
            public string PatchStatus { get; set; } = string.Empty; // The main status field
            public string LastKnownUpdate { get; set; } = string.Empty;
            public string LastStatusChange { get; set; } = string.Empty;
            public string Notes { get; set; } = string.Empty;
            public string AdditionalInfoLink { get; set; } = string.Empty;
        }

        private static List<ModConflictInfo> conflictDatabase = new List<ModConflictInfo>();
        private static Dictionary<string, ModConflictInfo> quickLookup = new Dictionary<string, ModConflictInfo>();
        private static DateTime lastUpdated = DateTime.MinValue;

        /// <summary>
        /// Downloads and loads the conflict data from Google Sheets
        /// </summary>
        public static async Task<bool> LoadConflictDataAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // Set a timeout
                    client.Timeout = TimeSpan.FromSeconds(30);

                    // Download the CSV
                    string csvContent = await client.GetStringAsync(GOOGLE_SHEETS_CSV_URL);

                    // Parse the CSV
                    conflictDatabase = ParseCSV(csvContent);
                    lastUpdated = DateTime.Now;

                    return conflictDatabase.Count > 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading conflict data: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Synchronous wrapper for loading conflict data
        /// </summary>
        public static bool LoadConflictData()
        {
            try
            {
                Task<bool> task = LoadConflictDataAsync();
                task.Wait();
                return task.Result;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parses CSV content into ModConflictInfo objects
        /// CSV Format: ➕, Mod Name, Creator, Link, Patch Status, Last Known Update, Last Status Change, Notes, Additional Info Link
        /// </summary>
        private static List<ModConflictInfo> ParseCSV(string csvContent)
        {
            var results = new List<ModConflictInfo>();

            using (StringReader reader = new StringReader(csvContent))
            {
                string? line;
                bool isFirstLine = true;

                while ((line = reader.ReadLine()) != null)
                {
                    // Skip header row
                    if (isFirstLine)
                    {
                        isFirstLine = false;
                        continue;
                    }

                    // Skip empty lines
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    try
                    {
                        // Parse CSV line (handle quoted fields)
                        List<string> fields = ParseCSVLine(line);

                        if (fields.Count >= 5 && !string.IsNullOrWhiteSpace(fields[1])) // Need at least mod name and status
                        {
                            var modInfo = new ModConflictInfo
                            {
                                // Skip column 0 (➕ icon)
                                ModName = fields.Count > 1 ? fields[1].Trim() : "",
                                Creator = fields.Count > 2 ? fields[2].Trim() : "",
                                Link = fields.Count > 3 ? fields[3].Trim() : "",
                                PatchStatus = fields.Count > 4 ? fields[4].Trim() : "",
                                LastKnownUpdate = fields.Count > 5 ? fields[5].Trim() : "",
                                LastStatusChange = fields.Count > 6 ? fields[6].Trim() : "",
                                Notes = fields.Count > 7 ? fields[7].Trim() : "",
                                AdditionalInfoLink = fields.Count > 8 ? fields[8].Trim() : ""
                            };

                            results.Add(modInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error parsing line: {line}. Error: {ex.Message}");
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Parses a single CSV line, handling quoted fields properly
        /// </summary>
        private static List<string> ParseCSVLine(string line)
        {
            List<string> fields = new List<string>();
            bool inQuotes = false;
            string currentField = "";

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(currentField);
                    currentField = "";
                }
                else
                {
                    currentField += c;
                }
            }

            // Add the last field
            fields.Add(currentField);

            return fields;
        }

        /// <summary>
        /// Checks if a mod has known conflicts based on filename
        /// </summary>
        public static ModConflictInfo? CheckModConflict(string modFileName)
        {
            if (conflictDatabase.Count == 0)
                return null;

            // Clean the mod filename for comparison
            string cleanModName = Path.GetFileNameWithoutExtension(modFileName)
                .ToLower()
                .Replace("_", " ")
                .Replace("-", " ")
                .Replace(".", " ")
                .Trim();

            // Try exact match first
            var exactMatch = conflictDatabase.FirstOrDefault(m =>
                CleanModName(m.ModName) == cleanModName);

            if (exactMatch != null)
                return exactMatch;

            // Try partial match - check if database mod name is contained in file name
            var partialMatch = conflictDatabase.FirstOrDefault(m =>
            {
                string dbModName = CleanModName(m.ModName);
                return !string.IsNullOrEmpty(dbModName) &&
                       (cleanModName.Contains(dbModName) || dbModName.Contains(cleanModName));
            });

            return partialMatch;
        }

        /// <summary>
        /// Helper to clean mod names for comparison
        /// </summary>
        private static string CleanModName(string modName)
        {
            return modName.ToLower()
                .Replace("_", " ")
                .Replace("-", " ")
                .Replace(".", " ")
                .Trim();
        }

        /// <summary>
        /// Interprets the Patch Status field and returns a simplified status
        /// </summary>
        public static string InterpretPatchStatus(string patchStatus)
        {
            if (string.IsNullOrWhiteSpace(patchStatus))
                return "unknown";

            string status = patchStatus.ToLower().Trim();

            // Compatible/Working statuses
            if (status.Contains("updated") ||
                status.Contains("working") ||
                status.Contains("compatible") ||
                status.Contains("no update") ||
                status.Contains("fine") ||
                status.Contains("ok"))
            {
                return "compatible";
            }

            // Broken/Conflict statuses
            if (status.Contains("broken") ||
                status.Contains("outdated") ||
                status.Contains("not working") ||
                status.Contains("crashes") ||
                status.Contains("error"))
            {
                return "conflict";
            }

            // Warning statuses
            if (status.Contains("caution") ||
                status.Contains("warning") ||
                status.Contains("issues") ||
                status.Contains("testing") ||
                status.Contains("beta"))
            {
                return "warning";
            }

            // Default to warning if we can't determine
            return "warning";
        }

        /// <summary>
        /// Gets all mods in the database
        /// </summary>
        public static List<ModConflictInfo> GetAllKnownMods()
        {
            return new List<ModConflictInfo>(conflictDatabase);
        }

        /// <summary>
        /// Gets statistics about the conflict database
        /// </summary>
        public static (int total, int conflicts, int warnings, int compatible) GetDatabaseStats()
        {
            int total = conflictDatabase.Count;
            int conflicts = 0;
            int warnings = 0;
            int compatible = 0;

            foreach (var mod in conflictDatabase)
            {
                string status = InterpretPatchStatus(mod.PatchStatus);
                switch (status)
                {
                    case "conflict":
                        conflicts++;
                        break;
                    case "warning":
                        warnings++;
                        break;
                    case "compatible":
                        compatible++;
                        break;
                }
            }

            return (total, conflicts, warnings, compatible);
        }

        /// <summary>
        /// Gets when the data was last updated
        /// </summary>
        public static DateTime GetLastUpdateTime()
        {
            return lastUpdated;
        }

        /// <summary>
        /// Checks if the data needs refreshing (older than 1 hour)
        /// </summary>
        public static bool NeedsRefresh()
        {
            return DateTime.Now - lastUpdated > TimeSpan.FromHours(1);
        }
    }
}