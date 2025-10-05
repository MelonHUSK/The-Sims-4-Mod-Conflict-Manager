using System;
using System.Collections.ObjectModel;
using System.IO;
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
        private void ScanModsButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(selectedModsFolder) || !Directory.Exists(selectedModsFolder))
            {
                MessageBox.Show("Please select a valid Mods folder.", "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Clear previous results
            modsList.Clear();
            ResetStatistics();

            StatusText.Text = "Scanning mods folder...";
            ScanProgressBar.Visibility = Visibility.Visible;

            try
            {
                // Get all .package files recursively
                string[] packageFiles = Directory.GetFiles(selectedModsFolder, "*.package", SearchOption.AllDirectories);

                StatusText.Text = $"Found {packageFiles.Length} mod files. Analyzing...";

                int totalMods = 0;
                int compatible = 0;
                int conflicts = 0;
                int warnings = 0;

                // Process each mod file
                foreach (string filePath in packageFiles)
                {
                    FileInfo fileInfo = new FileInfo(filePath);

                    // Create a ModInfo object (for now with dummy data)
                    // Later we'll parse the actual .package file
                    ModInfo mod = new ModInfo
                    {
                        ModName = Path.GetFileNameWithoutExtension(filePath),
                        Creator = "Unknown", // Will be extracted from package
                        RequiredVersion = "1.0.0.0", // Will be extracted from package
                        FileSize = FormatFileSize(fileInfo.Length),
                        FilePath = filePath,
                        Status = "✓", // Default status
                        Issue = "No issues detected"
                    };

                    // TODO: Add actual conflict detection logic here
                    // For now, simulate random results
                    Random rand = new Random(filePath.GetHashCode());
                    int statusRoll = rand.Next(0, 10);

                    if (statusRoll < 7) // 70% compatible
                    {
                        mod.Status = "✓";
                        mod.Issue = "Compatible";
                        compatible++;
                    }
                    else if (statusRoll < 9) // 20% warning
                    {
                        mod.Status = "⚠";
                        mod.Issue = "May cause conflicts with game version 1.108";
                        warnings++;
                    }
                    else // 10% conflict
                    {
                        mod.Status = "✗";
                        mod.Issue = "Incompatible with current game version";
                        conflicts++;
                    }

                    modsList.Add(mod);
                    totalMods++;
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
            }
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
        public string Creator { get; set; } = string.Empty;
        public string RequiredVersion { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty;
        public string Issue { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }
}