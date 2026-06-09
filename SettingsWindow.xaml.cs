using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace nuone_tools
{
    public sealed partial class SettingsWindow : Window
    {
        private ShortcutCaptureTarget _captureTarget = ShortcutCaptureTarget.None;

        public SettingsWindow(ShortcutSettings settings)
        {
            InitializeComponent();

            EditableSettings = new ShortcutSettings
            {
                CopyToOtherPaneKey = settings.CopyToOtherPaneKey,
                MoveToOtherPaneKey = settings.MoveToOtherPaneKey,
                NavigateUpKey = settings.NavigateUpKey,
                DeleteKey = settings.DeleteKey,
            };

            AppWindow.Resize(new Windows.Graphics.SizeInt32(1520, 980));
            TrySetWindowIcon();
            RootLayout.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(RootLayout_KeyDown), true);
            SyncShortcutText();
        }

        public ShortcutSettings EditableSettings { get; }

        public event EventHandler<ShortcutSettings>? SettingsSaved;

        private void TrySetWindowIcon()
        {
            try
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
                if (File.Exists(iconPath))
                {
                    AppWindow.SetIcon(iconPath);
                }
            }
            catch
            {
            }
        }

        private void EditCopyShortcut_Click(object sender, RoutedEventArgs e)
        {
            _captureTarget = ShortcutCaptureTarget.CopyToOtherPane;
            CopyShortcutTextBox.Text = "請按任意鍵...";
            CaptureHintTextBlock.Text = "正在擷取「複製到另一個 Pane」快捷鍵...";
            _ = RootLayout.Focus(FocusState.Programmatic);
        }

        private void EditMoveShortcut_Click(object sender, RoutedEventArgs e)
        {
            _captureTarget = ShortcutCaptureTarget.MoveToOtherPane;
            MoveShortcutTextBox.Text = "請按任意鍵...";
            CaptureHintTextBlock.Text = "正在擷取「移動到另一個 Pane」快捷鍵...";
            _ = RootLayout.Focus(FocusState.Programmatic);
        }

        private void EditNavigateUpShortcut_Click(object sender, RoutedEventArgs e)
        {
            _captureTarget = ShortcutCaptureTarget.NavigateUp;
            NavigateUpShortcutTextBox.Text = "請按任意鍵...";
            CaptureHintTextBlock.Text = "正在擷取「上一層」快捷鍵...";
            _ = RootLayout.Focus(FocusState.Programmatic);
        }

        private void EditDeleteShortcut_Click(object sender, RoutedEventArgs e)
        {
            _captureTarget = ShortcutCaptureTarget.Delete;
            DeleteShortcutTextBox.Text = "請按任意鍵...";
            CaptureHintTextBlock.Text = "正在擷取「刪除」快捷鍵...";
            _ = RootLayout.Focus(FocusState.Programmatic);
        }

        private void RootLayout_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (_captureTarget == ShortcutCaptureTarget.None)
            {
                return;
            }

            e.Handled = true;

            if (e.Key is Windows.System.VirtualKey.Tab
                or Windows.System.VirtualKey.LeftShift
                or Windows.System.VirtualKey.RightShift
                or Windows.System.VirtualKey.LeftControl
                or Windows.System.VirtualKey.RightControl
                or Windows.System.VirtualKey.LeftMenu
                or Windows.System.VirtualKey.RightMenu)
            {
                return;
            }

            var key = NormalizeCapturedKey(e.Key);
            switch (_captureTarget)
            {
                case ShortcutCaptureTarget.CopyToOtherPane:
                    EditableSettings.CopyToOtherPaneKey = key;
                    break;
                case ShortcutCaptureTarget.MoveToOtherPane:
                    EditableSettings.MoveToOtherPaneKey = key;
                    break;
                case ShortcutCaptureTarget.NavigateUp:
                    EditableSettings.NavigateUpKey = key;
                    break;
                case ShortcutCaptureTarget.Delete:
                    EditableSettings.DeleteKey = key;
                    break;
            }

            _captureTarget = ShortcutCaptureTarget.None;
            SyncShortcutText();
            CaptureHintTextBlock.Text = "已擷取按鍵，按儲存即可套用。";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (EditableSettings.CopyToOtherPaneKey == EditableSettings.MoveToOtherPaneKey
                || EditableSettings.CopyToOtherPaneKey == EditableSettings.NavigateUpKey
                || EditableSettings.CopyToOtherPaneKey == EditableSettings.DeleteKey
                || EditableSettings.MoveToOtherPaneKey == EditableSettings.NavigateUpKey
                || EditableSettings.MoveToOtherPaneKey == EditableSettings.DeleteKey
                || EditableSettings.NavigateUpKey == EditableSettings.DeleteKey)
            {
                CaptureHintTextBlock.Text = "四個動作不能使用相同的快捷鍵。";
                return;
            }

            SettingsSaved?.Invoke(this, new ShortcutSettings
            {
                CopyToOtherPaneKey = EditableSettings.CopyToOtherPaneKey,
                MoveToOtherPaneKey = EditableSettings.MoveToOtherPaneKey,
                NavigateUpKey = EditableSettings.NavigateUpKey,
                DeleteKey = EditableSettings.DeleteKey,
            });

            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SyncShortcutText()
        {
            CopyShortcutTextBox.Text = FormatShortcutKey(EditableSettings.CopyToOtherPaneKey);
            MoveShortcutTextBox.Text = FormatShortcutKey(EditableSettings.MoveToOtherPaneKey);
            NavigateUpShortcutTextBox.Text = FormatShortcutKey(EditableSettings.NavigateUpKey);
            DeleteShortcutTextBox.Text = FormatShortcutKey(EditableSettings.DeleteKey);
        }

        private static Windows.System.VirtualKey NormalizeCapturedKey(Windows.System.VirtualKey key)
        {
            return key == Windows.System.VirtualKey.Cancel
                ? Windows.System.VirtualKey.Pause
                : key;
        }

        private static string FormatShortcutKey(Windows.System.VirtualKey key)
        {
            return key switch
            {
                Windows.System.VirtualKey.Number0 => "0",
                Windows.System.VirtualKey.Number1 => "1",
                Windows.System.VirtualKey.Number2 => "2",
                Windows.System.VirtualKey.Number3 => "3",
                Windows.System.VirtualKey.Number4 => "4",
                Windows.System.VirtualKey.Number5 => "5",
                Windows.System.VirtualKey.Number6 => "6",
                Windows.System.VirtualKey.Number7 => "7",
                Windows.System.VirtualKey.Number8 => "8",
                Windows.System.VirtualKey.Number9 => "9",
                Windows.System.VirtualKey.Pause => "Pause / Break",
                Windows.System.VirtualKey.Control => "Ctrl",
                Windows.System.VirtualKey.LeftControl => "Left Ctrl",
                Windows.System.VirtualKey.RightControl => "Right Ctrl",
                Windows.System.VirtualKey.Shift => "Shift",
                Windows.System.VirtualKey.LeftShift => "Left Shift",
                Windows.System.VirtualKey.RightShift => "Right Shift",
                Windows.System.VirtualKey.Menu => "Alt",
                Windows.System.VirtualKey.LeftMenu => "Left Alt",
                Windows.System.VirtualKey.RightMenu => "Right Alt",
                _ => key.ToString(),
            };
        }
    }
}
