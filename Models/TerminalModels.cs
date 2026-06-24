using System;
using System.Diagnostics;

namespace nuone_tools
{
    public enum TerminalShellKind
    {
        PowerShell,
        GitBash,
        CommandPrompt,
    }

    public sealed class TerminalTabSession : ObservableObject
    {
        private string _title = string.Empty;
        private string _workingDirectory = string.Empty;
        private string _shellPath = string.Empty;
        private string _statusText = "未啟動";
        private string _outputText = string.Empty;
        private TerminalShellKind _shellKind = TerminalShellKind.PowerShell;
        private bool _isRunning;
        private Guid _processToken = Guid.Empty;
        private short _viewportColumns = 120;
        private short _viewportRows = 32;

        public Guid Id { get; set; } = Guid.NewGuid();

        public int TabNumber { get; set; }

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public TerminalShellKind ShellKind
        {
            get => _shellKind;
            set
            {
                if (SetProperty(ref _shellKind, value))
                {
                    OnPropertyChanged(nameof(ShellDisplayName));
                }
            }
        }

        public string ShellDisplayName => ShellKind switch
        {
            TerminalShellKind.GitBash => "Git Bash",
            TerminalShellKind.CommandPrompt => "cmd",
            _ => "PowerShell",
        };

        public string WorkingDirectory
        {
            get => _workingDirectory;
            set => SetProperty(ref _workingDirectory, value);
        }

        public string ShellPath
        {
            get => _shellPath;
            set => SetProperty(ref _shellPath, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string OutputText
        {
            get => _outputText;
            set => SetProperty(ref _outputText, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        public Process? Process { get; set; }

        public bool IsStarting { get; set; }

        public Guid ProcessToken
        {
            get => _processToken;
            set => SetProperty(ref _processToken, value);
        }

        public ConPtyRuntimeContext? ConPtyContext { get; set; }

        public short ViewportColumns
        {
            get => _viewportColumns;
            set => SetProperty(ref _viewportColumns, value);
        }

        public short ViewportRows
        {
            get => _viewportRows;
            set => SetProperty(ref _viewportRows, value);
        }
    }
}
