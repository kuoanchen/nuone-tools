using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace nuone_tools
{
    public sealed partial class MainWindow
    {
        private const int TerminalMaxOutputLength = 64000;
        private const short TerminalDefaultColumns = 120;
        private const short TerminalDefaultRows = 32;
        private readonly DispatcherQueueTimer? _terminalCursorTimer;
        private bool _isTerminalCursorVisible = true;
        private bool _isTerminalHostFocused;
        private bool _isTerminalRenderQueued;

        internal void SendTerminalCommand_Click(object sender, RoutedEventArgs e)
        {
            _ = SendTerminalCommandAsync();
        }

        internal void TerminalCommandTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            _ = SendTerminalCommandAsync();
        }

        internal void TerminalHost_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
        {
            var character = (char)args.Character;
            if (character < ' ' || character == '\u007f' || IsControlKeyDown())
            {
                return;
            }

            args.Handled = true;
            _isTerminalCursorVisible = true;
            RequestTerminalRender();
            _ = SendTerminalRawInputAsync(character.ToString());
        }

        internal void TerminalHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (IsControlKeyDown() && e.Key == Windows.System.VirtualKey.V)
            {
                e.Handled = true;
                _isTerminalCursorVisible = true;
                _ = PasteClipboardToTerminalAsync();
                return;
            }

            if (!TryBuildTerminalKeyInput(e.Key, out var input))
            {
                return;
            }

            e.Handled = true;
            _isTerminalCursorVisible = true;
            RequestTerminalRender();
            _ = SendTerminalRawInputAsync(input);
        }

        internal void TerminalHost_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _ = TerminalHost.Focus(FocusState.Pointer);
        }

        internal void TerminalHost_GotFocus(object sender, RoutedEventArgs e)
        {
            _isTerminalHostFocused = true;
            _isTerminalCursorVisible = true;
            _terminalCursorTimer?.Start();
            UpdateTerminalUi();
        }

        internal void TerminalHost_LostFocus(object sender, RoutedEventArgs e)
        {
            _isTerminalHostFocused = false;
            _isTerminalCursorVisible = false;
            _terminalCursorTimer?.Stop();
            UpdateTerminalUi();
        }

        internal void RestartTerminal_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTerminalTab is null)
            {
                return;
            }

            RestartTerminalProcess(_selectedTerminalTab);
        }

        internal void ClearTerminalOutput_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedTerminalTab is null)
            {
                return;
            }

            _selectedTerminalTab.OutputText = string.Empty;
            _selectedTerminalTab.StatusText = "輸出已清空";
            UpdateTerminalUi();
        }

        internal void SyncTerminalWorkingDirectory_Click(object sender, RoutedEventArgs e)
        {
            SyncTerminalWorkingDirectoryFromActivePane();
        }

        internal void TerminalTabsView_AddTabButtonClick(TabView sender, object args)
        {
            var shellKind = _selectedTerminalTab?.ShellKind ?? GetDefaultTerminalShellKind();
            AddTerminalTab(shellKind, true);
        }

        internal void TerminalTabsView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            if (args.Item is not TabViewItem { Tag: TerminalTabSession session })
            {
                return;
            }

            RemoveTerminalTab(session);
        }

        internal void TerminalTabsView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TerminalTabsView.SelectedItem is TabViewItem { Tag: TerminalTabSession session })
            {
                SelectTerminalTab(session);
            }
        }

        internal void TerminalShellComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedTerminalTab is null ||
                TerminalShellComboBox.SelectedItem is not ComboBoxItem { Tag: string tag } ||
                !Enum.TryParse<TerminalShellKind>(tag, out var shellKind) ||
                _selectedTerminalTab.ShellKind == shellKind)
            {
                return;
            }

            _selectedTerminalTab.ShellKind = shellKind;
            _selectedTerminalTab.Title = BuildTerminalTabTitle(_selectedTerminalTab);
            UpdateTerminalTabHeader(_selectedTerminalTab);
            RestartTerminalProcess(_selectedTerminalTab);
        }

        internal void OpenBuiltInTerminalTab(TerminalShellKind shellKind, string workingDirectory)
        {
            EnsureTerminalTabExists();
            AddTerminalTab(shellKind, true, workingDirectory);
            SwitchToAppSection(AppSection.Terminal);
            FocusTerminalHost();
        }

        internal void OpenBuiltInTerminalTabAndRunCommand(TerminalShellKind shellKind, string workingDirectory, string command)
        {
            OpenBuiltInTerminalTab(shellKind, workingDirectory);

            var session = _selectedTerminalTab;
            if (session is null)
            {
                return;
            }

            EnsureTerminalProcessStarted(session);
            _ = SendTerminalInternalCommandAsync(session, command);
            session.StatusText = $"命令已送出 · {session.WorkingDirectory}";
            UpdateTerminalUi();
        }

        private async Task SendTerminalCommandAsync()
        {
            var session = _selectedTerminalTab;
            if (session is null)
            {
                return;
            }

            EnsureTerminalProcessStarted(session);
            var command = TerminalCommandTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            if (session.ConPtyContext is null || session.Process is null || session.Process.HasExited)
            {
                AppendTerminalOutputLine(session, "[system] Terminal 尚未啟動。");
                session.StatusText = "Terminal 尚未啟動";
                UpdateTerminalUi();
                return;
            }

            TerminalCommandTextBox.Text = string.Empty;

            try
            {
                await WriteToTerminalAsync(session.ConPtyContext, command + "\r\n");
                session.StatusText = $"命令已送出 · {session.WorkingDirectory}";
            }
            catch (Exception ex)
            {
                AppendTerminalOutputLine(session, $"[system] 送出命令失敗：{ex.Message}");
                session.StatusText = "送出命令失敗";
            }

            UpdateTerminalUi();
        }

        private async Task SendTerminalRawInputAsync(string input)
        {
            var session = _selectedTerminalTab;
            if (session is null || string.IsNullOrEmpty(input))
            {
                return;
            }

            EnsureTerminalProcessStarted(session);
            if (session.ConPtyContext is null || session.Process is null || session.Process.HasExited)
            {
                return;
            }

            try
            {
                await WriteToTerminalAsync(session.ConPtyContext, input);
            }
            catch (Exception ex)
            {
                AppendTerminalOutputLine(session, $"[system] 送出 terminal 輸入失敗：{ex.Message}");
                session.StatusText = "送出輸入失敗";
                UpdateTerminalUi();
            }
        }

        internal bool TryInterruptActiveTerminal()
        {
            var session = _selectedTerminalTab;
            if (_activeSection != AppSection.Terminal ||
                session is null ||
                session.ConPtyContext is null ||
                session.Process is null ||
                session.Process.HasExited)
            {
                return false;
            }

            _isTerminalCursorVisible = true;
            RequestTerminalRender();
            var processToken = session.ProcessToken;
            _ = SendTerminalRawInputAsync("\u0003");
            session.StatusText = $"已送出中斷訊號 · {session.WorkingDirectory}";
            UpdateTerminalUi();
            _ = EnsureTerminalInterruptCompletesAsync(session, processToken);
            return true;
        }

        private async Task EnsureTerminalInterruptCompletesAsync(TerminalTabSession session, Guid processToken)
        {
            await Task.Delay(900);

            if (session.ProcessToken != processToken ||
                session.Process is null ||
                session.Process.HasExited)
            {
                return;
            }

            await EnqueueOnUiAsync(() =>
            {
                if (session.ProcessToken != processToken ||
                    session.Process is null ||
                    session.Process.HasExited)
                {
                    return;
                }

                AppendTerminalOutputLine(session, "[system] Ctrl+C 未停止目前程序，已重新啟動此終端機。");
                RestartTerminalProcess(session);
            });
        }

        private async Task PasteClipboardToTerminalAsync()
        {
            try
            {
                var content = Clipboard.GetContent();
                if (!content.Contains(StandardDataFormats.Text))
                {
                    return;
                }

                var text = await content.GetTextAsync();
                if (!string.IsNullOrEmpty(text))
                {
                    await SendTerminalRawInputAsync(text);
                }
            }
            catch (Exception ex)
            {
                if (_selectedTerminalTab is not null)
                {
                    AppendTerminalOutputLine(_selectedTerminalTab, $"[system] 貼上失敗：{ex.Message}");
                }
            }
        }

        private static bool TryBuildTerminalKeyInput(Windows.System.VirtualKey key, out string input)
        {
            input = string.Empty;

            if (IsControlKeyDown())
            {
                input = key switch
                {
                    Windows.System.VirtualKey.C => "\u0003",
                    Windows.System.VirtualKey.D => "\u0004",
                    Windows.System.VirtualKey.L => "\u000c",
                    Windows.System.VirtualKey.Z => "\u001a",
                    _ => string.Empty,
                };

                return input.Length > 0;
            }

            input = key switch
            {
                Windows.System.VirtualKey.Enter => "\r",
                Windows.System.VirtualKey.Back => "\u007f",
                Windows.System.VirtualKey.Tab => "\t",
                Windows.System.VirtualKey.Escape => "\u001b",
                Windows.System.VirtualKey.Up => "\u001b[A",
                Windows.System.VirtualKey.Down => "\u001b[B",
                Windows.System.VirtualKey.Right => "\u001b[C",
                Windows.System.VirtualKey.Left => "\u001b[D",
                Windows.System.VirtualKey.Delete => "\u001b[3~",
                Windows.System.VirtualKey.Home => "\u001b[H",
                Windows.System.VirtualKey.End => "\u001b[F",
                Windows.System.VirtualKey.PageUp => "\u001b[5~",
                Windows.System.VirtualKey.PageDown => "\u001b[6~",
                _ => string.Empty,
            };

            return input.Length > 0;
        }

        private static bool IsControlKeyDown()
        {
            return IsVirtualKeyDown(Windows.System.VirtualKey.Control) ||
                IsVirtualKeyDown(Windows.System.VirtualKey.LeftControl) ||
                IsVirtualKeyDown(Windows.System.VirtualKey.RightControl);
        }

        private static bool IsVirtualKeyDown(Windows.System.VirtualKey key)
        {
            return (GetKeyState((int)key) & 0x8000) != 0;
        }

        private void EnsureTerminalTabExists()
        {
            if (TerminalTabs.Count > 0)
            {
                if (_selectedTerminalTab is null)
                {
                    SelectTerminalTab(TerminalTabs[0]);
                }

                return;
            }

            AddTerminalTab(GetDefaultTerminalShellKind(), true);
        }

        private void AddTerminalTab(TerminalShellKind shellKind, bool shouldSelect, string? workingDirectoryOverride = null)
        {
            var workingDirectory = string.IsNullOrWhiteSpace(workingDirectoryOverride)
                ? GetPreferredTerminalWorkingDirectory()
                : workingDirectoryOverride.Trim();
            var session = new TerminalTabSession
            {
                TabNumber = _nextTerminalTabNumber++,
                ShellKind = shellKind,
                WorkingDirectory = workingDirectory,
                StatusText = "未啟動",
            };
            session.Title = BuildTerminalTabTitle(session);
            session.ShellPath = ResolveTerminalShellPath(shellKind);

            TerminalTabs.Add(session);
            var tabItem = BuildTerminalTabViewItem(session);
            TerminalTabsView.TabItems.Add(tabItem);

            if (shouldSelect)
            {
                TerminalTabsView.SelectedItem = tabItem;
                SelectTerminalTab(session);
                EnsureTerminalProcessStarted(session);
            }
        }

        private void RemoveTerminalTab(TerminalTabSession session)
        {
            StopTerminalProcess(session);

            var tabItem = FindTerminalTabViewItem(session);
            if (tabItem is not null)
            {
                TerminalTabsView.TabItems.Remove(tabItem);
            }

            TerminalTabs.Remove(session);

            if (TerminalTabs.Count == 0)
            {
                _selectedTerminalTab = null;
                AddTerminalTab(GetDefaultTerminalShellKind(), true);
                return;
            }

            var next = TerminalTabs.LastOrDefault();
            if (next is not null)
            {
                TerminalTabsView.SelectedItem = FindTerminalTabViewItem(next);
                SelectTerminalTab(next);
            }
        }

        private void SelectTerminalTab(TerminalTabSession session)
        {
            _selectedTerminalTab = session;
            EnsureTerminalProcessStarted(session);
            UpdateTerminalUi();
            UpdateSharedStatusBar();
            FocusTerminalHost();
        }

        private void FocusTerminalHost()
        {
            DispatcherQueue.TryEnqueue(() => _ = TerminalHost.Focus(FocusState.Programmatic));
        }

        private void InitializeTerminalCursorTimer()
        {
            if (_terminalCursorTimer is null)
            {
                return;
            }

            _terminalCursorTimer.Interval = TimeSpan.FromMilliseconds(530);
            _terminalCursorTimer.IsRepeating = true;
            _terminalCursorTimer.Tick += TerminalCursorTimer_Tick;
        }

        private void TerminalCursorTimer_Tick(DispatcherQueueTimer sender, object args)
        {
            if (!_isTerminalHostFocused || _selectedTerminalTab is null || !_selectedTerminalTab.IsRunning)
            {
                _isTerminalCursorVisible = false;
                return;
            }

            _isTerminalCursorVisible = !_isTerminalCursorVisible;
            RequestTerminalRender();
        }

        private void SyncTerminalWorkingDirectoryFromActivePane()
        {
            var session = _selectedTerminalTab;
            if (session is null)
            {
                return;
            }

            var candidate = _activePane.CurrentPath;
            if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
            {
                session.StatusText = "目前 pane 路徑不可用，保留原本的工作目錄";
                UpdateTerminalUi();
                return;
            }

            session.WorkingDirectory = candidate;
            if (session.ConPtyContext is not null && session.Process is not null && !session.Process.HasExited)
            {
                _ = SendTerminalInternalCommandAsync(session, BuildWorkingDirectoryCommand(session.ShellKind, candidate));
            }

            session.StatusText = $"工作目錄已同步到 {session.WorkingDirectory}";
            UpdateTerminalUi();
        }

        private void EnsureTerminalProcessStarted(TerminalTabSession session)
        {
            if (session.IsStarting)
            {
                return;
            }

            if (session.Process is not null && !session.Process.HasExited && session.ConPtyContext is not null)
            {
                UpdateTerminalUi();
                return;
            }

            session.IsStarting = true;
            session.ProcessToken = Guid.NewGuid();

            try
            {
                session.ShellPath = ResolveTerminalShellPath(session.ShellKind);
                if (!File.Exists(session.ShellPath) && !IsPathLikeCommand(session.ShellPath))
                {
                    AppendTerminalOutputLine(session, $"[system] 找不到 {session.ShellDisplayName} 可執行檔。");
                    session.StatusText = $"{session.ShellDisplayName} 不可用";
                    UpdateTerminalUi();
                    return;
                }

                if (!Directory.Exists(session.WorkingDirectory))
                {
                    session.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }

                var launch = TerminalConPtyNative.CreatePseudoConsoleProcess(
                    session.ShellPath,
                    BuildShellArguments(session.ShellKind),
                    session.WorkingDirectory,
                    TerminalDefaultColumns,
                    TerminalDefaultRows);

                session.ConPtyContext = launch.Context;
                session.Process = Process.GetProcessById(launch.ProcessId);
                session.Process.EnableRaisingEvents = true;
                var processToken = session.ProcessToken;
                session.Process.Exited += (_, _) => TerminalProcess_Exited(session, processToken);
                _ = Task.Run(() => PumpTerminalOutputAsync(session, launch.Context, processToken));

                AppendTerminalOutputLine(session, $"[system] Terminal 已啟動：{session.ShellDisplayName}");
                AppendTerminalOutputLine(session, $"[system] 工作目錄：{session.WorkingDirectory}");
                session.StatusText = "執行中";
                session.IsRunning = true;
                UpdateTerminalUi();
            }
            catch (Exception ex)
            {
                CleanupTerminalRuntime(session);
                AppendTerminalOutputLine(session, $"[system] 啟動 terminal 失敗：{ex.Message}");
                session.StatusText = "啟動 terminal 失敗";
                session.IsRunning = false;
                UpdateTerminalUi();
            }
            finally
            {
                session.IsStarting = false;
            }
        }

        private void RestartTerminalProcess(TerminalTabSession session)
        {
            StopTerminalProcess(session);
            session.OutputText = string.Empty;
            EnsureTerminalProcessStarted(session);
        }

        private void StopAllTerminalProcesses()
        {
            foreach (var session in TerminalTabs.ToList())
            {
                StopTerminalProcess(session);
            }
        }

        private void StopTerminalProcess(TerminalTabSession session)
        {
            var process = session.Process;
            var context = session.ConPtyContext;
            session.ProcessToken = Guid.Empty;
            session.Process = null;
            session.ConPtyContext = null;

            if (process is null && context is null)
            {
                session.IsRunning = false;
                return;
            }

            try
            {
                if (context is not null)
                {
                    try
                    {
                        _ = WriteToTerminalAsync(context, "exit\r\n");
                    }
                    catch
                    {
                    }
                }

                if (process is not null && !process.HasExited)
                {
                    if (!process.WaitForExit(1000))
                    {
                        process.Kill(true);
                    }
                }
            }
            catch
            {
            }
            finally
            {
                context?.Dispose();
                process?.Dispose();
                session.IsRunning = false;
                session.StatusText = "未啟動";
            }
        }

        private async Task PumpTerminalOutputAsync(TerminalTabSession session, ConPtyRuntimeContext context, Guid processToken)
        {
            var buffer = new byte[4096];
            while (session.ProcessToken == processToken)
            {
                try
                {
                    var bytesRead = await Task.Run(() => context.OutputReader.Read(buffer, 0, buffer.Length));
                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    AppendTerminalOutput(session, Encoding.UTF8.GetString(buffer, 0, bytesRead));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    AppendTerminalOutputLine(session, $"[system] 讀取 terminal 輸出失敗：{ex.Message}");
                    break;
                }
            }
        }

        private void TerminalProcess_Exited(TerminalTabSession session, Guid processToken)
        {
            if (session.ProcessToken != processToken)
            {
                return;
            }

            session.ProcessToken = Guid.Empty;
            CleanupTerminalRuntime(session);
            session.Process = null;
            session.IsRunning = false;
            session.StatusText = "Terminal 已結束";
            AppendTerminalOutputLine(session, "[system] Terminal 已結束。");
            UpdateTerminalUi();
        }

        private void AppendTerminalOutputLine(TerminalTabSession session, string line)
        {
            AppendTerminalOutput(session, $"{line}{Environment.NewLine}");
        }

        private void AppendTerminalOutput(TerminalTabSession session, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                var next = session.OutputText + text;
                if (next.Length > TerminalMaxOutputLength)
                {
                    next = next[^TerminalMaxOutputLength..];
                }

                session.OutputText = next;
                if (ReferenceEquals(session, _selectedTerminalTab))
                {
                    RequestTerminalRender();
                }
            });
        }

        private async Task SendTerminalInternalCommandAsync(TerminalTabSession session, string command)
        {
            if (session.ConPtyContext is null || session.Process is null || session.Process.HasExited)
            {
                return;
            }

            try
            {
                await WriteToTerminalAsync(session.ConPtyContext, command + "\r\n");
            }
            catch (Exception ex)
            {
                AppendTerminalOutputLine(session, $"[system] 更新工作目錄失敗：{ex.Message}");
            }
        }

        private static async Task WriteToTerminalAsync(ConPtyRuntimeContext context, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await Task.Run(() =>
            {
                context.InputWriter.Write(bytes, 0, bytes.Length);
                context.InputWriter.Flush();
            });
        }

        private void CleanupTerminalRuntime(TerminalTabSession session)
        {
            try
            {
                session.ConPtyContext?.Dispose();
            }
            catch
            {
            }

            session.ConPtyContext = null;

            try
            {
                session.Process?.Dispose();
            }
            catch
            {
            }

            session.Process = null;
        }

        private void UpdateTerminalUi()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                EnsureTerminalTabExists();

                if (_selectedTerminalTab is null)
                {
                    return;
                }

                TerminalShellTextBlock.Text = string.IsNullOrWhiteSpace(_selectedTerminalTab.ShellPath)
                    ? "未指定"
                    : _selectedTerminalTab.ShellPath;
                TerminalWorkingDirectoryTextBlock.Text = string.IsNullOrWhiteSpace(_selectedTerminalTab.WorkingDirectory)
                    ? "未指定"
                    : _selectedTerminalTab.WorkingDirectory;
                TerminalStatusTextBlock.Text = _selectedTerminalTab.StatusText;
                RequestTerminalRender();

                var selectedTag = _selectedTerminalTab.ShellKind.ToString();
                foreach (var item in TerminalShellComboBox.Items.OfType<ComboBoxItem>())
                {
                    if (string.Equals(item.Tag as string, selectedTag, StringComparison.Ordinal))
                    {
                        TerminalShellComboBox.SelectedItem = item;
                        break;
                    }
                }

                var tabItem = FindTerminalTabViewItem(_selectedTerminalTab);
                if (tabItem is not null && !ReferenceEquals(TerminalTabsView.SelectedItem, tabItem))
                {
                    TerminalTabsView.SelectedItem = tabItem;
                }
            });
        }

        private string GetTerminalSharedStatusPrimaryText()
        {
            return _selectedTerminalTab is null || string.IsNullOrWhiteSpace(_selectedTerminalTab.WorkingDirectory)
                ? "等待指定工作目錄"
                : _selectedTerminalTab.WorkingDirectory;
        }

        private string GetTerminalSharedStatusDetailText()
        {
            return _selectedTerminalTab is null
                ? "尚未建立分頁"
                : $"{_selectedTerminalTab.Title} · {_selectedTerminalTab.StatusText}";
        }

        private static string ResolveTerminalShellPath(TerminalShellKind shellKind)
        {
            string[] candidates = shellKind switch
            {
                TerminalShellKind.GitBash => new[]
                {
                    @"C:\Program Files\Git\bin\bash.exe",
                    @"C:\Program Files\Git\usr\bin\bash.exe",
                    @"C:\Program Files (x86)\Git\bin\bash.exe",
                },
                TerminalShellKind.CommandPrompt => new[]
                {
                    Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                },
                _ => new[]
                {
                    @"C:\Program Files\PowerShell\7\pwsh.exe",
                    Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
                },
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return shellKind switch
            {
                TerminalShellKind.GitBash => "bash.exe",
                TerminalShellKind.CommandPrompt => "cmd.exe",
                _ => "powershell.exe",
            };
        }

        private static string BuildShellArguments(TerminalShellKind shellKind)
        {
            return shellKind switch
            {
                TerminalShellKind.GitBash => "--login -i",
                TerminalShellKind.CommandPrompt => "/Q",
                _ => "-NoLogo",
            };
        }

        private static string BuildWorkingDirectoryCommand(TerminalShellKind shellKind, string path)
        {
            return shellKind switch
            {
                TerminalShellKind.GitBash => $"cd '{path.Replace("\\", "/", StringComparison.Ordinal).Replace("'", "'\\''", StringComparison.Ordinal)}'",
                TerminalShellKind.CommandPrompt => $"cd /d \"{path}\"",
                _ => $"Set-Location -LiteralPath '{path.Replace("'", "''", StringComparison.Ordinal)}'",
            };
        }

        private static bool IsPathLikeCommand(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && !value.Contains(Path.DirectorySeparatorChar);
        }

        private TerminalShellKind GetDefaultTerminalShellKind()
        {
            return _shortcutSettings.DefaultTerminalShellKind;
        }

        private string GetPreferredTerminalWorkingDirectory()
        {
            return ResolveTerminalWorkingDirectory(
                _shortcutSettings.DefaultTerminalWorkingDirectoryMode,
                _shortcutSettings.DefaultTerminalCustomWorkingDirectory);
        }

        private string ResolveTerminalWorkingDirectory(ToolbarWorkingDirectoryMode workingDirectoryMode, string? customWorkingDirectory)
        {
            var candidate = workingDirectoryMode switch
            {
                ToolbarWorkingDirectoryMode.LeftPane => LeftPane.CurrentPath?.Trim() ?? string.Empty,
                ToolbarWorkingDirectoryMode.RightPane => RightPane.CurrentPath?.Trim() ?? string.Empty,
                ToolbarWorkingDirectoryMode.CustomPath => customWorkingDirectory?.Trim() ?? string.Empty,
                _ => _activePane.CurrentPath?.Trim() ?? string.Empty,
            };

            return !string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)
                ? candidate
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private string BuildTerminalTabTitle(TerminalTabSession session)
        {
            return $"{session.ShellDisplayName} {session.TabNumber}";
        }

        private TabViewItem BuildTerminalTabViewItem(TerminalTabSession session)
        {
            return new TabViewItem
            {
                Header = session.Title,
                Tag = session,
                IsClosable = true,
            };
        }

        private TabViewItem? FindTerminalTabViewItem(TerminalTabSession session)
        {
            return TerminalTabsView.TabItems
                .OfType<TabViewItem>()
                .FirstOrDefault(item => ReferenceEquals(item.Tag, session));
        }

        private void UpdateTerminalTabHeader(TerminalTabSession session)
        {
            var tabItem = FindTerminalTabViewItem(session);
            if (tabItem is not null)
            {
                tabItem.Header = session.Title;
            }
        }

        private void RequestTerminalRender()
        {
            if (_isTerminalRenderQueued)
            {
                return;
            }

            _isTerminalRenderQueued = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                _isTerminalRenderQueued = false;
                if (_selectedTerminalTab is null)
                {
                    TerminalOutputTextBlock.Blocks.Clear();
                    return;
                }

                RenderTerminalOutput(_selectedTerminalTab.OutputText);
            });
        }

        private void RenderTerminalOutput(string output)
        {
            var shouldAutoScrollToBottom = IsTerminalScrollNearBottom();
            var screen = BuildTerminalScreen(output);
            TerminalOutputTextBlock.Blocks.Clear();

            var paragraph = new Paragraph();
            AppendScreenRuns(paragraph, screen);

            if (paragraph.Inlines.Count == 0)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = string.Empty,
                    Foreground = new SolidColorBrush(ParseColor("#F2F2F2")),
                });
            }

            if (_selectedTerminalTab?.IsRunning == true && _isTerminalHostFocused && _isTerminalCursorVisible)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = "\u2588",
                    Foreground = new SolidColorBrush(ParseColor("#F2F2F2")),
                });
            }

            TerminalOutputTextBlock.Blocks.Add(paragraph);
            TerminalOutputScrollViewer.UpdateLayout();
            if (shouldAutoScrollToBottom)
            {
                TerminalOutputScrollViewer.ChangeView(null, TerminalOutputScrollViewer.ScrollableHeight, null, true);
            }
        }

        private bool IsTerminalScrollNearBottom()
        {
            const double bottomThreshold = 24;
            return TerminalOutputScrollViewer.ScrollableHeight - TerminalOutputScrollViewer.VerticalOffset <= bottomThreshold;
        }

        private static void AppendScreenRuns(Paragraph paragraph, List<List<TerminalCell>> screen)
        {
            if (screen.Count == 0)
            {
                return;
            }

            for (var rowIndex = 0; rowIndex < screen.Count; rowIndex++)
            {
                var line = TrimTrailingEmptyCells(screen[rowIndex]);
                if (line.Count == 0)
                {
                    if (rowIndex < screen.Count - 1)
                    {
                        paragraph.Inlines.Add(new Run
                        {
                            Text = Environment.NewLine,
                            Foreground = new SolidColorBrush(ParseColor("#F2F2F2")),
                        });
                    }

                    continue;
                }

                var buffer = new StringBuilder();
                var currentColor = line[0].ForegroundHex;

                foreach (var cell in line)
                {
                    if (!string.Equals(cell.ForegroundHex, currentColor, StringComparison.Ordinal))
                    {
                        AppendColoredRun(paragraph, buffer.ToString(), currentColor);
                        buffer.Clear();
                        currentColor = cell.ForegroundHex;
                    }

                    buffer.Append(cell.Character);
                }

                if (buffer.Length > 0)
                {
                    AppendColoredRun(paragraph, buffer.ToString(), currentColor);
                }

                if (rowIndex < screen.Count - 1)
                {
                    paragraph.Inlines.Add(new Run
                    {
                        Text = Environment.NewLine,
                        Foreground = new SolidColorBrush(ParseColor("#F2F2F2")),
                    });
                }
            }
        }

        private static void AppendColoredRun(Paragraph paragraph, string text, string foregroundHex)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            paragraph.Inlines.Add(new Run
            {
                Text = text.Replace("\0", string.Empty, StringComparison.Ordinal),
                Foreground = new SolidColorBrush(ParseColor(foregroundHex)),
            });
        }

        private static List<List<TerminalCell>> BuildTerminalScreen(string output)
        {
            var screen = new List<List<TerminalCell>> { new() };
            if (string.IsNullOrEmpty(output))
            {
                return screen;
            }

            var row = 0;
            var column = 0;
            var state = new TerminalColorState();

            for (var index = 0; index < output.Length; index++)
            {
                var character = output[index];
                if (character == '\x1b')
                {
                    if (TryConsumeOsc(output, ref index))
                    {
                        continue;
                    }

                    if (TryConsumeCsi(output, ref index, screen, ref row, ref column, state))
                    {
                        continue;
                    }

                    continue;
                }

                switch (character)
                {
                    case '\r':
                        column = 0;
                        break;
                    case '\n':
                        row++;
                        EnsureRow(screen, row);
                        column = 0;
                        break;
                    case '\b':
                    case '\u007f':
                        if (column > 0)
                        {
                            column--;
                            RemoveCharacter(screen[row], column);
                        }
                        break;
                    case '\a':
                    case '\0':
                        break;
                    default:
                        WriteCharacter(screen, row, ref column, character, state.ForegroundHex);
                        break;
                }
            }

            return screen;
        }

        private static bool TryConsumeOsc(string text, ref int index)
        {
            if (index + 1 >= text.Length || text[index + 1] != ']')
            {
                return false;
            }

            index += 2;
            while (index < text.Length)
            {
                if (text[index] == '\a')
                {
                    return true;
                }

                if (text[index] == '\x1b' && index + 1 < text.Length && text[index + 1] == '\\')
                {
                    index++;
                    return true;
                }

                index++;
            }

            index = text.Length - 1;
            return true;
        }

        private static bool TryConsumeCsi(
            string text,
            ref int index,
            List<List<TerminalCell>> screen,
            ref int row,
            ref int column,
            TerminalColorState state)
        {
            if (index + 1 >= text.Length || text[index + 1] != '[')
            {
                return false;
            }

            var scan = index + 2;
            while (scan < text.Length && (text[scan] < '@' || text[scan] > '~'))
            {
                scan++;
            }

            if (scan >= text.Length)
            {
                index = text.Length - 1;
                return true;
            }

            var finalChar = text[scan];
            var parameterText = text[(index + 2)..scan];
            var parameters = parameterText.Split(';', StringSplitOptions.None);
            index = scan;

            switch (finalChar)
            {
                case 'm':
                    ApplySgr(parameters, state);
                    return true;
                case 'K':
                    EnsureRow(screen, row);
                    ClearToLineEnd(screen[row], column);
                    return true;
                case 'J':
                    if (ParseCsiInt(parameters.ElementAtOrDefault(0), 0) == 2)
                    {
                        screen.Clear();
                        screen.Add(new List<TerminalCell>());
                        row = 0;
                        column = 0;
                    }
                    return true;
                case 'C':
                    column += Math.Max(1, ParseCsiInt(parameters.ElementAtOrDefault(0), 1));
                    return true;
                case 'D':
                    column = Math.Max(0, column - Math.Max(1, ParseCsiInt(parameters.ElementAtOrDefault(0), 1)));
                    return true;
                case 'G':
                    column = Math.Max(0, ParseCsiInt(parameters.ElementAtOrDefault(0), 1) - 1);
                    return true;
                case 'H':
                case 'f':
                    row = Math.Max(0, ParseCsiInt(parameters.ElementAtOrDefault(0), 1) - 1);
                    column = Math.Max(0, ParseCsiInt(parameters.ElementAtOrDefault(1), 1) - 1);
                    EnsureRow(screen, row);
                    return true;
                case 'A':
                    row = Math.Max(0, row - Math.Max(1, ParseCsiInt(parameters.ElementAtOrDefault(0), 1)));
                    EnsureRow(screen, row);
                    return true;
                case 'B':
                    row += Math.Max(1, ParseCsiInt(parameters.ElementAtOrDefault(0), 1));
                    EnsureRow(screen, row);
                    return true;
                default:
                    return true;
            }
        }

        private static int ParseCsiInt(string? parameterText, int fallback)
        {
            return int.TryParse(parameterText, out var value) ? value : fallback;
        }

        private static void ApplySgr(string[] parameters, TerminalColorState state)
        {
            if (parameters.Length == 0 || (parameters.Length == 1 && string.IsNullOrEmpty(parameters[0])))
            {
                state.Reset();
                return;
            }

            for (var i = 0; i < parameters.Length; i++)
            {
                var code = ParseCsiInt(parameters[i], 0);
                switch (code)
                {
                    case 0:
                    case 39:
                        state.Reset();
                        break;
                    case >= 30 and <= 37:
                        state.ForegroundHex = GetAnsiColor(code - 30, bright: false);
                        break;
                    case >= 90 and <= 97:
                        state.ForegroundHex = GetAnsiColor(code - 90, bright: true);
                        break;
                    case 38:
                        if (i + 2 < parameters.Length &&
                            ParseCsiInt(parameters[i + 1], -1) == 5)
                        {
                            state.ForegroundHex = GetExtendedAnsiColor(ParseCsiInt(parameters[i + 2], 7));
                            i += 2;
                        }
                        break;
                }
            }
        }

        private static void EnsureRow(List<List<TerminalCell>> screen, int row)
        {
            while (screen.Count <= row)
            {
                screen.Add(new List<TerminalCell>());
            }
        }

        private static void WriteCharacter(
            List<List<TerminalCell>> screen,
            int row,
            ref int column,
            char character,
            string foregroundHex)
        {
            EnsureRow(screen, row);
            var line = screen[row];
            while (line.Count < column)
            {
                line.Add(new TerminalCell(' ', TerminalColorState.DefaultForegroundHex));
            }

            if (column < line.Count)
            {
                line[column] = new TerminalCell(character, foregroundHex);
            }
            else
            {
                line.Add(new TerminalCell(character, foregroundHex));
            }

            column++;
        }

        private static void RemoveCharacter(List<TerminalCell> line, int column)
        {
            if (column < 0 || column >= line.Count)
            {
                return;
            }

            line.RemoveAt(column);
        }

        private static List<TerminalCell> TrimTrailingEmptyCells(List<TerminalCell> line)
        {
            var end = line.Count;
            while (end > 0 && line[end - 1].Character == '\0')
            {
                end--;
            }

            return end == line.Count ? line : line.Take(end).ToList();
        }

        private static void ClearToLineEnd(List<TerminalCell> line, int column)
        {
            if (column < 0)
            {
                column = 0;
            }

            if (column >= line.Count)
            {
                return;
            }

            line.RemoveRange(column, line.Count - column);
            while (line.Count > 0 && line[^1].Character == ' ')
            {
                line.RemoveAt(line.Count - 1);
            }
        }

        private static string GetAnsiColor(int index, bool bright)
        {
            var normal = new[]
            {
                "#0C0C0C",
                "#C50F1F",
                "#13A10E",
                "#C19C00",
                "#0037DA",
                "#881798",
                "#3A96DD",
                "#CCCCCC",
            };
            var brightPalette = new[]
            {
                "#767676",
                "#E74856",
                "#16C60C",
                "#F9F1A5",
                "#3B78FF",
                "#B4009E",
                "#61D6D6",
                "#F2F2F2",
            };

            var palette = bright ? brightPalette : normal;
            return palette[Math.Clamp(index, 0, palette.Length - 1)];
        }

        private static string GetExtendedAnsiColor(int index)
        {
            index = Math.Clamp(index, 0, 255);
            if (index < 16)
            {
                return index < 8
                    ? GetAnsiColor(index, bright: false)
                    : GetAnsiColor(index - 8, bright: true);
            }

            if (index is >= 16 and <= 231)
            {
                var value = index - 16;
                var r = value / 36;
                var g = (value / 6) % 6;
                var b = value % 6;
                return $"#{MapCubeColor(r):X2}{MapCubeColor(g):X2}{MapCubeColor(b):X2}";
            }

            var gray = 8 + ((index - 232) * 10);
            return $"#{gray:X2}{gray:X2}{gray:X2}";
        }

        private static int MapCubeColor(int value)
        {
            return value == 0 ? 0 : 55 + (value * 40);
        }

        private sealed class TerminalColorState
        {
            internal const string DefaultForegroundHex = "#F2F2F2";

            internal string ForegroundHex { get; set; } = DefaultForegroundHex;

            internal void Reset()
            {
                ForegroundHex = DefaultForegroundHex;
            }
        }

        private readonly record struct TerminalCell(char Character, string ForegroundHex);
    }
}
