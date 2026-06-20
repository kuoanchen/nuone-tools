using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace nuone_tools
{
    public sealed partial class MainWindow
    {
        private const string EnhancePdfCommand = "nuone:enhance-pdf";
        private const double EnhancePdfDpiScale = 2.0;

        private async Task ExecuteEnhancePdfAsync()
        {
            var selectedEntries = GetSelectedEntriesInDisplayOrder(_activePane);
            var selectedPdfFiles = selectedEntries
                .Where(static entry =>
                    !entry.IsDirectory &&
                    !IsSshPath(entry.FullPath) &&
                    string.Equals(Path.GetExtension(entry.FullPath), ".pdf", StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(entry.FullPath))
                .ToList();
            var skippedEntries = selectedEntries
                .Except(selectedPdfFiles)
                .ToList();

            if (selectedPdfFiles.Count == 0)
            {
                await ShowMessageAsync("PDF 增強", "請先在目前 pane 選取至少一個本機 PDF 檔案。");
                return;
            }

            var scriptPath = ResolveEnhancePdfScriptPath();
            if (scriptPath is null)
            {
                await ShowMessageAsync("PDF 增強失敗", "找不到 enhance_pdf.py，請重新發佈或檢查 scripts 目錄。");
                return;
            }

            var backgroundWorkId = BeginBackgroundWork(
                $"PDF 增強 {selectedPdfFiles.Count.ToString(CultureInfo.InvariantCulture)} 個檔案中");
            var completionRecord = string.Empty;

            try
            {
                var outputs = new List<string>();
                var failures = new List<string>();

                foreach (var entry in selectedPdfFiles)
                {
                    var outputPath = BuildEnhancedPdfOutputPath(entry.FullPath);
                    AppendDebugLog(
                        "pdf-enhance-debug.log",
                        $"start input={entry.FullPath} output={outputPath} scale={EnhancePdfDpiScale.ToString(CultureInfo.InvariantCulture)}");

                    try
                    {
                        await RunEnhancePdfScriptAsync(scriptPath, entry.FullPath, outputPath);
                        outputs.Add(outputPath);
                        AppendDebugLog("pdf-enhance-debug.log", $"success input={entry.FullPath} output={outputPath}");
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{entry.Name}：{ex.Message}");
                        AppendDebugLog("pdf-enhance-debug.log", $"error input={entry.FullPath} output={outputPath} detail={ex}");
                    }
                }

                await EnqueueOnUiAsync(() => RefreshPaneAfterLocalChange(_activePane));

                completionRecord = BuildEnhancePdfCompletionRecord(outputs, failures, skippedEntries);

                if (failures.Count == 0 && skippedEntries.Count == 0)
                {
                    var summary = outputs.Count == 1
                        ? $"已完成 1 個 PDF 增強：{Path.GetFileName(outputs[0])}"
                        : $"已完成 {outputs.Count.ToString(CultureInfo.InvariantCulture)} 個 PDF 增強";
                    await ShowMessageAsync("PDF 增強完成", summary);
                    return;
                }

                var message = new StringBuilder();
                if (outputs.Count > 0)
                {
                    message.Append("已完成部分或全部 PDF 增強。");
                }
                else
                {
                    message.Append("沒有任何 PDF 增強成功。");
                }

                if (outputs.Count > 0)
                {
                    message.AppendLine();
                    message.AppendLine();
                    message.AppendLine("輸出：");
                    foreach (var output in outputs)
                    {
                        message.AppendLine(Path.GetFileName(output));
                    }
                }

                if (skippedEntries.Count > 0)
                {
                    message.AppendLine();
                    message.AppendLine("略過：");
                    foreach (var skippedEntry in skippedEntries)
                    {
                        message.AppendLine(skippedEntry.Name);
                    }
                }

                if (failures.Count > 0)
                {
                    message.AppendLine();
                    message.AppendLine("失敗：");
                    foreach (var failure in failures)
                    {
                        message.AppendLine(failure);
                    }
                }

                await ShowMessageAsync("PDF 增強結果", message.ToString().TrimEnd());
            }
            finally
            {
                CompleteBackgroundWork(
                    backgroundWorkId,
                    string.IsNullOrWhiteSpace(completionRecord) ? null : completionRecord,
                    string.IsNullOrWhiteSpace(completionRecord) ? null : completionRecord,
                    persistToLocalHistory: false);
            }
        }

        private string? ResolveEnhancePdfScriptPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "scripts", "enhance_pdf.py"),
                Path.Combine(Environment.CurrentDirectory, "scripts", "enhance_pdf.py"),
            }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

            foreach (var candidate in candidates)
            {
                AppendDebugLog("pdf-enhance-debug.log", $"resolve-script candidate={candidate} exists={File.Exists(candidate)}");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string BuildEnhancedPdfOutputPath(string inputPath)
        {
            var directory = Path.GetDirectoryName(inputPath) ?? string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(inputPath);
            return Path.Combine(directory, $"{baseName}_enhanced.pdf");
        }

        private async Task RunEnhancePdfScriptAsync(string scriptPath, string inputPath, string outputPath)
        {
            var launchers = new[]
            {
                ("py", new[] { "-3", scriptPath, inputPath, outputPath, EnhancePdfDpiScale.ToString(CultureInfo.InvariantCulture) }),
                ("python", new[] { scriptPath, inputPath, outputPath, EnhancePdfDpiScale.ToString(CultureInfo.InvariantCulture) }),
            };

            Exception? lastException = null;

            foreach (var (fileName, arguments) in launchers)
            {
                try
                {
                    await RunEnhancePdfProcessAsync(fileName, arguments, inputPath);
                    return;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    AppendDebugLog("pdf-enhance-debug.log", $"launcher-failed launcher={fileName} input={inputPath} detail={ex}");
                }
            }

            throw lastException ?? new InvalidOperationException("找不到可用的 Python 執行環境。");
        }

        private async Task RunEnhancePdfProcessAsync(string fileName, IReadOnlyList<string> arguments, string inputPath)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                throw new InvalidOperationException($"無法啟動 {fileName}。");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            AppendDebugLog(
                "pdf-enhance-debug.log",
                $"process-exit launcher={fileName} exitCode={process.ExitCode} input={inputPath} stdout={TrimForDebugPreview(stdout)} stderr={TrimForDebugPreview(stderr)}");

            if (process.ExitCode != 0)
            {
                var detail = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() :
                    !string.IsNullOrWhiteSpace(stdout) ? stdout.Trim() :
                    $"Python 結束代碼 {process.ExitCode.ToString(CultureInfo.InvariantCulture)}";
                throw new InvalidOperationException(detail);
            }
        }

        private static string BuildEnhancePdfCompletionRecord(
            IReadOnlyList<string> outputs,
            IReadOnlyList<string> failures,
            IReadOnlyList<FileEntry> skippedEntries)
        {
            var builder = new StringBuilder();
            builder.Append("完成：PDF 增強");

            if (outputs.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("輸出：");
                foreach (var output in outputs)
                {
                    builder.AppendLine(output);
                }
            }

            if (skippedEntries.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("略過：");
                foreach (var skippedEntry in skippedEntries)
                {
                    builder.AppendLine(skippedEntry.FullPath);
                }
            }

            if (failures.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine("失敗：");
                foreach (var failure in failures)
                {
                    builder.AppendLine(failure);
                }
            }

            return builder.ToString().TrimEnd();
        }
    }
}
