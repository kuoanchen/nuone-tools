using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace nuone_tools.Views
{
    public sealed partial class SettingsView : UserControl
    {
        private MainWindow? _owner;

        public SettingsView()
        {
            InitializeComponent();
        }

        public MainWindow? Owner
        {
            get => _owner;
            set
            {
                _owner = value;
                DataContext = value;
            }
        }

        internal TextBox AccountApiUrlTextBoxElement => AccountApiUrlTextBox;
        internal Border AccountConnectionStatusCardElement => AccountConnectionStatusCard;
        internal TextBlock AccountConnectionStatusTextBlockElement => AccountConnectionStatusTextBlock;
        internal TextBox AccountEmailTextBoxElement => AccountEmailTextBox;
        internal TextBlock AccountLastLoginTextBlockElement => AccountLastLoginTextBlock;
        internal StackPanel AccountLoginFieldsPanelElement => AccountLoginFieldsPanel;
        internal TextBox AccountPayloadJsonTextBoxElement => AccountPayloadJsonTextBox;
        internal PasswordBox AccountPasswordBoxElement => AccountPasswordBox;
        internal TextBlock AccountServiceAccountsTextBlockElement => AccountServiceAccountsTextBlock;
        internal Border AccountSettingsContentElement => AccountSettingsContent;
        internal Border AccountSettingsNavBorderElement => AccountSettingsNavBorder;
        internal TextBlock AccountSettingsNavTextElement => AccountSettingsNavText;
        internal TextBlock AccountTokenTextBlockElement => AccountTokenTextBlock;
        internal TextBlock AccountUserTextBlockElement => AccountUserTextBlock;
        internal Border AppearanceSettingsContentElement => AppearanceSettingsContent;
        internal Border AppearanceSettingsNavBorderElement => AppearanceSettingsNavBorder;
        internal TextBlock AppearanceSettingsNavTextElement => AppearanceSettingsNavText;
        internal TextBlock CaptureHintTextBlockElement => CaptureHintTextBlock;
        internal TextBox CopyShortcutTextBoxElement => CopyShortcutTextBox;
        internal TextBox CreateFolderShortcutTextBoxElement => CreateFolderShortcutTextBox;
        internal TextBox DeleteShortcutTextBoxElement => DeleteShortcutTextBox;
        internal TextBox DefaultTerminalCustomWorkingDirectoryTextBoxElement => DefaultTerminalCustomWorkingDirectoryTextBox;
        internal ComboBox DefaultTerminalShellComboBoxElement => DefaultTerminalShellComboBox;
        internal ComboBox DefaultTerminalWorkingDirectoryModeComboBoxElement => DefaultTerminalWorkingDirectoryModeComboBox;
        internal TextBox FileBunkerApiKeyTextBoxElement => FileBunkerApiKeyTextBox;
        internal TextBox FileBunkerClientIdTextBoxElement => FileBunkerClientIdTextBox;
        internal TextBox FileBunkerDaysToExpirationTextBoxElement => FileBunkerDaysToExpirationTextBox;
        internal TextBox FileBunkerDaysToPurgeTextBoxElement => FileBunkerDaysToPurgeTextBox;
        internal TextBox FileBunkerInputEndpointTextBoxElement => FileBunkerInputEndpointTextBox;
        internal TextBox FileBunkerKeyLengthTextBoxElement => FileBunkerKeyLengthTextBox;
        internal TextBox FileBunkerOutputEndpointTextBoxElement => FileBunkerOutputEndpointTextBox;
        internal TextBlock CurrentAppVersionTextBlockElement => CurrentAppVersionTextBlock;
        internal Button CheckForUpdatesButtonElement => CheckForUpdatesButton;
        internal StackPanel AppUpdateActionButtonsPanelElement => AppUpdateActionButtonsPanel;
        internal Button OpenUpdateDownloadButtonElement => OpenUpdateDownloadButton;
        internal Button CopyUpdateDownloadUrlButtonElement => CopyUpdateDownloadUrlButton;
        internal TextBox LogDirectoryPathTextBoxElement => LogDirectoryPathTextBox;
        internal TextBlock AppUpdateManifestUrlTextBlockElement => UpdateManifestUrlTextBlock;
        internal TextBlock AppUpdateReleaseNotesTextBlockElement => AppUpdateReleaseNotesTextBlock;
        internal TextBlock AppUpdateStatusTextBlockElement => AppUpdateStatusTextBlock;
        internal TextBlock LatestAppVersionTextBlockElement => LatestAppVersionTextBlock;
        internal TextBlock LastAppUpdateCheckTextBlockElement => LastAppUpdateCheckTextBlock;
        internal TextBlock LastLocalBackupTextBlockElement => LastLocalBackupTextBlock;
        internal StackPanel GeneralSettingsContentElement => GeneralSettingsContent;
        internal Border GeneralSettingsNavBorderElement => GeneralSettingsNavBorder;
        internal TextBlock GeneralSettingsNavTextElement => GeneralSettingsNavText;
        internal Button LoginAccountButtonElement => LoginAccountButton;
        internal TextBox MoveShortcutTextBoxElement => MoveShortcutTextBox;
        internal TextBox NavigateUpShortcutTextBoxElement => NavigateUpShortcutTextBox;
        internal TextBlock SettingsPageDescriptionElement => SettingsPageDescription;
        internal TextBlock SettingsPageTitleElement => SettingsPageTitle;
        internal Border ShortcutSettingsContentElement => ShortcutSettingsContent;
        internal Border ShortcutSettingsNavBorderElement => ShortcutSettingsNavBorder;
        internal TextBlock ShortcutSettingsNavTextElement => ShortcutSettingsNavText;
        internal ToggleSwitch ShowHiddenSystemItemsToggleElement => ShowHiddenSystemItemsToggle;
        internal ToggleSwitch ShowSelectedFileSizeToggleElement => ShowSelectedFileSizeToggle;
        internal ToggleSwitch ShowSelectedFolderSizeToggleElement => ShowSelectedFolderSizeToggle;
        internal ComboBox ThemeModeComboBoxElement => ThemeModeComboBox;
        internal Border ToolbarSettingsContentElement => ToolbarSettingsContent;
        internal Border ToolbarSettingsNavBorderElement => ToolbarSettingsNavBorder;
        internal TextBlock ToolbarSettingsNavTextElement => ToolbarSettingsNavText;

        private void AccountApiUrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AccountApiUrlTextBox_TextChanged(sender, e);
        }

        private void AccountEmailTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AccountEmailTextBox_TextChanged(sender, e);
        }

        private void AddToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            Owner?.AddToolbarCommand_Click(sender, e);
        }

        private void DeleteToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            Owner?.DeleteToolbarCommand_Click(sender, e);
        }

        private void DefaultTerminalCustomWorkingDirectoryTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.DefaultTerminalCustomWorkingDirectoryTextBox_TextChanged(sender, e);
        }

        private void DefaultTerminalShellComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Owner?.DefaultTerminalShellComboBox_SelectionChanged(sender, e);
        }

        private void DefaultTerminalWorkingDirectoryModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Owner?.DefaultTerminalWorkingDirectoryModeComboBox_SelectionChanged(sender, e);
        }

        private void EditCopyShortcut_Click(object sender, RoutedEventArgs e)
        {
            Owner?.EditCopyShortcut_Click(sender, e);
        }

        private void EditCreateFolderShortcut_Click(object sender, RoutedEventArgs e)
        {
            Owner?.EditCreateFolderShortcut_Click(sender, e);
        }

        private void EditDeleteShortcut_Click(object sender, RoutedEventArgs e)
        {
            Owner?.EditDeleteShortcut_Click(sender, e);
        }

        private void EditMoveShortcut_Click(object sender, RoutedEventArgs e)
        {
            Owner?.EditMoveShortcut_Click(sender, e);
        }

        private void EditNavigateUpShortcut_Click(object sender, RoutedEventArgs e)
        {
            Owner?.EditNavigateUpShortcut_Click(sender, e);
        }

        private void EditToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            Owner?.EditToolbarCommand_Click(sender, e);
        }

        private void FileBunkerApiKeyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.FileBunkerApiKeyTextBox_TextChanged(sender, e);
        }

        private void FileBunkerClientIdTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.FileBunkerClientIdTextBox_TextChanged(sender, e);
        }

        private void FileBunkerDaysToExpirationTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.FileBunkerDaysToExpirationTextBox_TextChanged(sender, e);
        }

        private void FileBunkerDaysToPurgeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.FileBunkerDaysToPurgeTextBox_TextChanged(sender, e);
        }

        private void FileBunkerInputEndpointTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.FileBunkerInputEndpointTextBox_TextChanged(sender, e);
        }

        private void FileBunkerKeyLengthTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.FileBunkerKeyLengthTextBox_TextChanged(sender, e);
        }

        private void FileBunkerOutputEndpointTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.FileBunkerOutputEndpointTextBox_TextChanged(sender, e);
        }

        private void LoginAccountButton_Click(object sender, RoutedEventArgs e)
        {
            Owner?.LoginAccountButton_Click(sender, e);
        }

        private void LogDirectoryPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.LogDirectoryPathTextBox_TextChanged(sender, e);
        }

        private void OpenLogDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            Owner?.OpenLogDirectoryButton_Click(sender, e);
        }

        private void BackupLocalSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            Owner?.BackupLocalSettingsButton_Click(sender, e);
        }

        private void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            Owner?.CheckForUpdatesButton_Click(sender, e);
        }

        private void OpenUpdateDownloadButton_Click(object sender, RoutedEventArgs e)
        {
            Owner?.OpenUpdateDownloadButton_Click(sender, e);
        }

        private void CopyUpdateDownloadUrlButton_Click(object sender, RoutedEventArgs e)
        {
            Owner?.CopyUpdateDownloadUrlButton_Click(sender, e);
        }

        private void ShowAccountSettings_Click(object sender, RoutedEventArgs e)
        {
            Owner?.ShowAccountSettings_Click(sender, e);
        }

        private void ShowAppearanceSettings_Click(object sender, RoutedEventArgs e)
        {
            Owner?.ShowAppearanceSettings_Click(sender, e);
        }

        private void ShowGeneralSettings_Click(object sender, RoutedEventArgs e)
        {
            Owner?.ShowGeneralSettings_Click(sender, e);
        }

        private void ShowHiddenSystemItemsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            Owner?.ShowHiddenSystemItemsToggle_Toggled(sender, e);
        }

        private void ShowSelectedFileSizeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            Owner?.ShowSelectedFileSizeToggle_Toggled(sender, e);
        }

        private void ShowSelectedFolderSizeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            Owner?.ShowSelectedFolderSizeToggle_Toggled(sender, e);
        }

        private void ShowShortcutSettings_Click(object sender, RoutedEventArgs e)
        {
            Owner?.ShowShortcutSettings_Click(sender, e);
        }

        private void ShowToolbarSettings_Click(object sender, RoutedEventArgs e)
        {
            Owner?.ShowToolbarSettings_Click(sender, e);
        }

        private void ThemeModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Owner?.ThemeModeComboBox_SelectionChanged(sender, e);
        }

        private void UseDefaultLogDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            Owner?.UseDefaultLogDirectoryButton_Click(sender, e);
        }

        private void ToolbarCommandsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            Owner?.ToolbarCommandsListView_DragItemsCompleted(sender, args);
        }

        private void ToolbarIconPresenter_Loaded(object sender, RoutedEventArgs e)
        {
            Owner?.ToolbarIconPresenter_Loaded(sender, e);
        }

        private void ToolbarIconPresenter_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            Owner?.ToolbarIconPresenter_DataContextChanged(sender, args);
        }

        private void ToolbarIconSummary_Loaded(object sender, RoutedEventArgs e)
        {
            Owner?.ToolbarIconSummary_Loaded(sender, e);
        }
    }
}

