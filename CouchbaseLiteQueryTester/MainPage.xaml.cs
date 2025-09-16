using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Couchbase.Lite;
using CouchbaseLiteQueryTester.Utilities;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using System.Threading.Tasks;

namespace CouchbaseLiteQueryTester
{
    public partial class MainPage : ContentPage
    {
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

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
#if WINDOWS || MACCATALYST
            var result = await FolderPicker.Default.PickAsync(new FolderPickerOptions
            {
                Title = "Select a Couchbase Lite .cblite2 database"
            });

            return result?.Folder?.Path;
#else
            await Task.CompletedTask;
            throw new PlatformNotSupportedException("Selecting a database is currently supported on Windows and Mac only.");
#endif
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
                using var results = query.Execute();

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
            var formatted = TextHighlighter.CreateJsonFormattedString(json);

            ResultLabel.FormattedText = formatted;
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
            var formatted = new FormattedString();
            formatted.Spans.Add(new Span
            {
                Text = message,
                TextColor = Colors.Red
            });

            ResultLabel.FormattedText = formatted;
            ResultSummaryLabel.Text = "An error occurred.";
        }

        private void ClearResultsContent()
        {
            ResultLabel.FormattedText = new FormattedString();
            ResultLabel.Text = string.Empty;
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
