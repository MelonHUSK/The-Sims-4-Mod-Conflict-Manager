using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace The_Sims_4_Mod_Conflict_Manager
{
    public partial class MainWindow : Window
    {
        // Observable collection for binding to DataGrid
        private ObservableCollection<ModInfo> modsList = new ObservableCollection<ModInfo>();
        private string selectedModsFolder = string.Empty;

        public MainWindow()
        {
            InitializeComponent();
            ModsDataGrid.ItemsSource = modsList;
        }

        // Browse button - Open folder dialog
        private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // Use OpenFolderDialog (available in .NET 6+ WPF)
            var dialog = new OpenFolderDialog
            {
                Title = "Select your Sims 4 Mods folder",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() == true)
            {
                selectedModsFolder = dialog.FolderName;
                ModsFolderTextBox.Text = selectedModsFolder;
                ScanModsButton.IsEnabled = true;
                StatusText.Text = $"Folder selected: {selectedModsFolder}";
            }
        }

        // Scan Mods button - Scan the folder for .package files
        private async void ScanModsButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(selectedModsFolder) || !Directory.Exists(selectedModsFolder))
            {
                MessageBox.Show("Please select a valid Mods folder.", "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Disable buttons during scan
            ScanModsButton.IsEnabled = false;
            BrowseFolderButton.IsEnabled = false;

            // Clear previous results
            modsList.Clear();
            ResetStatistics();

            StatusText.Text = "Loading conflict database from Google Sheets...";
            ScanProgressBar.Visibility = Visibility.Visible;
            ScanProgressBar.IsIndeterminate = true;

            try
            {
                // Load the conflict data from Google Sheets (async)
                bool dataLoaded = await ConflictDataLoader.LoadConflictDataAsync();

                if (!dataLoaded)
                {
                    MessageBox.Show("Warning: Could not load conflict database from Google Sheets. Scanning will proceed without conflict detection.",
                                    "Database Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    var stats = ConflictDataLoader.GetDatabaseStats();
                    StatusText.Text = $"Database loaded: {stats.total} known mods. Scanning folder...";
                }

                ScanProgressBar.IsIndeterminate = false;

                // Get all .package files recursively
                string[] packageFiles = await Task.Run(() =>
                    Directory.GetFiles(selectedModsFolder, "*.package", SearchOption.AllDirectories));

                StatusText.Text = $"Found {packageFiles.Length} mod files. Analyzing...";

                int totalMods = 0;
                int compatible = 0;
                int conflicts = 0;
                int warnings = 0;

                // Setup progress bar
                ScanProgressBar.Minimum = 0;
                ScanProgressBar.Maximum = packageFiles.Length;
                ScanProgressBar.Value = 0;

                // Process each mod file
                for (int i = 0; i < packageFiles.Length; i++)
                {
                    string filePath = packageFiles[i];

                    // Process this mod on a background thread
                    var result = await Task.Run(() => ProcessModFile(filePath));

                    // Update counts based on status
                    switch (result.statusType)
                    {
                        case StatusType.Compatible:
                            compatible++;
                            break;
                        case StatusType.Conflict:
                            conflicts++;
                            break;
                        case StatusType.Warning:
                            warnings++;
                            break;
                    }

                    modsList.Add(result.mod);
                    totalMods++;

                    // Update progress bar and status
                    ScanProgressBar.Value = i + 1;
                    StatusText.Text = $"Processing mods... {i + 1}/{packageFiles.Length}";
                }

                // Update statistics
                TotalModsText.Text = totalMods.ToString();
                CompatibleModsText.Text = compatible.ToString();
                ConflictsText.Text = conflicts.ToString();
                WarningsText.Text = warnings.ToString();

                StatusText.Text = $"Scan complete! Found {totalMods} mods.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error scanning mods folder: {ex.Message}", "Scan Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Scan failed.";
            }
            finally
            {
                ScanProgressBar.Visibility = Visibility.Collapsed;
                ScanModsButton.IsEnabled = true;
                BrowseFolderButton.IsEnabled = true;
            }
        }

        // Enum for status types
        private enum StatusType
        {
            Compatible,
            Conflict,
            Warning
        }

        // Process a single mod file (runs on background thread)
        private (ModInfo mod, StatusType statusType) ProcessModFile(string filePath)
        {
            FileInfo fileInfo = new FileInfo(filePath);

            // Parse the .package file to extract basic metadata
            DBPFReader.PackageInfo packageInfo = DBPFReader.ReadPackageFile(filePath);

            // Create a ModInfo object with parsed data
            ModInfo modInfo = new ModInfo
            {
                ModName = packageInfo.ModName,
                FileSize = FormatFileSize(fileInfo.Length),
                FilePath = filePath,
                Status = "✓",
                Issue = "No issues detected"
            };

            StatusType statusType = StatusType.Warning;

            // Check against the conflict database
            var conflictInfo = ConflictDataLoader.CheckModConflict(modInfo.ModName);

            if (conflictInfo != null)
            {
                // Mod found in database interpret the patch status
                string interpretedStatus = ConflictDataLoader.InterpretPatchStatus(conflictInfo.PatchStatus);
                System.Diagnostics.Debug.WriteLine($"  Mod: {modInfo.ModName} | PatchStatus: '{conflictInfo.PatchStatus}' | Interpreted as: '{interpretedStatus}'");

                switch (interpretedStatus)
                {
                    case "conflict":
                        modInfo.Status = "✗";
                        modInfo.Issue = $"{conflictInfo.PatchStatus}";
                        if (!string.IsNullOrWhiteSpace(conflictInfo.Notes))
                            modInfo.Issue += $" - {conflictInfo.Notes}";
                        statusType = StatusType.Conflict;
                        break;

                    case "warning":
                        modInfo.Status = "⚠";
                        modInfo.Issue = $"{conflictInfo.PatchStatus}";
                        if (!string.IsNullOrWhiteSpace(conflictInfo.Notes))
                            modInfo.Issue += $" - {conflictInfo.Notes}";
                        statusType = StatusType.Warning;
                        break;

                    case "compatible":
                        modInfo.Status = "✓";
                        modInfo.Issue = $"{conflictInfo.PatchStatus}";
                        if (!string.IsNullOrWhiteSpace(conflictInfo.LastKnownUpdate))
                            modInfo.Issue += $" (Updated: {conflictInfo.LastKnownUpdate})";
                        statusType = StatusType.Compatible;
                        break;

                    default:
                        modInfo.Status = "?";
                        modInfo.Issue = $"Unknown status: {conflictInfo.PatchStatus}";
                        statusType = StatusType.Warning;
                        break;
                }
            }
            else
            {
                // Mod not in database - do basic checks
                if (!packageInfo.IsValid)
                {
                    modInfo.Status = "⚠";
                    modInfo.Issue = "Not a valid DBPF package file";
                    statusType = StatusType.Warning;
                }
                else if (DBPFReader.IsScriptMod(filePath))
                {
                    modInfo.Status = "⚠";
                    modInfo.Issue = "Not in database, manual check recommended";
                    modInfo.ModName += " [SCRIPT]";
                    statusType = StatusType.Warning;
                }
                else
                {
                    modInfo.Status = "?";
                    modInfo.Issue = "Not found in conflict database - status unknown";
                    statusType = StatusType.Warning;
                }
            }

            return (modInfo, statusType);
        }

        // Helper method to format file size
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        // Reset statistics counters
        private void ResetStatistics()
        {
            TotalModsText.Text = "0";
            CompatibleModsText.Text = "0";
            ConflictsText.Text = "0";
            WarningsText.Text = "0";
        }
    }

    // ModInfo class to represent a single mod
    public class ModInfo
    {
        public string Status { get; set; } = string.Empty;
        public string ModName { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty;
        public string Issue { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }
}