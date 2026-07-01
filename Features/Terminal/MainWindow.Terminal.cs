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
        private const double TerminalCellWidth = 8.4;
        private const double TerminalCellHeight = 20.0;
        private const double TerminalHorizontalPadding = 36.0;
        private const double TerminalVerticalPadding = 36.0;
        private readonly DispatcherQueueTimer? _terminalCursorTimer;
        private bool _isTerminalCursorVisible = true;
        private bool _isTerminalHostFocused;
        private bool _isTerminalRenderQueued;

        internal void SendTerminalCommand_Click(object sender, RoutedEventArgs e)
        {
            RunFireAndForget(SendTerminalCommandAsync(), "terminal send command click");
        }

        internal void TerminalCommandTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            RunFireAndForget(SendTerminalCommandAsync(), "terminal send command key");
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
            RunFireAndForget(SendTerminalRawInputAsync(character.ToString()), "terminal character input");
        }

        internal void TerminalHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (IsControlKeyDown() && e.Key == Windows.System.VirtualKey.V)
            {
                e.Handled = true;
                _isTerminalCursorVisible = true;
                RunFireAndForget(PasteClipboardToTerminalAsync(), "terminal paste clipboard");
                return;
            }

            if (!TryBuildTerminalKeyInput(e.Key, out var input))
            {
                return;
            }

            e.Handled = true;
            _isTerminalCursorVisible = true;
            RequestTerminalRender();
            RunFireAndForget(SendTerminalRawInputAsync(input), "terminal key input");
        }

        internal void TerminalHost_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;
            TryEnqueueUi(
                () => _ = TerminalHost.Focus(FocusState.Programmatic),
                "terminal host pointer focus");
        }

        internal void TerminalHost_GotFocus(object sender, RoutedEventArgs e)
        {
            _isTerminalHostFocused = true;
            _isTerminalCursorVisible = true;
            _terminalCursorTimer?.Start();
            UpdateTerminalUi();
        }

        internal void TerminalHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ResizeSelectedTerminalToViewport();
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

        internal void OpenBuiltInTerminalTab(TerminalShellKind shellKind, string workingDirectory, string? customTitle = null)
        {
            EnsureTerminalTabExists();
            AddTerminalTab(shellKind, true, workingDirectory, customTitle);
            SwitchToAppSection(AppSection.Terminal);
            FocusTerminalHost();
        }

        internal void OpenBuiltInTerminalTabAndRunCommand(TerminalShellKind shellKind, string workingDirectory, string command, string? customTitle = null)
        {
            OpenBuiltInTerminalTab(shellKind, workingDirectory, customTitle);

            var session = _selectedTerminalTab;
            if (session is null)
            {
                return;
            }

            EnsureTerminalProcessStarted(session);
            RunFireAndForget(SendTerminalInternalCommandAsync(session, command), "terminal internal command");
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
            RunFireAndForget(SendTerminalRawInputAsync("\u0003"), "terminal interrupt");
            session.StatusText = $"已送出中斷訊號 · {session.WorkingDirectory}";
            UpdateTerminalUi();
            return true;
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

        private void EnsureTerminalTabExists(string? workingDirectoryOverride = null)
        {
            if (TerminalTabs.Count > 0)
            {
                var session = _selectedTerminalTab;
                if (session is null)
                {
                    session = TerminalTabs[0];
                    SelectTerminalTab(session);
                }

                if (!string.IsNullOrWhiteSpace(workingDirectoryOverride))
                {
                    ApplyTerminalWorkingDirectoryOverride(session, workingDirectoryOverride);
                }

                return;
            }

            AddTerminalTab(GetDefaultTerminalShellKind(), true, workingDirectoryOverride);
        }

        private void ApplyTerminalWorkingDirectoryOverride(TerminalTabSession session, string workingDirectoryOverride)
        {
            var candidate = workingDirectoryOverride.Trim();
            if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
            {
                return;
            }

            session.WorkingDirectory = candidate;
            if (session.ConPtyContext is not null && session.Process is not null && !session.Process.HasExited)
            {
                RunFireAndForget(
                    SendTerminalInternalCommandAsync(session, BuildWorkingDirectoryCommand(session.ShellKind, candidate)),
                    "terminal external launch working directory command");
            }

            session.StatusText = $"工作目錄已切換到 {session.WorkingDirectory}";
            session.Title = BuildTerminalTabTitle(session);
            UpdateTerminalTabHeader(session);
            UpdateTerminalUi();
        }

        private void AddTerminalTab(TerminalShellKind shellKind, bool shouldSelect, string? workingDirectoryOverride = null, string? customTitle = null)
        {
            var workingDirectory = string.IsNullOrWhiteSpace(workingDirectoryOverride)
                ? GetPreferredTerminalWorkingDirectory()
                : workingDirectoryOverride.Trim();
            var session = new TerminalTabSession
            {
                TabNumber = _nextTerminalTabNumber++,
                CustomTitle = customTitle?.Trim() ?? string.Empty,
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
            TryEnqueueUi(
                () => _ = TerminalHost.Focus(FocusState.Programmatic),
                "terminal host focus");
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
            try
            {
                if (!_isTerminalHostFocused || _selectedTerminalTab is null || !_selectedTerminalTab.IsRunning)
                {
                    _isTerminalCursorVisible = false;
                    return;
                }

                _isTerminalCursorVisible = !_isTerminalCursorVisible;
                RequestTerminalRender();
            }
            catch (Exception ex)
            {
                LogBoundaryException(ex, "terminal cursor timer tick");
            }
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
                RunFireAndForget(
                    SendTerminalInternalCommandAsync(session, BuildWorkingDirectoryCommand(session.ShellKind, candidate)),
                    "terminal sync working directory command");
            }

            session.StatusText = $"工作目錄已同步到 {session.WorkingDirectory}";
            session.Title = BuildTerminalTabTitle(session);
            UpdateTerminalTabHeader(session);
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
                ApplyViewportSizeToSession(session);
                session.Process = Process.GetProcessById(launch.ProcessId);
                session.Process.EnableRaisingEvents = true;
                var processToken = session.ProcessToken;
                session.Process.Exited += (_, _) => TerminalProcess_Exited(session, processToken);
                RunFireAndForget(
                    Task.Run(() => PumpTerminalOutputAsync(session, launch.Context, processToken)),
                    "terminal output pump");

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
                        RunFireAndForget(WriteToTerminalAsync(context, "exit\r\n"), "terminal cleanup exit command");
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
            TryEnqueueUi(
                () => HandleTerminalProcessExited(session, processToken),
                "terminal process exited");
        }

        private void HandleTerminalProcessExited(TerminalTabSession session, Guid processToken)
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

            TryEnqueueUi(
                () =>
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
                },
                "terminal append output");
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
            TryEnqueueUi(
                () =>
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
                },
                "terminal update ui");
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

        private static string BuildShellArguments(TerminalShellKind shellKind, string? initialCommand = null)
        {
            if (!string.IsNullOrWhiteSpace(initialCommand))
            {
                return shellKind switch
                {
                    TerminalShellKind.GitBash => $"--login -i -c \"{initialCommand.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}; exec bash -i\"",
                    TerminalShellKind.CommandPrompt => $"/K {initialCommand}",
                    _ => $"-NoLogo -NoExit -ExecutionPolicy Bypass -Command \"{initialCommand.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
                };
            }

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
            if (!string.IsNullOrWhiteSpace(session.CustomTitle))
            {
                return session.CustomTitle;
            }

            var directoryName = GetTerminalDirectoryTitle(session.WorkingDirectory);
            return string.IsNullOrWhiteSpace(directoryName)
                ? $"{session.ShellDisplayName} {session.TabNumber}"
                : directoryName;
        }

        private static string GetTerminalDirectoryTitle(string? workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                return string.Empty;
            }

            var trimmed = workingDirectory.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return string.Empty;
            }

            var name = Path.GetFileName(trimmed);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return trimmed;
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
            TryEnqueueUi(
                () =>
                {
                    _isTerminalRenderQueued = false;
                    if (_selectedTerminalTab is null)
                    {
                        TerminalOutputTextBlock.Blocks.Clear();
                        return;
                    }

                    RenderTerminalOutput(_selectedTerminalTab);
                },
                "terminal render output");
        }

        private void RenderTerminalOutput(TerminalTabSession session)
        {
            var shouldAutoScrollToBottom = IsTerminalScrollNearBottom();
            var terminalScreen = BuildTerminalScreen(session.OutputText, session.ViewportColumns);
            TerminalOutputTextBlock.Blocks.Clear();

            var paragraph = new Paragraph();
            var showCursor = _selectedTerminalTab?.IsRunning == true && _isTerminalHostFocused && _isTerminalCursorVisible;
            AppendScreenRuns(paragraph, terminalScreen, showCursor);

            if (paragraph.Inlines.Count == 0)
            {
                paragraph.Inlines.Add(new Run
                {
                    Text = string.Empty,
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

        private static void AppendScreenRuns(Paragraph paragraph, TerminalScreenState terminalScreen, bool showCursor)
        {
            var screen = terminalScreen.Rows;
            if (screen.Count == 0)
            {
                return;
            }

            for (var rowIndex = 0; rowIndex < screen.Count; rowIndex++)
            {
                var line = MaterializeLineForRender(
                    screen[rowIndex],
                    rowIndex == terminalScreen.CursorRow ? terminalScreen.CursorColumn : -1,
                    showCursor && rowIndex == terminalScreen.CursorRow);

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

        private static TerminalScreenState BuildTerminalScreen(string output, short viewportColumns)
        {
            var columns = viewportColumns > 0 ? viewportColumns : TerminalDefaultColumns;
            var screen = new List<List<TerminalCell>> { new() };
            if (string.IsNullOrEmpty(output))
            {
                return new TerminalScreenState(screen, 0, 0);
            }

            var row = 0;
            var column = 0;
            var state = new TerminalColorState();
            var savedRow = 0;
            var savedColumn = 0;

            for (var index = 0; index < output.Length; index++)
            {
                var character = output[index];
                if (character == '\x1b')
                {
                    if (TryConsumeOsc(output, ref index))
                    {
                        continue;
                    }

                    if (TryConsumeCsi(output, ref index, screen, ref row, ref column, ref savedRow, ref savedColumn, state))
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
                    case '\t':
                        var nextTabStop = ((column / 4) + 1) * 4;
                        while (column < nextTabStop)
                        {
                            WriteCharacter(screen, ref row, ref column, ' ', state.ForegroundHex, columns);
                        }
                        break;
                    case '\a':
                    case '\0':
                        break;
                    default:
                        WriteCharacter(screen, ref row, ref column, character, state.ForegroundHex, columns);
                        break;
                }
            }

            return new TerminalScreenState(screen, row, column);
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
            ref int savedRow,
            ref int savedColumn,
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
                    ClearLine(screen[row], column, ParseCsiInt(parameters.ElementAtOrDefault(0), 0));
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
                case 's':
                    savedRow = row;
                    savedColumn = column;
                    return true;
                case 'u':
                    row = Math.Max(0, savedRow);
                    column = Math.Max(0, savedColumn);
                    EnsureRow(screen, row);
                    return true;
                case 'C':
                    column += Math.Max(1, ParseCsiInt(parameters.ElementAtOrDefault(0), 1));
                    return true;
                case 'D':
                    column = Math.Max(0, column - Math.Max(1, ParseCsiInt(parameters.ElementAtOrDefault(0), 1)));
                    return true;
                case 'P':
                    EnsureRow(screen, row);
                    DeleteCharacters(screen[row], column, Math.Max(1, ParseCsiInt(parameters.ElementAtOrDefault(0), 1)));
                    return true;
                case '@':
                    EnsureRow(screen, row);
                    InsertBlankCharacters(screen[row], column, Math.Max(1, ParseCsiInt(parameters.ElementAtOrDefault(0), 1)));
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
            ref int row,
            ref int column,
            char character,
            string foregroundHex,
            short viewportColumns)
        {
            var columns = viewportColumns > 0 ? viewportColumns : TerminalDefaultColumns;
            if (column >= columns)
            {
                row++;
                column = 0;
            }

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

        private static List<TerminalCell> MaterializeLineForRender(List<TerminalCell> line, int cursorColumn, bool showCursor)
        {
            var renderedLine = TrimTrailingEmptyCells(line).ToList();
            if (!showCursor || cursorColumn < 0)
            {
                return renderedLine;
            }

            while (renderedLine.Count < cursorColumn)
            {
                renderedLine.Add(new TerminalCell(' ', TerminalColorState.DefaultForegroundHex));
            }

            if (renderedLine.Count == cursorColumn)
            {
                renderedLine.Add(new TerminalCell('\u2588', TerminalColorState.DefaultForegroundHex));
            }
            else
            {
                renderedLine[cursorColumn] = new TerminalCell('\u2588', renderedLine[cursorColumn].ForegroundHex);
            }

            return renderedLine;
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

        private void ResizeSelectedTerminalToViewport()
        {
            var session = _selectedTerminalTab;
            if (session is null)
            {
                return;
            }

            ApplyViewportSizeToSession(session);
        }

        private void ApplyViewportSizeToSession(TerminalTabSession session)
        {
            var columns = CalculateViewportColumns();
            var rows = CalculateViewportRows();

            var changed = session.ViewportColumns != columns || session.ViewportRows != rows;
            session.ViewportColumns = columns;
            session.ViewportRows = rows;

            if (session.ConPtyContext is not null)
            {
                try
                {
                    session.ConPtyContext.Resize(columns, rows);
                }
                catch
                {
                }
            }

            if (changed && ReferenceEquals(session, _selectedTerminalTab))
            {
                RequestTerminalRender();
            }
        }

        private short CalculateViewportColumns()
        {
            var width = TerminalOutputScrollViewer?.ActualWidth ?? 0;
            var usableWidth = Math.Max(0, width - TerminalHorizontalPadding);
            var columns = (short)Math.Max(40, Math.Floor(usableWidth / TerminalCellWidth));
            return columns > 0 ? columns : TerminalDefaultColumns;
        }

        private short CalculateViewportRows()
        {
            var height = TerminalOutputScrollViewer?.ActualHeight ?? 0;
            var usableHeight = Math.Max(0, height - TerminalVerticalPadding);
            var rows = (short)Math.Max(12, Math.Floor(usableHeight / TerminalCellHeight));
            return rows > 0 ? rows : TerminalDefaultRows;
        }

        private static void ClearLine(List<TerminalCell> line, int column, int mode)
        {
            switch (mode)
            {
                case 1:
                    if (line.Count == 0)
                    {
                        return;
                    }

                    var clearTo = Math.Min(Math.Max(column, 0), line.Count - 1);
                    for (var i = 0; i <= clearTo; i++)
                    {
                        line[i] = new TerminalCell(' ', TerminalColorState.DefaultForegroundHex);
                    }
                    break;
                case 2:
                    line.Clear();
                    break;
                default:
                    ClearToLineEnd(line, column);
                    break;
            }
        }

        private static void DeleteCharacters(List<TerminalCell> line, int column, int count)
        {
            if (column < 0 || column >= line.Count || count <= 0)
            {
                return;
            }

            var deleteCount = Math.Min(count, line.Count - column);
            line.RemoveRange(column, deleteCount);
        }

        private static void InsertBlankCharacters(List<TerminalCell> line, int column, int count)
        {
            if (count <= 0)
            {
                return;
            }

            column = Math.Max(0, column);
            while (line.Count < column)
            {
                line.Add(new TerminalCell(' ', TerminalColorState.DefaultForegroundHex));
            }

            for (var i = 0; i < count; i++)
            {
                line.Insert(Math.Min(column, line.Count), new TerminalCell(' ', TerminalColorState.DefaultForegroundHex));
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

        private readonly record struct TerminalScreenState(
            List<List<TerminalCell>> Rows,
            int CursorRow,
            int CursorColumn);

        private readonly record struct TerminalCell(char Character, string ForegroundHex);
    }
}
