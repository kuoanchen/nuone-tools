using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace nuone_tools.Views
{
    public sealed partial class AutomationView : UserControl
    {
        private MainWindow? _owner;

        public AutomationView()
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

        internal TextBox DestinationPathTextBox => AutomationDestinationPathTextBox;
        internal TextBox AutoExtractExtensionFilterTextBoxElement => AutoExtractExtensionFilterTextBox;
        internal TextBox AutoExtractExtractorPathTextBoxElement => AutoExtractExtractorPathTextBox;
        internal TextBox AutoExtractNameTextBoxElement => AutoExtractNameTextBox;
        internal TextBox AutoExtractWatchPathTextBoxElement => AutoExtractWatchPathTextBox;
        internal TextBox IntervalEntryTextBox => AutomationIntervalTextBox;
        internal TextBox IntervalTextBox => AutomationIntervalTextBox;
        internal ComboBox JobTypeComboBox => AutomationJobTypeComboBox;
        internal ComboBox ModeComboBox => AutomationModeComboBox;
        internal ComboBox ScheduleTypeComboBox => AutomationScheduleTypeComboBox;
        internal CheckBox MongoUseArchiveCheckBox => AutomationMongoUseArchiveCheckBox;
        internal TextBox MongoConnectionStringTextBox => AutomationMongoConnectionStringTextBox;
        internal TextBox MongoDatabaseNameTextBox => AutomationMongoDatabaseNameTextBox;
        internal TextBox MongoRetentionCountTextBox => AutomationMongoRetentionCountTextBox;
        internal TextBox MongoToolPathTextBox => AutomationMongoToolPathTextBox;
        internal CheckBox MongoUseGzipCheckBox => AutomationMongoUseGzipCheckBox;
        internal TextBox NameTextBox => AutomationNameTextBox;
        internal CheckBox RunMissedOnStartupCheckBox => AutomationRunMissedOnStartupCheckBox;
        internal TextBox ScheduleTimeTextBox => AutomationScheduleTimeTextBox;
        internal TextBox SourcePathTextBox => AutomationSourcePathTextBox;
        internal StackPanel FileSettingsPanel => AutomationFileSettingsPanel;
        internal StackPanel MongoSettingsPanel => AutomationMongoSettingsPanel;
        internal TextBlock HintTextBlock => AutomationHintTextBlock;
        internal StackPanel IntervalSchedulePanel => AutomationIntervalSchedulePanel;
        internal StackPanel TimeSchedulePanel => AutomationTimeSchedulePanel;
        internal StackPanel WeeklyDaysPanel => AutomationWeeklyDaysPanel;
        internal CheckBox WeeklyMondayCheckBox => AutomationWeeklyMondayCheckBox;
        internal CheckBox WeeklyTuesdayCheckBox => AutomationWeeklyTuesdayCheckBox;
        internal CheckBox WeeklyWednesdayCheckBox => AutomationWeeklyWednesdayCheckBox;
        internal CheckBox WeeklyThursdayCheckBox => AutomationWeeklyThursdayCheckBox;
        internal CheckBox WeeklyFridayCheckBox => AutomationWeeklyFridayCheckBox;
        internal CheckBox WeeklySaturdayCheckBox => AutomationWeeklySaturdayCheckBox;
        internal CheckBox WeeklySundayCheckBox => AutomationWeeklySundayCheckBox;

        private void AddAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            Owner?.AddAutomationJob_Click(sender, e);
        }

        private void AddAutomationDialog_Click(object sender, RoutedEventArgs e)
        {
            Owner?.AddAutomationDialog_Click(sender, e);
        }

        private void AddAutoExtractPassword_Click(object sender, RoutedEventArgs e)
        {
            Owner?.AddAutoExtractPassword_Click(sender, e);
        }

        private void AddAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            Owner?.AddAutoExtractProfile_Click(sender, e);
        }

        private void AutoExtractEnabledToggle_Toggled(object sender, RoutedEventArgs e)
        {
            Owner?.AutoExtractEnabledToggle_Toggled(sender, e);
        }

        private void AutoExtractExtractorPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutoExtractExtractorPathTextBox_TextChanged(sender, e);
        }

        private void AutoExtractExtensionFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutoExtractExtensionFilterTextBox_TextChanged(sender, e);
        }

        private void AutoExtractNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutoExtractNameTextBox_TextChanged(sender, e);
        }

        private void AutoExtractPasswordItemTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutoExtractPasswordItemTextBox_TextChanged(sender, e);
        }

        private void AutoExtractWatchPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutoExtractWatchPathTextBox_TextChanged(sender, e);
        }

        private void AutomationDestinationPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutomationDestinationPathTextBox_TextChanged(sender, e);
        }

        private void AutomationEnabledToggle_Toggled(object sender, RoutedEventArgs e)
        {
            Owner?.AutomationEnabledToggle_Toggled(sender, e);
        }

        private void AutomationIntervalTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutomationIntervalTextBox_TextChanged(sender, e);
        }

        private void AutomationJobTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Owner?.AutomationJobTypeComboBox_SelectionChanged(sender, e);
        }

        private void AutomationJobTypeItemComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            Owner?.AutomationJobTypeItemComboBox_Loaded(sender, e);
        }

        private void AutomationJobTypeItemComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Owner?.AutomationJobTypeItemComboBox_SelectionChanged(sender, e);
        }

        private void AutomationScheduleTypeComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            Owner?.AutomationScheduleTypeComboBox_Loaded(sender, e);
        }

        private void AutomationScheduleTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Owner?.AutomationScheduleTypeComboBox_SelectionChanged(sender, e);
        }

        private void AutomationModeComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            Owner?.AutomationModeComboBox_Loaded(sender, e);
        }

        private void AutomationModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Owner?.AutomationModeComboBox_SelectionChanged(sender, e);
        }

        private void AutomationNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutomationNameTextBox_TextChanged(sender, e);
        }

        private void AutomationMongoConnectionStringTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutomationMongoConnectionStringTextBox_TextChanged(sender, e);
        }

        private void AutomationMongoDatabaseNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutomationMongoDatabaseNameTextBox_TextChanged(sender, e);
        }

        private void AutomationMongoRetentionCountTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutomationMongoRetentionCountTextBox_TextChanged(sender, e);
        }

        private void AutomationMongoToolPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutomationMongoToolPathTextBox_TextChanged(sender, e);
        }

        private void AutomationMongoUseArchiveCheckBox_Click(object sender, RoutedEventArgs e)
        {
            Owner?.AutomationMongoUseArchiveCheckBox_Click(sender, e);
        }

        private void AutomationMongoUseGzipCheckBox_Click(object sender, RoutedEventArgs e)
        {
            Owner?.AutomationMongoUseGzipCheckBox_Click(sender, e);
        }

        private void AutomationRunMissedOnStartupCheckBox_Click(object sender, RoutedEventArgs e)
        {
            Owner?.AutomationRunMissedOnStartupCheckBox_Click(sender, e);
        }

        private void AutomationScheduleTimeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutomationScheduleTimeTextBox_TextChanged(sender, e);
        }

        private void AutomationSourcePathTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            Owner?.AutomationSourcePathTextBox_TextChanged(sender, e);
        }

        private void AutomationWeeklyDayCheckBox_Click(object sender, RoutedEventArgs e)
        {
            Owner?.AutomationWeeklyDayCheckBox_Click(sender, e);
        }

        private void DeleteAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            Owner?.DeleteAutomationJob_Click(sender, e);
        }

        private void DeleteAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            Owner?.DeleteAutoExtractProfile_Click(sender, e);
        }

        private void EditAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            Owner?.EditAutomationJob_Click(sender, e);
        }

        private void EditAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            Owner?.EditAutoExtractProfile_Click(sender, e);
        }

        private void RemoveAutoExtractPassword_Click(object sender, RoutedEventArgs e)
        {
            Owner?.RemoveAutoExtractPassword_Click(sender, e);
        }

        private void RunAutoExtractNow_Click(object sender, RoutedEventArgs e)
        {
            Owner?.RunAutoExtractNow_Click(sender, e);
        }

        private void RunAutomationNow_Click(object sender, RoutedEventArgs e)
        {
            Owner?.RunAutomationNow_Click(sender, e);
        }

        private void StartAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            Owner?.StartAutomationJob_Click(sender, e);
        }

        private void StartAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            Owner?.StartAutoExtractProfile_Click(sender, e);
        }

        private void StopAutomationJob_Click(object sender, RoutedEventArgs e)
        {
            Owner?.StopAutomationJob_Click(sender, e);
        }

        private void StopAutoExtractProfile_Click(object sender, RoutedEventArgs e)
        {
            Owner?.StopAutoExtractProfile_Click(sender, e);
        }
    }
}

