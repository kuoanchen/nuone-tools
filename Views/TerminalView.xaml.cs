using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Documents;

namespace nuone_tools.Views
{
    public sealed partial class TerminalView : UserControl
    {
        private MainWindow? _owner;

        public TerminalView()
        {
            InitializeComponent();
            TerminalOutputScrollViewer.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(TerminalOutput_KeyDown), true);
            TerminalOutputTextBlock.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(TerminalOutput_KeyDown), true);
            TerminalOutputScrollViewer.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(TerminalOutput_PointerPressed), true);
            TerminalOutputTextBlock.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(TerminalOutput_PointerPressed), true);
            TerminalOutputScrollViewer.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(TerminalOutput_PointerReleased), true);
            TerminalOutputTextBlock.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(TerminalOutput_PointerReleased), true);
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

        internal TextBox TerminalCommandTextBoxElement => TerminalCommandTextBox;
        internal Border TerminalHostElement => TerminalHost;
        internal ComboBox TerminalShellComboBoxElement => TerminalShellComboBox;
        internal RichTextBlock TerminalOutputTextBlockElement => TerminalOutputTextBlock;
        internal ScrollViewer TerminalOutputScrollViewerElement => TerminalOutputScrollViewer;
        internal TextBlock TerminalShellTextBlockElement => TerminalShellTextBlock;
        internal TextBlock TerminalStatusTextBlockElement => TerminalStatusTextBlock;
        internal TabView TerminalTabsViewElement => TerminalTabsView;
        internal TextBlock TerminalWorkingDirectoryTextBlockElement => TerminalWorkingDirectoryTextBlock;

        private void ClearTerminalOutput_Click(object sender, RoutedEventArgs e)
        {
            Owner?.ClearTerminalOutput_Click(sender, e);
        }

        private void RestartTerminal_Click(object sender, RoutedEventArgs e)
        {
            Owner?.RestartTerminal_Click(sender, e);
        }

        private void SendTerminalCommand_Click(object sender, RoutedEventArgs e)
        {
            Owner?.SendTerminalCommand_Click(sender, e);
        }

        private void SyncTerminalWorkingDirectory_Click(object sender, RoutedEventArgs e)
        {
            Owner?.SyncTerminalWorkingDirectory_Click(sender, e);
        }

        private void TerminalCommandTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            Owner?.TerminalCommandTextBox_KeyDown(sender, e);
        }

        private void TerminalHost_CharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
        {
            Owner?.TerminalHost_CharacterReceived(sender, args);
        }

        private void TerminalHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            Owner?.TerminalHost_KeyDown(sender, e);
        }

        private void TerminalHost_GotFocus(object sender, RoutedEventArgs e)
        {
            Owner?.TerminalHost_GotFocus(sender, e);
        }

        private void TerminalHost_LostFocus(object sender, RoutedEventArgs e)
        {
            Owner?.TerminalHost_LostFocus(sender, e);
        }

        private void TerminalHost_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            Owner?.TerminalHost_PointerPressed(sender, e);
        }

        private void TerminalHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Owner?.TerminalHost_SizeChanged(sender, e);
        }

        private void TerminalOutput_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            Owner?.TerminalHost_KeyDown(sender, e);
        }

        private void TerminalOutput_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            Owner?.TerminalHost_PointerPressed(sender, e);
        }

        private void TerminalOutput_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            Owner?.TerminalHost_PointerPressed(sender, e);
        }

        private void TerminalShellComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Owner?.TerminalShellComboBox_SelectionChanged(sender, e);
        }

        private void TerminalTabsView_AddTabButtonClick(TabView sender, object args)
        {
            Owner?.TerminalTabsView_AddTabButtonClick(sender, args);
        }

        private void TerminalTabsView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Owner?.TerminalTabsView_SelectionChanged(sender, e);
        }

        private void TerminalTabsView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            Owner?.TerminalTabsView_TabCloseRequested(sender, args);
        }
    }
}
