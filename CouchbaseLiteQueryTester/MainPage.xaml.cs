using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using CommunityToolkit.Maui.Storage;
using Couchbase.Lite;
using CouchbaseLiteQueryTester.Controls;
using CouchbaseLiteQueryTester.Utilities;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace CouchbaseLiteQueryTester
{
    public partial class MainPage : ContentPage
    {
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        private const string LastDatabaseFolderPreferenceKey = "LastDatabaseFolder";

        private Database? _database;

        public MainPage()
        {
            InitializeComponent();
            ExecuteButton.IsEnabled = false;
            ResultSummaryLabel.Text = "Results will appear here";
        }

        private async void OnOpenDatabaseClicked(object sender, EventArgs e)
        {
            try
            {
                var folderPath = await PickDatabaseFolderAsync();
                if (string.IsNullOrWhiteSpace(folderPath))
                {
                    return;
                }

                OpenDatabase(folderPath);
            }
            catch (Exception ex)
            {
                ShowError($"Failed to open database: {ex.Message}");
            }
        }

        private async Task<string?> PickDatabaseFolderAsync()
        {
            try
            {
                var options = CreateFolderPickerOptions(GetLastDatabaseFolder());
                var result = await FolderPicker.Default.PickAsync(options);

                return result?.Folder?.Path;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        private void OpenDatabase(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException("The selected folder does not exist.");
            }

            var directoryInfo = new DirectoryInfo(folderPath);
            if (!directoryInfo.Name.EndsWith(".cblite2", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The selected folder is not a .cblite2 Couchbase Lite database.");
            }

            var parent = directoryInfo.Parent?.FullName ?? throw new InvalidOperationException("Unable to determine the database directory.");
            var dbName = Path.GetFileNameWithoutExtension(directoryInfo.Name);

            _database?.Dispose();

            var config = new DatabaseConfiguration
            {
                Directory = parent
            };

            _database = new Database(dbName, config);

            ExecuteButton.IsEnabled = true;
            DatabaseStatusLabel.Text = $"Connected: {dbName}{Environment.NewLine}{directoryInfo.FullName}";
            ResultSummaryLabel.Text = "Database opened. Ready for queries.";
            ClearResultsContent();

            PersistLastDatabaseFolder(directoryInfo.Parent?.FullName ?? directoryInfo.FullName);
        }

        private void OnExecuteQueryClicked(object sender, EventArgs e)
        {
            ClearResultsContent();

            if (_database is null)
            {
                ShowError("Please open a Couchbase Lite database first.");
                return;
            }

            var sql = QueryEditor.Text?.Trim();
            if (string.IsNullOrWhiteSpace(sql))
            {
                ShowError("Enter a SQL++ query to execute.");
                return;
            }

            try
            {
                using var query = _database.CreateQuery(sql);
                var results = query.Execute();

                var rows = new List<object?>();
                foreach (var row in results)
                {
                    rows.Add(Simplify(row.ToDictionary()));
                }

                var json = JsonSerializer.Serialize(rows, _jsonOptions);
                DisplayJson(json, rows.Count);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
        }

        private void DisplayJson(string json, int rowCount)
        {
            ResultViewer.PlainTextColor = GetDefaultResultPlainTextColor();
            ResultViewer.HighlightingLanguage = SyntaxHighlightingLanguage.Json;
            ResultViewer.Text = json;
            ResultSummaryLabel.Text = rowCount == 1 ? "1 row returned" : $"{rowCount} rows returned";

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(50);
                await ResultScrollView.ScrollToAsync(0, 0, false);
            });
        }

        private void OnClearResultsClicked(object sender, EventArgs e)
        {
            ClearResultsContent();
            ResultSummaryLabel.Text = "Results cleared.";
        }

        private void ShowError(string message)
        {
            ResultViewer.HighlightingLanguage = SyntaxHighlightingLanguage.None;
            ResultViewer.PlainTextColor = Colors.Red;
            ResultViewer.Text = message;
            ResultSummaryLabel.Text = "An error occurred.";
        }

        private void ClearResultsContent()
        {
            ResultViewer.PlainTextColor = GetDefaultResultPlainTextColor();
            ResultViewer.HighlightingLanguage = SyntaxHighlightingLanguage.Json;
            ResultViewer.Text = string.Empty;
        }

        private static FolderPickerOptions CreateFolderPickerOptions(string? initialFolder)
        {
            var options = new FolderPickerOptions
            {
                Title = "Select a Couchbase Lite .cblite2 database"
            };

            TryApplyInitialFolder(options, initialFolder);
            return options;
        }

        private static void TryApplyInitialFolder(FolderPickerOptions options, string? initialFolder)
        {
            if (string.IsNullOrWhiteSpace(initialFolder) || !Directory.Exists(initialFolder))
            {
                return;
            }

            try
            {
                var optionType = options.GetType();
                var property = optionType.GetProperty("InitialFolder", BindingFlags.Public | BindingFlags.Instance)
                               ?? optionType.GetProperty("InitialDirectory", BindingFlags.Public | BindingFlags.Instance)
                               ?? optionType.GetProperty("InitialLocation", BindingFlags.Public | BindingFlags.Instance)
                               ?? optionType.GetProperty("StartLocation", BindingFlags.Public | BindingFlags.Instance);

                if (property is not null)
                {
                    if (property.PropertyType == typeof(string))
                    {
                        property.SetValue(options, initialFolder);
                    }
                    else if (property.PropertyType == typeof(DirectoryInfo))
                    {
                        property.SetValue(options, new DirectoryInfo(initialFolder));
                    }
                    else if (property.PropertyType.FullName == typeof(Uri).FullName)
                    {
                        var separator = Path.DirectorySeparatorChar.ToString();
                        var path = initialFolder.EndsWith(separator, StringComparison.Ordinal)
                            ? initialFolder
                            : initialFolder + separator;

                        if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
                        {
                            property.SetValue(options, uri);
                        }
                    }
                }

                var settingsProperty = optionType.GetProperty("SettingsIdentifier", BindingFlags.Public | BindingFlags.Instance);
                if (settingsProperty is not null && settingsProperty.PropertyType == typeof(string))
                {
                    settingsProperty.SetValue(options, $"CouchbaseLiteQueryTester:{LastDatabaseFolderPreferenceKey}");
                }
            }
            catch
            {
                // Ignore failures to set optional properties; the picker will fall back to its default behavior.
            }
        }

        private static string? GetLastDatabaseFolder()
        {
            var stored = Preferences.Get(LastDatabaseFolderPreferenceKey, string.Empty);
            if (string.IsNullOrWhiteSpace(stored) || !Directory.Exists(stored))
            {
                if (!string.IsNullOrWhiteSpace(stored))
                {
                    Preferences.Remove(LastDatabaseFolderPreferenceKey);
                }

                return null;
            }

            return stored;
        }

        private static void PersistLastDatabaseFolder(string? folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return;
            }

            Preferences.Set(LastDatabaseFolderPreferenceKey, folderPath);
        }

        private Color GetDefaultResultPlainTextColor()
        {
            var theme = Application.Current?.RequestedTheme ?? AppTheme.Light;
            return theme == AppTheme.Dark ? Colors.White : Colors.Black;
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _database?.Dispose();
            _database = null;
            ExecuteButton.IsEnabled = false;
            DatabaseStatusLabel.Text = "No database selected";
        }

        private static object? Simplify(object? value)
        {
            switch (value)
            {
                case null:
                    return null;
                case IDictionary<string, object?> dictionary:
                    return dictionary.ToDictionary(kvp => kvp.Key, kvp => Simplify(kvp.Value));
                case IReadOnlyDictionary<string, object?> readOnlyDictionary:
                    return readOnlyDictionary.ToDictionary(kvp => kvp.Key, kvp => Simplify(kvp.Value));
                case IEnumerable<KeyValuePair<string, object?>> pairs:
                {
                    var map = new Dictionary<string, object?>();
                    foreach (var kvp in pairs)
                    {
                        map[kvp.Key] = Simplify(kvp.Value);
                    }

                    return map;
                }
                case IEnumerable enumerable when value is not string:
                {
                    var list = new List<object?>();
                    foreach (var item in enumerable)
                    {
                        list.Add(Simplify(item));
                    }

                    return list;
                }
                case byte[] bytes:
                    return Convert.ToBase64String(bytes);
                case ReadOnlyMemory<byte> memory:
                    return Convert.ToBase64String(memory.ToArray());
                case Blob blob:
                    return new Dictionary<string, object?>
                    {
                        ["@type"] = "blob",
                        ["contentType"] = blob.ContentType,
                        ["length"] = blob.Length,
                        ["digest"] = blob.Digest
                    };
                default:
                    return value;
            }
        }
    }
}
