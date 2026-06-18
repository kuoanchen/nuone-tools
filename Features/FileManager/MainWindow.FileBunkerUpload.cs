using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Microsoft.UI.Xaml.Controls;

namespace nuone_tools
{
    public sealed partial class MainWindow
    {
        private const string FileBunkerUploadCommand = "nuone:filebunker-upload";

        private async Task ExecuteFileBunkerUploadAsync()
        {
            var selectedEntries = GetSelectedEntriesInDisplayOrder(_activePane);
            var selectedFiles = selectedEntries
                .Where(static entry => !entry.IsDirectory)
                .ToList();

            if (selectedFiles.Count == 0)
            {
                await ShowMessageAsync("FileBunker 上傳", "請先在目前 pane 選取至少一個檔案。");
                return;
            }

            FileBunkerSettingsState config;
            try
            {
                config = BuildFileBunkerUploadConfig();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("讀取 FileBunker 設定失敗", ex.Message);
                return;
            }

            var backgroundWorkId = BeginBackgroundWork($"上傳檔案到 FileBunker 中（{selectedFiles.Count}）");
            string? completionRecord = null;

            try
            {
                var uploadedUrls = new List<string>();
                var failedFiles = new List<string>();

                foreach (var entry in selectedFiles)
                {
                    try
                    {
                        uploadedUrls.Add(await UploadFileToFileBunkerAsync(entry.FullPath, config));
                    }
                    catch (Exception ex)
                    {
                        failedFiles.Add($"{entry.Name}：{ex.Message}");
                    }
                }

                if (uploadedUrls.Count > 0)
                {
                    CopyTextToClipboard(string.Join(Environment.NewLine, uploadedUrls));
                }

                completionRecord = BuildFileBunkerBackgroundWorkRecord(
                    selectedFiles.Count,
                    uploadedUrls,
                    failedFiles);
                AddNotificationHistoryRecord(
                    NotificationHistoryScope.Sync,
                    "FileBunker",
                    BuildFileBunkerNotificationSummary(uploadedUrls.Count, failedFiles.Count),
                    completionRecord);

                if (failedFiles.Count == 0)
                {
                    await ShowFileBunkerUploadSuccessDialogAsync(uploadedUrls);
                    return;
                }

                var partialMessage = new StringBuilder();
                partialMessage.Append("部分檔案已上傳。");
                if (uploadedUrls.Count > 0)
                {
                    partialMessage.AppendLine();
                    partialMessage.AppendLine();
                    partialMessage.Append($"成功：{uploadedUrls.Count.ToString(CultureInfo.InvariantCulture)} 個，連結已複製到剪貼簿。");
                }

                partialMessage.AppendLine();
                partialMessage.AppendLine();
                partialMessage.AppendLine("失敗明細：");
                foreach (var failedFile in failedFiles)
                {
                    partialMessage.AppendLine(failedFile);
                }

                await ShowMessageAsync("FileBunker 上傳結果", partialMessage.ToString().TrimEnd());
            }
            finally
            {
                CompleteBackgroundWork(backgroundWorkId, completionRecord, persistToLocalHistory: false);
            }
        }

        private FileBunkerSettingsState BuildFileBunkerUploadConfig()
        {
            var inputEndpoint = _fileBunkerSettings.InputEndpoint?.Trim() ?? string.Empty;
            var outputEndpointBase = _fileBunkerSettings.OutputEndpointBase?.Trim() ?? string.Empty;
            var apiKey = _fileBunkerSettings.ApiKey?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(inputEndpoint))
            {
                throw new InvalidOperationException("FileBunker 輸入端點未設定，請到設定頁的 FileBunker 區塊填寫。");
            }

            if (string.IsNullOrWhiteSpace(outputEndpointBase))
            {
                throw new InvalidOperationException("FileBunker 輸出端點未設定，請到設定頁的 FileBunker 區塊填寫。");
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("FileBunker API key 未設定，請到設定頁的 FileBunker 區塊填寫。");
            }

            return new FileBunkerSettingsState
            {
                InputEndpoint = inputEndpoint,
                OutputEndpointBase = outputEndpointBase,
                ApiKey = apiKey,
                KeyLength = _fileBunkerSettings.KeyLength <= 0 ? 64 : _fileBunkerSettings.KeyLength,
                ClientId = _fileBunkerSettings.ClientId?.Trim() ?? string.Empty,
                DaysToExpiration = _fileBunkerSettings.DaysToExpiration <= 0 ? 3650 : _fileBunkerSettings.DaysToExpiration,
                DaysToPurge = _fileBunkerSettings.DaysToPurge <= 0 ? 20 : _fileBunkerSettings.DaysToPurge,
            };
        }

        private async Task<string> UploadFileToFileBunkerAsync(string filePath, FileBunkerSettingsState config)
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            await using var fileStream = await file.OpenStreamForReadAsync();
            using var form = new MultipartFormDataContent();
            using var fileContent = new StreamContent(fileStream);
            using var request = new HttpRequestMessage(HttpMethod.Post, config.InputEndpoint);
            var contentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType;

