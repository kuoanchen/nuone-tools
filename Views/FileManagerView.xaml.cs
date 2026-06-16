using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace nuone_tools.Views
{
    public sealed partial class FileManagerView : UserControl
    {
        private MainWindow? _owner;

        public FileManagerView()
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

        internal StackPanel AddGroupEditorElement => AddGroupEditor;
        internal Button DriveSectionMenuButtonElement => DriveSectionMenuButton;
        internal Border LeftPaneBorderElement => LeftPaneBorder;
        internal ListView LeftPaneListViewElement => LeftPaneListView;
        internal TextBox LeftPathTextBoxElement => LeftPathTextBox;
        internal TextBox NewGroupNameTextBoxElement => NewGroupNameTextBox;
        internal Border RightPaneBorderElement => RightPaneBorder;
        internal ListView RightPaneListViewElement => RightPaneListView;
        internal TextBox RightPathTextBoxElement => RightPathTextBox;
        internal Border TopCommandBarBorderElement => TopCommandBarBorder;

        private void AddCurrentPathToGroup_Click(object sender, RoutedEventArgs e)
        {
            Owner?.AddCurrentPathToGroup_Click(sender, e);
        }

        private void AddToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            Owner?.AddToolbarCommand_Click(sender, e);
        }

        private void CancelAddGroup_Click(object sender, RoutedEventArgs e)
        {
            Owner?.CancelAddGroup_Click(sender, e);
        }

        private void ClearPaneFilter_Click(object sender, RoutedEventArgs e)
        {
            Owner?.ClearPaneFilter_Click(sender, e);
        }

        private void ConfirmAddGroup_Click(object sender, RoutedEventArgs e)
        {
            Owner?.ConfirmAddGroup_Click(sender, e);
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            Owner?.CopyPath_Click(sender, e);
        }

        private void CopyToLeftPane_Click(object sender, RoutedEventArgs e)
        {
            Owner?.CopyToLeftPane_Click(sender, e);
        }

        private void CopyToRightPane_Click(object sender, RoutedEventArgs e)
        {
            Owner?.CopyToRightPane_Click(sender, e);
        }

        private void CreateFolderLeft_Click(object sender, RoutedEventArgs e)
        {
            Owner?.CreateFolderLeft_Click(sender, e);
        }

        private void CreateFolderRight_Click(object sender, RoutedEventArgs e)
        {
            Owner?.CreateFolderRight_Click(sender, e);
        }

        private void CustomGroupsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            Owner?.CustomGroupsListView_DragItemsCompleted(sender, args);
        }

        private void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            Owner?.DeleteGroup_Click(sender, e);
        }

        private void DeletePath_Click(object sender, RoutedEventArgs e)
        {
            Owner?.DeletePath_Click(sender, e);
        }

        private void DeleteToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            Owner?.DeleteToolbarCommand_Click(sender, e);
        }

        private void DriveRestoreFlyout_Opening(object sender, object e)
        {
            Owner?.DriveRestoreFlyout_Opening(sender, e);
        }

        private void DriveShortcut_Click(object sender, RoutedEventArgs e)
        {
            Owner?.DriveShortcut_Click(sender, e);
        }

        private void DriveShortcut_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            Owner?.DriveShortcut_RightTapped(sender, e);
        }

        private async void FileEntryIconPresenter_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: FileEntry entry })
            {
                await entry.EnsureShellIconAsync();
            }
        }

        private void EditToolbarCommand_Click(object sender, RoutedEventArgs e)
        {
            Owner?.EditToolbarCommand_Click(sender, e);
        }

        private void GroupedPath_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Owner?.GroupedPath_Tapped(sender, e);
        }

        private void GroupedPathItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            Owner?.GroupedPathItem_PointerEntered(sender, e);
        }

        private void GroupedPathItem_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            Owner?.GroupedPathItem_PointerExited(sender, e);
        }

        private void GroupedPathOpen_Click(object sender, RoutedEventArgs e)
        {
            Owner?.GroupedPathOpen_Click(sender, e);
        }

        private void GroupedPathsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            Owner?.GroupedPathsListView_DragItemsCompleted(sender, args);
        }

        private void LeftPaneContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Owner?.LeftPaneContainer_Tapped(sender, e);
        }

        private void LeftPaneEntry_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            Owner?.LeftPaneEntry_RightTapped(sender, e);
        }

        private void LeftPaneEntry_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Owner?.LeftPaneEntry_Tapped(sender, e);
        }

        private void LeftPaneList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            Owner?.LeftPaneList_DoubleTapped(sender, e);
        }

        private void LeftPaneList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            Owner?.LeftPaneList_DragItemsStarting(sender, e);
        }

        private void LeftPaneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Owner?.LeftPaneList_SelectionChanged(sender, e);
        }

        private void LeftPaneList_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Owner?.LeftPaneList_Tapped(sender, e);
        }

        private void LeftPathBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            Owner?.LeftPathBox_KeyDown(sender, e);
        }

        private void LeftPathTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            Owner?.LeftPathTextBox_GotFocus(sender, e);
        }

        private void MoveToLeftPane_Click(object sender, RoutedEventArgs e)
        {
            Owner?.MoveToLeftPane_Click(sender, e);
        }

        private void MoveToRightPane_Click(object sender, RoutedEventArgs e)
        {
            Owner?.MoveToRightPane_Click(sender, e);
        }

        private void NewGroupNameTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            Owner?.NewGroupNameTextBox_KeyDown(sender, e);
        }

        private void OpenInLeftPane_Click(object sender, RoutedEventArgs e)
        {
            Owner?.OpenInLeftPane_Click(sender, e);
        }

        private void OpenInRightPane_Click(object sender, RoutedEventArgs e)
        {
            Owner?.OpenInRightPane_Click(sender, e);
        }

        private void OpenPath_Click(object sender, RoutedEventArgs e)
        {
            Owner?.OpenPath_Click(sender, e);
        }

        private void PaneListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            Owner?.PaneListView_ContainerContentChanging(sender, args);
        }

        private void RefreshAll_Click(object sender, RoutedEventArgs e)
        {
            Owner?.RefreshAll_Click(sender, e);
        }

        private void RemoveGroupedPath_Click(object sender, RoutedEventArgs e)
        {
            Owner?.RemoveGroupedPath_Click(sender, e);
        }

        private void RenameGroup_Click(object sender, RoutedEventArgs e)
        {
            Owner?.RenameGroup_Click(sender, e);
        }

        private void RenameGroupedPathAlias_Click(object sender, RoutedEventArgs e)
        {
            Owner?.RenameGroupedPathAlias_Click(sender, e);
        }

        private void RenamePath_Click(object sender, RoutedEventArgs e)
        {
            Owner?.RenamePath_Click(sender, e);
        }

        private void RightPaneContainer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Owner?.RightPaneContainer_Tapped(sender, e);
        }

        private void RightPaneEntry_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            Owner?.RightPaneEntry_RightTapped(sender, e);
        }

        private void RightPaneEntry_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Owner?.RightPaneEntry_Tapped(sender, e);
        }

        private void RightPaneList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            Owner?.RightPaneList_DoubleTapped(sender, e);
        }

        private void RightPaneList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {
            Owner?.RightPaneList_DragItemsStarting(sender, e);
        }

        private void RightPaneList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Owner?.RightPaneList_SelectionChanged(sender, e);
        }

        private void RightPaneList_Tapped(object sender, TappedRoutedEventArgs e)
        {
            Owner?.RightPaneList_Tapped(sender, e);
        }

        private void RightPathBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            Owner?.RightPathBox_KeyDown(sender, e);
        }

        private void RightPathTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            Owner?.RightPathTextBox_GotFocus(sender, e);
        }

        private void ShowAddGroupEditor_Click(object sender, RoutedEventArgs e)
        {
            Owner?.ShowAddGroupEditor_Click(sender, e);
        }

        private void ToolbarItem_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            Owner?.ToolbarItem_PointerEntered(sender, e);
        }

        private void ToolbarItem_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            Owner?.ToolbarItem_PointerExited(sender, e);
        }

        private void ToolbarCommandsListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
        {
            Owner?.ToolbarCommandsListView_DragItemsCompleted(sender, args);
        }

        private void ToolbarIconPresenter_Loaded(object sender, RoutedEventArgs e)
        {
            Owner?.ToolbarIconPresenter_Loaded(sender, e);
        }

        private void TopToolbarListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            Owner?.TopToolbarListView_ItemClick(sender, e);
        }
    }
}

