using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace nuone_tools
{
    public sealed partial class MainWindow
    {
        private const string DeployNodeDockerCommand = "nuone:deploy-node-docker";
        private const string OpenBuiltInTerminalCommand = "nuone:terminal";
        private const string OpenExternalTerminalCommand = "nuone:terminal-external";
        private const string TerminalCommandPrefix = "terminal";
        private const string DefaultNodeDockerUser = "admkuo";
        private const string DefaultNodeDockerHost = "docker05";
        private const string DefaultNodeDockerRemoteDirectory = "~/temp";

        private sealed class NodeDockerDeployOptions
        {
            public string User { get; init; } = DefaultNodeDockerUser;

            public string Host { get; init; } = DefaultNodeDockerHost;

            public string RemoteDirectory { get; init; } = DefaultNodeDockerRemoteDirectory;

            public NodeDockerLaunchMode LaunchMode { get; init; } = NodeDockerLaunchMode.ExternalWindow;

            public TerminalShellKind BuiltInShellKind { get; init; } = TerminalShellKind.PowerShell;
        }

        private sealed class BuiltInTerminalCommandOptions
        {
            public TerminalShellKind ShellKind { get; init; } = TerminalShellKind.PowerShell;

            public bool HasExplicitShellKind { get; init; }

            public string? WorkingDirectoryArgument { get; init; }

            public string? WindowArgument { get; init; }
        }

        private bool IsBuiltInToolbarCommand(string command)
        {
            return IsDeployNodeDockerCommand(command) ||
                IsOpenBuiltInTerminalCommand(command) ||
                IsOpenExternalTerminalCommand(command) ||
                string.Equals(command.Trim(), EnhancePdfCommand, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(command.Trim(), FileBunkerUploadCommand, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(command.Trim(), StorageUploadCommand, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDeployNodeDockerCommand(string command)
        {
            return string.Equals(command.Trim(), DeployNodeDockerCommand, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOpenBuiltInTerminalCommand(string command)
        {
            var trimmed = command.Trim();
            return string.Equals(trimmed, OpenBuiltInTerminalCommand, StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith($"{TerminalCommandPrefix}:", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, TerminalCommandPrefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOpenExternalTerminalCommand(string command)
        {
            return string.Equals(command.Trim(), OpenExternalTerminalCommand, StringComparison.OrdinalIgnoreCase);
        }

        private async Task ExecuteBuiltInToolbarCommandAsync(ToolbarCommandItem item)
        {
            if (string.Equals(item.Command, EnhancePdfCommand, StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteEnhancePdfAsync();
                return;
            }

            if (string.Equals(item.Command, FileBunkerUploadCommand, StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteFileBunkerUploadAsync();
                return;
            }

            if (string.Equals(item.Command, StorageUploadCommand, StringComparison.OrdinalIgnoreCase))
            {
                await ExecuteStorageUploadAsync();
                return;
            }

            if (IsDeployNodeDockerCommand(item.Command))
            {
                await DeploySelectedNodePackageToDockerAsync(_activePane, null, BuildNodeDockerDeployOptions(item));
                return;
            }

            if (IsOpenBuiltInTerminalCommand(item.Command))
            {
                await OpenBuiltInTerminalFromToolbarAsync(item);
                return;
            }

            if (IsOpenExternalTerminalCommand(item.Command))
            {
                await OpenExternalTerminalFromToolbarAsync(item);
            }
        }

        private async Task OpenBuiltInTerminalFromToolbarAsync(ToolbarCommandItem item)
        {
            var effectiveCommand = BuildBuiltInTerminalCommandText(item);
            var commandOptions = ParseBuiltInTerminalCommand(effectiveCommand);
            if (commandOptions is null)
            {
                await ShowMessageAsync("內建終端機按鈕無效", "terminal command 格式不正確。可使用 terminal:gitbash -d . -w 0。");
                return;
            }

            if (!string.IsNullOrWhiteSpace(commandOptions.WindowArgument) &&
                !string.Equals(commandOptions.WindowArgument, "0", StringComparison.Ordinal))
            {
                await ShowMessageAsync("內建終端機按鈕無效", "目前只支援 -w 0。");
                return;
            }

            var workingDirectory = ResolveToolbarTerminalWorkingDirectory(item, commandOptions);
            if (string.IsNullOrWhiteSpace(workingDirectory) || !IsNavigableDirectoryPath(workingDirectory))
            {
                await ShowMessageAsync("內建終端機按鈕無效", "指定的工作目錄不存在，請重新設定工具列按鈕。");
                return;
            }

            var shellKind = ResolveToolbarTerminalShellKind(item, commandOptions);
            OpenBuiltInTerminalTab(shellKind, NormalizePath(workingDirectory));
        }

        private static string BuildBuiltInTerminalCommandText(ToolbarCommandItem item)
        {
            var baseCommand = item.Command?.Trim() ?? string.Empty;
            var arguments = item.TerminalLaunchArguments?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return baseCommand;
            }

            if (string.Equals(baseCommand, OpenBuiltInTerminalCommand, StringComparison.OrdinalIgnoreCase))
            {
                return $"{TerminalCommandPrefix} {arguments}";
            }

            if (string.Equals(baseCommand, TerminalCommandPrefix, StringComparison.OrdinalIgnoreCase) ||
                baseCommand.StartsWith($"{TerminalCommandPrefix}:", StringComparison.OrdinalIgnoreCase))
            {
                return $"{baseCommand} {arguments}";
            }

            return baseCommand;
        }

        private async Task OpenExternalTerminalFromToolbarAsync(ToolbarCommandItem item)
        {
            var effectiveCommand = BuildBuiltInTerminalCommandText(new ToolbarCommandItem
            {
                Command = OpenBuiltInTerminalCommand,
                TerminalLaunchArguments = item.TerminalLaunchArguments,
            });
            var commandOptions = ParseBuiltInTerminalCommand(effectiveCommand);
            if (commandOptions is null)
            {
                await ShowMessageAsync("外部終端機按鈕無效", "terminal command 格式不正確。可使用 terminal:gitbash -d . -w 0。");
                return;
            }

            if (!string.IsNullOrWhiteSpace(commandOptions.WindowArgument) &&
                !string.Equals(commandOptions.WindowArgument, "0", StringComparison.Ordinal))
            {
                await ShowMessageAsync("外部終端機按鈕無效", "目前只支援 -w 0。");
                return;
            }

            var workingDirectory = ResolveToolbarTerminalWorkingDirectory(item, commandOptions);
            if (string.IsNullOrWhiteSpace(workingDirectory) || !IsNavigableDirectoryPath(workingDirectory))
            {
                await ShowMessageAsync("外部終端機按鈕無效", "指定的工作目錄不存在，請重新設定工具列按鈕。");
                return;
            }

            var shellKind = ResolveToolbarTerminalShellKind(item, commandOptions);

            try
            {
                LaunchExternalTerminal(shellKind, NormalizePath(workingDirectory), commandOptions.WindowArgument);
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("啟動外部終端機失敗", ex.Message);
            }
        }

        private void LaunchExternalTerminal(TerminalShellKind shellKind, string workingDirectory, string? windowArgument)
        {
            var shellPath = ResolveTerminalShellPath(shellKind);
            var shellArguments = BuildShellArguments(shellKind);
            var wtArguments = new List<string>();

            if (!string.IsNullOrWhiteSpace(windowArgument))
            {
                wtArguments.Add("-w");
                wtArguments.Add(windowArgument);
            }

            wtArguments.Add("new-tab");
            wtArguments.Add("-d");
            wtArguments.Add(workingDirectory);
            wtArguments.Add(shellPath);

            if (!string.IsNullOrWhiteSpace(shellArguments))
            {
                wtArguments.AddRange(TokenizeCommand(shellArguments));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "wt.exe",
                UseShellExecute = true,
                WorkingDirectory = workingDirectory,
            };

            foreach (var argument in wtArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo);
        }

        private TerminalShellKind ResolveToolbarTerminalShellKind(
            ToolbarCommandItem item,
            BuiltInTerminalCommandOptions commandOptions)
        {
            return commandOptions.HasExplicitShellKind
                ? commandOptions.ShellKind
                : item.TerminalShellKind;
        }

        private string ResolveToolbarTerminalWorkingDirectory(
            ToolbarCommandItem item,
            BuiltInTerminalCommandOptions commandOptions)
        {
            if (!string.IsNullOrWhiteSpace(commandOptions.WorkingDirectoryArgument))
            {
                return ResolveToolbarTerminalWorkingDirectoryArgument(commandOptions.WorkingDirectoryArgument);
            }

            return ResolveTerminalWorkingDirectory(item.TerminalWorkingDirectoryMode, item.TerminalCustomWorkingDirectory);
        }

        private string ResolveToolbarTerminalWorkingDirectoryArgument(string rawArgument)
        {
            var argument = rawArgument.Trim();
            if (string.Equals(argument, ".", StringComparison.Ordinal))
            {
                return _activePane.CurrentPath?.Trim() ?? string.Empty;
            }

            if (string.Equals(argument, "left", StringComparison.OrdinalIgnoreCase))
            {
                return LeftPane.CurrentPath?.Trim() ?? string.Empty;
            }

            if (string.Equals(argument, "right", StringComparison.OrdinalIgnoreCase))
            {
                return RightPane.CurrentPath?.Trim() ?? string.Empty;
            }

            if (Path.IsPathRooted(argument))
            {
                return argument;
            }

            var basePath = _activePane.CurrentPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(basePath) || !IsNavigableDirectoryPath(basePath))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(Path.Combine(basePath, argument));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static BuiltInTerminalCommandOptions? ParseBuiltInTerminalCommand(string command)
        {
            var trimmed = command.Trim();
            if (string.Equals(trimmed, OpenBuiltInTerminalCommand, StringComparison.OrdinalIgnoreCase))
            {
                return new BuiltInTerminalCommandOptions
                {
                    HasExplicitShellKind = false,
                };
            }

            if (string.Equals(trimmed, TerminalCommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return new BuiltInTerminalCommandOptions
                {
                    HasExplicitShellKind = false,
                };
            }

            if (!trimmed.StartsWith($"{TerminalCommandPrefix}:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var tokens = TokenizeCommand(trimmed);
            if (tokens.Count == 0)
            {
                return null;
            }

            var shellToken = tokens[0][(tokens[0].IndexOf(':') + 1)..].Trim();
            if (!TryParseTerminalShellKind(shellToken, out var shellKind))
            {
                return null;
            }

            string? workingDirectoryArgument = null;
            string? windowArgument = null;

            for (var index = 1; index < tokens.Count; index++)
            {
                var token = tokens[index];
                if (string.Equals(token, "-d", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= tokens.Count)
                    {
                        return null;
                    }

                    workingDirectoryArgument = tokens[++index];
                    continue;
                }

                if (token.StartsWith("-d", StringComparison.OrdinalIgnoreCase) && token.Length > 2)
                {
                    workingDirectoryArgument = token[2..];
                    continue;
                }

                if (string.Equals(token, "-w", StringComparison.OrdinalIgnoreCase))
                {
                    if (index + 1 >= tokens.Count)
                    {
                        return null;
                    }

                    windowArgument = tokens[++index];
                    continue;
                }

                if (token.StartsWith("-w", StringComparison.OrdinalIgnoreCase) && token.Length > 2)
                {
                    windowArgument = token[2..];
                    continue;
                }

                return null;
            }

            return new BuiltInTerminalCommandOptions
            {
                ShellKind = shellKind,
                HasExplicitShellKind = true,
                WorkingDirectoryArgument = workingDirectoryArgument,
                WindowArgument = windowArgument,
            };
        }

        private static bool TryParseTerminalShellKind(string rawValue, out TerminalShellKind shellKind)
        {
            switch (rawValue.Trim().ToLowerInvariant())
            {
                case "gitbash":
                case "git-bash":
                case "bash":
                    shellKind = TerminalShellKind.GitBash;
                    return true;
                case "cmd":
                case "commandprompt":
                case "command-prompt":
                    shellKind = TerminalShellKind.CommandPrompt;
                    return true;
                case "powershell":
                case "pwsh":
                    shellKind = TerminalShellKind.PowerShell;
                    return true;
                default:
                    shellKind = TerminalShellKind.PowerShell;
                    return false;
            }
        }

        private static List<string> TokenizeCommand(string command)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            var quote = '\0';

            foreach (var character in command)
            {
                if (quote == '\0' && char.IsWhiteSpace(character))
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }

                    continue;
                }

                if (character is '"' or '\'')
                {
                    if (quote == '\0')
                    {
                        quote = character;
                        continue;
                    }

                    if (quote == character)
                    {
                        quote = '\0';
                        continue;
                    }
                }

                current.Append(character);
            }

            if (quote != '\0')
            {
                return new List<string>();
            }

            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
            }

            return tokens;
        }

        private async Task DeploySelectedNodePackageToDockerAsync(
            PaneViewModel pane,
            IReadOnlyList<string>? selectedPaths,
            NodeDockerDeployOptions? defaultOptions = null)
        {
            var paths = selectedPaths?.Where(static path => !string.IsNullOrWhiteSpace(path)).ToList()
                ?? GetSelectedEntriesInDisplayOrder(pane).Select(static entry => entry.FullPath).ToList();

            if (paths.Count != 1)
            {
                await ShowMessageAsync("部署 Node.js 到 Docker", "請只選取一個要部署的 zip 檔。");
                return;
            }

            var packagePath = paths[0];
            if (!File.Exists(packagePath))
            {
                await ShowMessageAsync("部署 Node.js 到 Docker", $"找不到檔案：{packagePath}");
                return;
            }

            if (!string.Equals(Path.GetExtension(packagePath), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                await ShowMessageAsync("部署 Node.js 到 Docker", "目前只支援選取 .zip 部署包。");
                return;
            }

            var options = await ShowNodeDockerDeployDialogAsync(
                packagePath,
                defaultOptions ?? new NodeDockerDeployOptions());
            if (options is null)
            {
                return;
            }

            try
            {
                var launcherPath = CreateNodeDockerDeployLauncher(packagePath, options);
                if (options.LaunchMode == NodeDockerLaunchMode.BuiltInTerminal)
                {
                    var launcherDirectory = Path.GetDirectoryName(launcherPath);
                    if (string.IsNullOrWhiteSpace(launcherDirectory) || !Directory.Exists(launcherDirectory))
                    {
                        throw new InvalidOperationException("找不到部署啟動腳本所在目錄。");
                    }

                    OpenBuiltInTerminalTabAndRunCommand(
                        options.BuiltInShellKind,
                        launcherDirectory,
                        BuildInternalTerminalExecutionCommand(options.BuiltInShellKind, launcherPath));
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = launcherPath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                await ShowMessageAsync("部署 Node.js 到 Docker 失敗", ex.Message);
            }
        }

        private static NodeDockerDeployOptions BuildNodeDockerDeployOptions(ToolbarCommandItem item)
        {
            return new NodeDockerDeployOptions
            {
                User = string.IsNullOrWhiteSpace(item.NodeDockerUser)
                    ? DefaultNodeDockerUser
                    : item.NodeDockerUser.Trim(),
                Host = string.IsNullOrWhiteSpace(item.NodeDockerHost)
                    ? DefaultNodeDockerHost
                    : item.NodeDockerHost.Trim(),
                RemoteDirectory = string.IsNullOrWhiteSpace(item.NodeDockerRemoteDirectory)
                    ? DefaultNodeDockerRemoteDirectory
                    : item.NodeDockerRemoteDirectory.Trim(),
                LaunchMode = item.NodeDockerLaunchMode,
                BuiltInShellKind = item.TerminalShellKind,
            };
        }

        private static string BuildInternalTerminalExecutionCommand(TerminalShellKind shellKind, string launcherPath)
        {
            return shellKind switch
            {
                TerminalShellKind.CommandPrompt => $"\"{launcherPath}\"",
                TerminalShellKind.GitBash => $"cmd.exe /c \"\\\"{launcherPath}\\\"\"",
                _ => $"& '{launcherPath.Replace("'", "''", StringComparison.Ordinal)}'",
            };
        }

        private async Task<NodeDockerDeployOptions?> ShowNodeDockerDeployDialogAsync(
            string packagePath,
            NodeDockerDeployOptions defaultOptions)
        {
            var userTextBox = new TextBox { Text = defaultOptions.User };
            var hostTextBox = new TextBox { Text = defaultOptions.Host };
            var remoteDirectoryTextBox = new TextBox { Text = defaultOptions.RemoteDirectory };

            var panel = new StackPanel
            {
                Spacing = 10,
                Width = 560,
            };
            panel.Children.Add(new TextBlock
            {
                Text = $"部署檔案：{Path.GetFileName(packagePath)}",
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(new TextBlock { Text = "SSH 使用者" });
            panel.Children.Add(userTextBox);
            panel.Children.Add(new TextBlock { Text = "Docker 主機" });
            panel.Children.Add(hostTextBox);
            panel.Children.Add(new TextBlock { Text = "遠端暫存目錄" });
            panel.Children.Add(remoteDirectoryTextBox);
            panel.Children.Add(new TextBlock
            {
                Text = "會開啟命令視窗執行 scp、ssh、unzip、chmod、dos2unix、sudo ./init.sh，並在偵測到 init.sh 輸出的 sudo docker run 指令後直接執行。完成後會自動刪除 zip、部署腳本與解壓暫存資料夾。若 sudo 需要密碼，請在視窗中輸入。",
                Opacity = 0.78,
                TextWrapping = TextWrapping.Wrap,
            });

            var dialog = new ContentDialog
            {
                Title = "部署 Node.js 到 Docker",
                Content = panel,
                PrimaryButtonText = "開始部署",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootLayout.XamlRoot,
            };
            ApplyThemeToDialog(dialog);

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return null;
            }

            var user = userTextBox.Text.Trim();
            var host = hostTextBox.Text.Trim();
            var remoteDirectory = remoteDirectoryTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(user) ||
                string.IsNullOrWhiteSpace(host) ||
                string.IsNullOrWhiteSpace(remoteDirectory))
            {
                await ShowMessageAsync("部署 Node.js 到 Docker", "SSH 使用者、主機與遠端暫存目錄都必須填寫。");
                return null;
            }

            return new NodeDockerDeployOptions
            {
                User = user,
                Host = host,
                RemoteDirectory = remoteDirectory,
                LaunchMode = defaultOptions.LaunchMode,
                BuiltInShellKind = defaultOptions.BuiltInShellKind,
            };
        }

        private static string CreateNodeDockerDeployLauncher(string packagePath, NodeDockerDeployOptions options)
        {
            var workDirectory = Path.Combine(Path.GetTempPath(), "nuone-tools", "node-docker-deploy");
            Directory.CreateDirectory(workDirectory);

            var fileName = Path.GetFileName(packagePath);
            var stamp = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            var token = Guid.NewGuid().ToString("N");
            var remoteScriptPath = Path.Combine(workDirectory, $"deploy-node-{stamp}-{token}.sh");
            var launcherPath = Path.Combine(workDirectory, $"deploy-node-{stamp}-{token}.cmd");

            File.WriteAllText(
                remoteScriptPath,
                NormalizeToLf(BuildNodeDockerRemoteScript(fileName, options.RemoteDirectory)),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            File.WriteAllText(
                launcherPath,
                BuildNodeDockerLauncherScript(packagePath, remoteScriptPath, fileName, options),
                Encoding.ASCII);

            return launcherPath;
        }

        private static string BuildNodeDockerLauncherScript(
            string packagePath,
            string remoteScriptPath,
            string fileName,
            NodeDockerDeployOptions options)
        {
            var remoteLogin = $"{options.User}@{options.Host}";
            var remoteDirectory = options.RemoteDirectory.TrimEnd('/');
            var remotePackageTarget = $"{remoteLogin}:{remoteDirectory}/";
            var remoteScriptTarget = $"{remoteLogin}:{remoteDirectory}/.nuone-node-deploy.sh";
            var remoteMkdirCommand = $"mkdir -p {BuildBashPathArgument(options.RemoteDirectory)}";
            var remoteScriptCommand = $"cd {BuildBashPathArgument(options.RemoteDirectory)} && bash .nuone-node-deploy.sh";

            var builder = new StringBuilder();
            builder.AppendLine("@echo off");
            builder.AppendLine("setlocal");
            builder.AppendLine("chcp 65001 >nul");
            builder.AppendLine("echo [Nuone Tools] Deploy Node.js package to Docker");
            builder.AppendLine($"echo Package: {EscapeBatchEcho(fileName)}");
            builder.AppendLine($"echo Target : {EscapeBatchEcho(remoteLogin)}:{EscapeBatchEcho(options.RemoteDirectory)}");
            builder.AppendLine("echo.");
            builder.AppendLine("echo [1/4] Prepare remote temp directory...");
            builder.AppendLine($"ssh {QuoteBatchArgument(remoteLogin)} {QuoteBatchArgument(remoteMkdirCommand)}");
            builder.AppendLine("if errorlevel 1 goto fail");
            builder.AppendLine("echo.");
            builder.AppendLine("echo [2/4] Upload package...");
            builder.AppendLine($"scp {QuoteBatchArgument(packagePath)} {QuoteBatchArgument(remotePackageTarget)}");
            builder.AppendLine("if errorlevel 1 goto fail");
            builder.AppendLine("echo.");
            builder.AppendLine("echo [3/4] Upload deploy script...");
            builder.AppendLine($"scp {QuoteBatchArgument(remoteScriptPath)} {QuoteBatchArgument(remoteScriptTarget)}");
            builder.AppendLine("if errorlevel 1 goto fail");
            builder.AppendLine("echo.");
            builder.AppendLine("echo [4/4] Run remote deploy script...");
            builder.AppendLine($"ssh -t {QuoteBatchArgument(remoteLogin)} {QuoteBatchArgument(remoteScriptCommand)}");
            builder.AppendLine("if errorlevel 1 goto fail");
            builder.AppendLine("echo.");
            builder.AppendLine("echo Deploy completed.");
            builder.AppendLine("goto end");
            builder.AppendLine(":fail");
            builder.AppendLine("echo.");
            builder.AppendLine("echo Deploy failed.");
            builder.AppendLine(":end");
            builder.AppendLine("echo.");
            builder.AppendLine("pause");
            return builder.ToString();
        }

        private static string BuildNodeDockerRemoteScript(string fileName, string remoteDirectory)
        {
            var builder = new StringBuilder();
            builder.AppendLine("#!/usr/bin/env bash");
            builder.AppendLine("set -euo pipefail");
            builder.AppendLine($"remote_dir={BuildBashPathValue(remoteDirectory)}");
            builder.AppendLine($"archive={QuoteBashSingle(fileName)}");
            builder.AppendLine("work_dir=\".nuone-node-deploy\"");
            builder.AppendLine("script_path=\"$remote_dir/.nuone-node-deploy.sh\"");
            builder.AppendLine("archive_path=\"$remote_dir/$archive\"");
            builder.AppendLine("init_log=\"$(mktemp)\"");
            builder.AppendLine("cleanup() {");
            builder.AppendLine("  rm -f \"$init_log\"");
            builder.AppendLine("  rm -f \"$archive_path\"");
            builder.AppendLine("  rm -f \"$script_path\"");
            builder.AppendLine("  rm -rf \"$remote_dir/$work_dir\"");
            builder.AppendLine("}");
            builder.AppendLine("trap cleanup EXIT");
            builder.AppendLine("mkdir -p \"$remote_dir\"");
            builder.AppendLine("cd \"$remote_dir\"");
            builder.AppendLine("rm -rf \"$work_dir\"");
            builder.AppendLine("mkdir -p \"$work_dir\"");
            builder.AppendLine("unzip -oq \"$archive\" -d \"$work_dir\"");
            builder.AppendLine("init_file=\"$(find \"$work_dir\" -maxdepth 3 -type f -name init.sh | head -n 1)\"");
            builder.AppendLine("if [ -z \"$init_file\" ]; then");
            builder.AppendLine("  echo \"init.sh not found after extraction\"");
            builder.AppendLine("  exit 1");
            builder.AppendLine("fi");
            builder.AppendLine("cd \"$(dirname \"$init_file\")\"");
            builder.AppendLine("chmod +x init.sh");
            builder.AppendLine("if command -v dos2unix >/dev/null 2>&1; then");
            builder.AppendLine("  dos2unix init.sh");
            builder.AppendLine("else");
            builder.AppendLine("  sed -i 's/\\r$//' init.sh");
            builder.AppendLine("fi");
            builder.AppendLine("sudo ./init.sh | tee \"$init_log\"");
            builder.AppendLine("docker_cmd=\"$(grep -E '^[[:space:]]*sudo docker run ' \"$init_log\" | tail -n 1 | sed 's/^[[:space:]]*//')\"");
            builder.AppendLine("if [ -z \"$docker_cmd\" ]; then");
            builder.AppendLine("  echo \"docker run command not found in init.sh output\"");
            builder.AppendLine("  exit 1");
            builder.AppendLine("fi");
            builder.AppendLine("echo \"Executing generated container command...\"");
            builder.AppendLine("eval \"$docker_cmd\"");
            return builder.ToString();
        }

        private static string NormalizeToLf(string value)
        {
            return value.Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        private static string BuildBashPathValue(string value)
        {
            var trimmed = value.Trim();
            if (string.Equals(trimmed, "~", StringComparison.Ordinal))
            {
                return "\"$HOME\"";
            }

            if (trimmed.StartsWith("~/", StringComparison.Ordinal))
            {
                return $"\"$HOME/{EscapeBashDouble(trimmed[2..])}\"";
            }

            return QuoteBashSingle(trimmed);
        }

        private static string BuildBashPathArgument(string value)
        {
            var trimmed = value.Trim();
            if (string.Equals(trimmed, "~", StringComparison.Ordinal))
            {
                return "~";
            }

            if (trimmed.StartsWith("~/", StringComparison.Ordinal))
            {
                var rest = trimmed[2..];
                if (string.IsNullOrWhiteSpace(rest))
                {
                    return "~";
                }

                return IsSimpleBashPath(rest) ? $"~/{rest}" : $"~/{QuoteBashSingle(rest)}";
            }

            return IsSimpleBashPath(trimmed) ? trimmed : QuoteBashSingle(trimmed);
        }

        private static string QuoteBatchArgument(string value)
        {
            return $"\"{EscapeBatchLiteral(value).Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }

        private static string EscapeBatchEcho(string value)
        {
            return EscapeBatchLiteral(value)
                .Replace("^", "^^", StringComparison.Ordinal)
                .Replace("&", "^&", StringComparison.Ordinal)
                .Replace("|", "^|", StringComparison.Ordinal)
                .Replace("<", "^<", StringComparison.Ordinal)
                .Replace(">", "^>", StringComparison.Ordinal);
        }

        private static string EscapeBatchLiteral(string value)
        {
            return value.Replace("%", "%%", StringComparison.Ordinal);
        }

        private static string QuoteBashSingle(string value)
        {
            return $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
        }

        private static string EscapeBashDouble(string value)
        {
            return value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("$", "\\$", StringComparison.Ordinal)
                .Replace("`", "\\`", StringComparison.Ordinal);
        }

        private static bool IsSimpleBashPath(string value)
        {
            return value.Length > 0 && value.All(static character =>
                char.IsLetterOrDigit(character) ||
                character == '/' ||
                character == '_' ||
                character == '-' ||
                character == '.');
        }
    }
}