            var metadata = JsonSerializer.Serialize(new
            {
                fb = new
                {
                    keyLength = config.KeyLength,
                    clientID = config.ClientId,
                    daysToExpiration = config.DaysToExpiration,
                    daysToPurge = config.DaysToPurge,
                },
            });

            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(new StringContent(file.Name), "fileName");
            form.Add(new StringContent(metadata, Encoding.UTF8, "application/json"), "metadata");
            form.Add(fileContent, "file", file.Name);

            request.Content = form;
            request.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);

            using var response = await SharedHttpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(BuildFileBunkerUploadErrorMessage(response.StatusCode, body));
            }

            using var document = JsonDocument.Parse(body);
            return BuildFileBunkerUploadedUrl(config.OutputEndpointBase, document.RootElement);
        }

        private async Task ShowFileBunkerUploadSuccessDialogAsync(IReadOnlyList<string> uploadedUrls)
        {
            var urlsText = string.Join(Environment.NewLine, uploadedUrls);
            var summary = uploadedUrls.Count == 1
                ? "已上傳 1 個檔案，連結已複製到剪貼簿。"
                : $"已上傳 {uploadedUrls.Count.ToString(CultureInfo.InvariantCulture)} 個檔案，連結已複製到剪貼簿。";

            var content = new StackPanel
            {
                Spacing = 12,
            };
            content.Children.Add(new TextBlock
            {
                Text = summary,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            });
            content.Children.Add(new TextBox
            {
                Text = urlsText,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                MinHeight = uploadedUrls.Count == 1 ? 84 : 160,
                MaxHeight = 260,
            });

            var dialog = new ContentDialog
            {
                Title = "FileBunker 上傳完成",
                Content = content,
                PrimaryButtonText = "複製連結",
                CloseButtonText = "知道了",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootLayout.XamlRoot,
            };
            ApplyThemeToDialog(dialog);

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                CopyTextToClipboard(urlsText);
            }
        }

        private static string BuildFileBunkerBackgroundWorkRecord(
            int totalFiles,
            IReadOnlyList<string> uploadedUrls,
            IReadOnlyList<string> failedFiles)
        {
            var record = new StringBuilder();
            record.Append("完成：上傳檔案到 FileBunker 中（");
            record.Append(totalFiles.ToString(CultureInfo.InvariantCulture));
            record.Append('）');

            if (uploadedUrls.Count > 0)
            {
                record.AppendLine();
                record.Append("成功：");
                record.Append(uploadedUrls.Count.ToString(CultureInfo.InvariantCulture));
                record.AppendLine(" 個");
                record.AppendLine("連結：");
                foreach (var url in uploadedUrls)
                {
                    record.AppendLine(url);
                }
            }

            if (failedFiles.Count > 0)
            {
                record.AppendLine();
                record.Append("失敗：");
                record.Append(failedFiles.Count.ToString(CultureInfo.InvariantCulture));
                record.AppendLine(" 個");
                foreach (var failedFile in failedFiles)
                {
                    record.AppendLine(failedFile);
                }
            }

            return record.ToString().TrimEnd();
        }

        private static string BuildFileBunkerNotificationSummary(int uploadedCount, int failedCount)
        {
            if (failedCount <= 0)
            {
                return uploadedCount == 1 ? "FileBunker 已上傳 1 個檔案" : $"FileBunker 已上傳 {uploadedCount.ToString(CultureInfo.InvariantCulture)} 個檔案";
            }

            return $"FileBunker 上傳完成，成功 {uploadedCount.ToString(CultureInfo.InvariantCulture)} 個，失敗 {failedCount.ToString(CultureInfo.InvariantCulture)} 個";
        }

        private static string BuildFileBunkerUploadErrorMessage(System.Net.HttpStatusCode statusCode, string responseBody)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                if (document.RootElement.TryGetProperty("message", out var messageProperty) &&
                    messageProperty.ValueKind == JsonValueKind.String)
                {
                    var message = messageProperty.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        return $"{(int)statusCode} {message}";
                    }
                }
            }
            catch
            {
            }

            return $"HTTP {(int)statusCode}";
        }

        private static string BuildFileBunkerUploadedUrl(string endpointBase, JsonElement root)
        {
            if (!root.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("上傳成功，但回應缺少 metadata。");
            }

            if (!metadata.TryGetProperty("customerIndex", out var customerIndex) || customerIndex.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("上傳成功，但回應缺少 customerIndex。");
            }

            var begins = customerIndex.TryGetProperty("begins", out var beginsProperty) && beginsProperty.ValueKind == JsonValueKind.String
                ? beginsProperty.GetString()?.Trim()
                : string.Empty;
            var id = metadata.TryGetProperty("_id", out var idProperty) && idProperty.ValueKind == JsonValueKind.String
                ? idProperty.GetString()?.Trim()
                : string.Empty;
            var key = customerIndex.TryGetProperty("key", out var keyProperty) && keyProperty.ValueKind == JsonValueKind.String
                ? keyProperty.GetString()?.Trim()
                : string.Empty;

            if (string.IsNullOrWhiteSpace(begins) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("上傳成功，但回應資料不完整。");
            }

            return $"{endpointBase.TrimEnd('/')}/{begins}/{id}/{key}";
        }

        private static void CopyTextToClipboard(string text)
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(text);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
        }

    }
}
