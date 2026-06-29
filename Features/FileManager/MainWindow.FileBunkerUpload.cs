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
        private const string StorageUploadCommand = "nuone:storage-upload";

        private sealed class StorageUploadConfig
        {
            public string ApiBaseUrl { get; init; } = string.Empty;

            public string Token { get; init; } = string.Empty;

            public string ServiceAccountCode { get; init; } = string.Empty;

            public string AppId { get; init; } = "nuone-tools";

            public string OwnerType { get; init; } = "tools";
        }

        private sealed class StorageUploadResult
        {
            public string Id { get; init; } = string.Empty;

            public string Name { get; init; } = string.Empty;

            public string Url { get; init; } = string.Empty;

            public string MimeType { get; init; } = string.Empty;
        }

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
                var clipboardCopied = true;
                var clipboardErrorMessage = string.Empty;

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
                    clipboardCopied = TryCopyTextToClipboard(
                        string.Join(Environment.NewLine, uploadedUrls),
                        "filebunker-auto-copy",
                        out clipboardErrorMessage);
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
                    partialMessage.Append(
                        clipboardCopied
                            ? $"成功：{uploadedUrls.Count.ToString(CultureInfo.InvariantCulture)} 個，連結已複製到剪貼簿。"
                            : $"成功：{uploadedUrls.Count.ToString(CultureInfo.InvariantCulture)} 個，但自動複製連結到剪貼簿失敗。");
                }

                if (!clipboardCopied && !string.IsNullOrWhiteSpace(clipboardErrorMessage))
                {
                    partialMessage.AppendLine();
                    partialMessage.AppendLine();
                    partialMessage.Append("剪貼簿錯誤：");
                    partialMessage.Append(clipboardErrorMessage);
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
            catch (Exception ex)
            {
                LogBoundaryException(ex, "filebunker upload");
                await ShowMessageAsync("FileBunker 上傳失敗", ex.Message);
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
                if (!TryCopyTextToClipboard(urlsText, "filebunker-dialog-copy", out var copyErrorMessage))
                {
                    await ShowMessageAsync("複製連結失敗", copyErrorMessage);
                }
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

        private bool TryCopyTextToClipboard(string text, string operation, out string errorMessage)
        {
            try
            {
                CopyTextToClipboard(text);
                errorMessage = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                AppendDebugLog("clipboard-debug.log", $"clipboard-failed operation={operation} message={ex.Message} detail={ex}");
                return false;
            }
        }

        private async Task ExecuteStorageUploadAsync()
        {
            var selectedEntries = GetSelectedEntriesInDisplayOrder(_activePane);
            var selectedFiles = selectedEntries
                .Where(static entry => !entry.IsDirectory)
                .ToList();

            if (selectedFiles.Count == 0)
            {
                await ShowMessageAsync("Storage 上傳", "請先在目前 pane 選取至少一個檔案。");
                return;
            }

            var skippedEntries = selectedFiles
                .Where(entry => IsSshPath(entry.FullPath) || !File.Exists(entry.FullPath))
                .ToList();
            var eligibleFiles = selectedFiles
                .Except(skippedEntries)
                .ToList();

            if (eligibleFiles.Count == 0)
            {
                await ShowMessageAsync("Storage 上傳", "目前只支援上傳本機檔案，不能直接上傳 ssh:// 遠端檔案。");
                return;
            }

            StorageUploadConfig config;
            try
            {
                config = BuildStorageUploadConfig();
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("讀取 Storage 設定失敗", ex.Message);
                return;
            }

            var backgroundWorkId = BeginBackgroundWork(
                $"上傳檔案到 Storage 中（{eligibleFiles.Count.ToString(CultureInfo.InvariantCulture)}）");
            string? completionRecord = null;
            AppLogging.Information(
                "Storage upload started EligibleCount={EligibleCount} SkippedCount={SkippedCount} ServiceAccount={ServiceAccount}",
                eligibleFiles.Count,
                skippedEntries.Count,
                config.ServiceAccountCode);

            AppendDebugLog(
                "storage-upload-debug.log",
                $"start files={eligibleFiles.Count} skipped={skippedEntries.Count} api={config.ApiBaseUrl} sa={config.ServiceAccountCode}");

            try
            {
                var uploadedFiles = await UploadFilesToStorageAsync(
                    eligibleFiles.Select(static entry => entry.FullPath).ToList(),
                    config);
                var uploadedUrls = uploadedFiles
                    .Select(static file => file.Url)
                    .Where(static url => !string.IsNullOrWhiteSpace(url))
                    .ToList();
                var clipboardCopied = true;
                var clipboardErrorMessage = string.Empty;

                if (uploadedUrls.Count > 0)
                {
                    clipboardCopied = TryCopyTextToClipboard(
                        string.Join(Environment.NewLine, uploadedUrls),
                        "storage-auto-copy",
                        out clipboardErrorMessage);
                }

                var skippedFiles = skippedEntries
                    .Select(static entry => $"{entry.Name}：不支援直接上傳遠端或不存在的檔案")
                    .ToList();

                completionRecord = BuildStorageUploadBackgroundWorkRecord(uploadedFiles, skippedFiles);
                AddNotificationHistoryRecord(
                    NotificationHistoryScope.Sync,
                    "Storage",
                    BuildStorageUploadNotificationSummary(uploadedFiles.Count, skippedFiles.Count),
                    completionRecord);
                AppLogging.Information(
                    "Storage upload completed UploadedCount={UploadedCount} SkippedCount={SkippedCount} ServiceAccount={ServiceAccount}",
                    uploadedFiles.Count,
                    skippedFiles.Count,
                    config.ServiceAccountCode);

                AppendDebugLog(
                    "storage-upload-debug.log",
                    $"success uploaded={uploadedFiles.Count} skipped={skippedFiles.Count} sa={config.ServiceAccountCode} clipboardCopied={clipboardCopied}");

                if (skippedFiles.Count == 0)
                {
                    await ShowStorageUploadSuccessDialogAsync(uploadedFiles, clipboardCopied, clipboardErrorMessage);
                    return;
                }

                var partialMessage = new StringBuilder();
                partialMessage.Append("部分檔案已上傳到 Storage。");
                if (uploadedUrls.Count > 0)
                {
                    partialMessage.AppendLine();
                    partialMessage.AppendLine();
                    partialMessage.Append(
                        clipboardCopied
                            ? $"成功：{uploadedUrls.Count.ToString(CultureInfo.InvariantCulture)} 個，連結已複製到剪貼簿。"
                            : $"成功：{uploadedUrls.Count.ToString(CultureInfo.InvariantCulture)} 個，但自動複製連結到剪貼簿失敗。");
                }

                if (!clipboardCopied && !string.IsNullOrWhiteSpace(clipboardErrorMessage))
                {
                    partialMessage.AppendLine();
                    partialMessage.AppendLine();
                    partialMessage.Append("剪貼簿錯誤：");
                    partialMessage.Append(clipboardErrorMessage);
                }

                partialMessage.AppendLine();
                partialMessage.AppendLine();
                partialMessage.AppendLine("略過明細：");
                foreach (var skippedFile in skippedFiles)
                {
                    partialMessage.AppendLine(skippedFile);
                }

                await ShowMessageAsync("Storage 上傳結果", partialMessage.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                AppendDebugLog("storage-upload-debug.log", $"error message={ex.Message} detail={ex}");
                await ShowMessageAsync("Storage 上傳失敗", ex.Message);
            }
            finally
            {
                CompleteBackgroundWork(backgroundWorkId, completionRecord, persistToLocalHistory: false);
            }
        }

        private StorageUploadConfig BuildStorageUploadConfig()
        {
            var apiBaseUrl = NormalizeApiBaseUrl(_accountSettings.ApiBaseUrl);
            var token = _accountSettings.Token?.Trim() ?? string.Empty;
            var serviceAccountCode = ResolveStorageServiceAccountCode();

            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                throw new InvalidOperationException("API Base URL 未設定，請先到設定頁登入帳號。");
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("尚未登入 api.nuone.cl，請先到設定頁登入帳號。");
            }

            if (string.IsNullOrWhiteSpace(serviceAccountCode))
            {
                throw new InvalidOperationException("登入資料裡找不到可用的 storage 服務帳號。");
            }

            return new StorageUploadConfig
            {
                ApiBaseUrl = apiBaseUrl,
                Token = token,
                ServiceAccountCode = serviceAccountCode,
                AppId = "nuone-tools",
                OwnerType = "tools",
            };
        }

        private string ResolveStorageServiceAccountCode()
        {
            return ResolveServiceAccountCode("storage", "storage-upload-debug.log");
        }

        private string ResolveServiceAccountCode(string serviceName, string debugLogFileName)
        {
            var serviceAccounts = ParseServiceAccounts(_accountSettings.ServiceAccountsJson);
            var preferredCode = TryReadPreferredServiceAccountCode(_accountSettings.PayloadJson);

            AppendDebugLog(
                debugLogFileName,
                $"resolve-sa service={serviceName} preferred={preferredCode} totalAccounts={serviceAccounts.Count}");

            if (!string.IsNullOrWhiteSpace(preferredCode))
            {
                foreach (var account in serviceAccounts)
                {
                    if (!string.Equals(account.Code, preferredCode, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (account.Services.Any(service =>
                            string.Equals(service, serviceName, StringComparison.OrdinalIgnoreCase)))
                    {
                        AppendDebugLog(
                            debugLogFileName,
                            $"resolve-sa service={serviceName} matched-preferred code={account.Code}");
                        return account.Code;
                    }
                }

                AppendDebugLog(
                    debugLogFileName,
                    $"resolve-sa service={serviceName} preferred-not-usable code={preferredCode}");
            }

            foreach (var account in serviceAccounts)
            {
                if (account.Services.Any(service =>
                        string.Equals(service, serviceName, StringComparison.OrdinalIgnoreCase)))
                {
                    AppendDebugLog(
                        debugLogFileName,
                        $"resolve-sa service={serviceName} matched-fallback code={account.Code}");
                    return account.Code;
                }
            }

            AppendDebugLog(
                debugLogFileName,
                $"resolve-sa service={serviceName} no-match payloadPreview={TrimForDebugPreview(_accountSettings.ServiceAccountsJson)}");

            return string.Empty;
        }

        private static string TryReadPreferredServiceAccountCode(string rawPayloadJson)
        {
            if (string.IsNullOrWhiteSpace(rawPayloadJson))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(rawPayloadJson);
                if (document.RootElement.TryGetProperty("serviceAccountCode", out var serviceAccountCodeProperty) &&
                    serviceAccountCodeProperty.ValueKind == JsonValueKind.String)
                {
                    return serviceAccountCodeProperty.GetString()?.Trim() ?? string.Empty;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static List<(string Code, List<string> Services)> ParseServiceAccounts(string rawServiceAccountsJson)
        {
            var result = new List<(string Code, List<string> Services)>();
            if (string.IsNullOrWhiteSpace(rawServiceAccountsJson))
            {
                return result;
            }

            try
            {
                using var document = JsonDocument.Parse(rawServiceAccountsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return result;
                }

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var code = item.TryGetProperty("code", out var codeProperty) &&
                               codeProperty.ValueKind == JsonValueKind.String
                        ? codeProperty.GetString()?.Trim() ?? string.Empty
                        : string.Empty;
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        continue;
                    }

                    var services = new List<string>();
                    if (item.TryGetProperty("services", out var servicesProperty) &&
                        servicesProperty.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var serviceItem in servicesProperty.EnumerateArray())
                        {
                            if (serviceItem.ValueKind != JsonValueKind.Object)
                            {
                                continue;
                            }

                            var serviceName = serviceItem.TryGetProperty("name", out var serviceNameProperty) &&
                                              serviceNameProperty.ValueKind == JsonValueKind.String
                                ? serviceNameProperty.GetString()?.Trim() ?? string.Empty
                                : string.Empty;
                            if (!string.IsNullOrWhiteSpace(serviceName))
                            {
                                services.Add(serviceName);
                            }
                        }
                    }

                    result.Add((code, services));
                }
            }
            catch
            {
            }

            return result;
        }

        private async Task<List<StorageUploadResult>> UploadFilesToStorageAsync(
            IReadOnlyList<string> filePaths,
            StorageUploadConfig config)
        {
            using var form = new MultipartFormDataContent();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{config.ApiBaseUrl.TrimEnd('/')}/storage");
            var streams = new List<Stream>();
            var contents = new List<StreamContent>();

            try
            {
                foreach (var filePath in filePaths)
                {
                    var file = await StorageFile.GetFileFromPathAsync(filePath);
                    var stream = await file.OpenStreamForReadAsync();
                    var fileContent = new StreamContent(stream);
                    var contentType = string.IsNullOrWhiteSpace(file.ContentType)
                        ? "application/octet-stream"
                        : file.ContentType;

                    fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                    form.Add(fileContent, "files", file.Name);

                    streams.Add(stream);
                    contents.Add(fileContent);
                }

                form.Add(new StringContent(config.OwnerType), "ownerType");
                form.Add(new StringContent(config.AppId), "appId");
                request.Content = form;
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.Token);
                request.Headers.TryAddWithoutValidation("x-service-account", config.ServiceAccountCode);
                request.Headers.TryAddWithoutValidation("x-client-app", "nuone-tools");
                AppLogging.Debug(
                    "Storage upload request prepared FileCount={FileCount} ServiceAccount={ServiceAccount}",
                    filePaths.Count,
                    config.ServiceAccountCode);

                AppendDebugLog(
                    "storage-upload-debug.log",
                    $"request files={filePaths.Count} sa={config.ServiceAccountCode} appId={config.AppId} ownerType={config.OwnerType}");

                using var response = await SharedHttpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                AppendDebugLog(
                    "storage-upload-debug.log",
                    $"response status={(int)response.StatusCode} bodyPreview={TrimForDebugPreview(body)}");

                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(BuildStorageUploadErrorMessage(response.StatusCode, body));
                }

                return ParseStorageUploadResults(body);
            }
            finally
            {
                foreach (var fileContent in contents)
                {
                    fileContent.Dispose();
                }

                foreach (var stream in streams)
                {
                    stream.Dispose();
                }
            }
        }

        private static List<StorageUploadResult> ParseStorageUploadResults(string responseBody)
        {
            using var document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("files", out var filesProperty) ||
                filesProperty.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Storage 回應缺少 files。");
            }

            var results = new List<StorageUploadResult>();
            foreach (var item in filesProperty.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = ReadJsonString(item, "originalName");
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = ReadJsonString(item, "name");
                }

                var url = ReadJsonString(item, "url");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                results.Add(new StorageUploadResult
                {
                    Id = ReadJsonString(item, "id"),
                    Name = name,
                    Url = url,
                    MimeType = ReadJsonString(item, "mimeType"),
                });
            }

            if (results.Count == 0)
            {
                throw new InvalidOperationException("Storage 回應裡沒有可用的上傳結果。");
            }

            return results;
        }

        private static string BuildStorageUploadBackgroundWorkRecord(
            IReadOnlyList<StorageUploadResult> uploadedFiles,
            IReadOnlyList<string> skippedFiles)
        {
            var record = new StringBuilder();
            record.Append("完成：上傳檔案到 Storage 中（");
            record.Append(uploadedFiles.Count.ToString(CultureInfo.InvariantCulture));
            record.Append('）');

            if (uploadedFiles.Count > 0)
            {
                record.AppendLine();
                record.AppendLine("成功：");
                foreach (var file in uploadedFiles)
                {
                    record.Append(file.Name);
                    record.Append(" => ");
                    record.AppendLine(file.Url);
                }
            }

            if (skippedFiles.Count > 0)
            {
                record.AppendLine();
                record.AppendLine("略過：");
                foreach (var skippedFile in skippedFiles)
                {
                    record.AppendLine(skippedFile);
                }
            }

            return record.ToString().TrimEnd();
        }

        private static string BuildStorageUploadNotificationSummary(int uploadedCount, int skippedCount)
        {
            if (skippedCount <= 0)
            {
                return uploadedCount == 1
                    ? "Storage 已上傳 1 個檔案"
                    : $"Storage 已上傳 {uploadedCount.ToString(CultureInfo.InvariantCulture)} 個檔案";
            }

            return $"Storage 上傳完成，成功 {uploadedCount.ToString(CultureInfo.InvariantCulture)} 個，略過 {skippedCount.ToString(CultureInfo.InvariantCulture)} 個";
        }

        private async Task ShowStorageUploadSuccessDialogAsync(
            IReadOnlyList<StorageUploadResult> uploadedFiles,
            bool clipboardCopied,
            string clipboardErrorMessage)
        {
            var urlsText = string.Join(
                Environment.NewLine,
                uploadedFiles.Select(file => $"{file.Name} => {file.Url}"));
            var summary = uploadedFiles.Count == 1
                ? (clipboardCopied
                    ? "已上傳 1 個檔案到 Storage，連結已複製到剪貼簿。"
                    : "已上傳 1 個檔案到 Storage，但自動複製連結到剪貼簿失敗。")
                : (clipboardCopied
                    ? $"已上傳 {uploadedFiles.Count.ToString(CultureInfo.InvariantCulture)} 個檔案到 Storage，連結已複製到剪貼簿。"
                    : $"已上傳 {uploadedFiles.Count.ToString(CultureInfo.InvariantCulture)} 個檔案到 Storage，但自動複製連結到剪貼簿失敗。");

            var content = new StackPanel
            {
                Spacing = 12,
            };
            content.Children.Add(new TextBlock
            {
                Text = summary,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            });
            if (!clipboardCopied && !string.IsNullOrWhiteSpace(clipboardErrorMessage))
            {
                content.Children.Add(new TextBlock
                {
                    Text = $"剪貼簿錯誤：{clipboardErrorMessage}",
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                });
            }
            content.Children.Add(new TextBox
            {
                Text = urlsText,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                MinHeight = uploadedFiles.Count == 1 ? 84 : 160,
                MaxHeight = 260,
            });

            var dialog = new ContentDialog
            {
                Title = "Storage 上傳完成",
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
                if (!TryCopyTextToClipboard(
                        string.Join(Environment.NewLine, uploadedFiles.Select(static file => file.Url)),
                        "storage-dialog-copy",
                        out var recopyErrorMessage))
                {
                    await ShowMessageAsync("複製連結失敗", recopyErrorMessage);
                }
            }
        }

        private static string BuildStorageUploadErrorMessage(System.Net.HttpStatusCode statusCode, string responseBody)
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

                if (document.RootElement.TryGetProperty("error", out var errorProperty) &&
                    errorProperty.ValueKind == JsonValueKind.String)
                {
                    var error = errorProperty.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return $"{(int)statusCode} {error}";
                    }
                }
            }
            catch
            {
            }

            return $"HTTP {(int)statusCode}";
        }

        private static string ReadJsonString(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.String
                ? property.GetString()?.Trim() ?? string.Empty
                : string.Empty;
        }

        private static string TrimForDebugPreview(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "<empty>";
            }

            const int maxLength = 320;
            var normalized = value.Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
            return normalized.Length <= maxLength
                ? normalized
                : normalized[..maxLength];
        }

    }
}
