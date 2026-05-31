using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Diagnostics;
using PhotoHelper.Data;
using PhotoHelper.Logging;
using PhotoHelper.Services;
using PhotoHelper.Utils;

namespace PhotoHelper;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string AppDataFolderName = ".photohelper";
    private const string LogsFolderName = "Logs";
    private const string DataFolderName = "Data";
    private const string SettingsFileName = "settings.json";
    private const string GlobalSettingsFileName = "global.settings.json";
    private const int MaxRecentTargets = 6;
    private string? _settingsFilePath;
    private string? _activeTargetRoot;
    private AppSettings _settings = new();
    private readonly string _globalSettingsFilePath;
    private GlobalSettings _globalSettings = new();
    private bool _isUpdatingLibraryCombo;
    private DatabaseRebuildService? _databaseRebuildService;
    private DatabaseService? _databaseService;
    private ArchiveService? _archiveService;
    private Logger? _logger;
    private ScanService? _scanService;

    public MainWindow()
    {
        InitializeComponent();

        var globalRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoHelper");
        _globalSettingsFilePath = Path.Combine(globalRoot, GlobalSettingsFileName);
        _globalSettings = GlobalSettings.Load(_globalSettingsFilePath);

        Loaded += OnLoaded;
        BrowseTargetButton.Click += OnBrowseTargetClicked;
        BrowseSourceButton.Click += OnBrowseSourceClicked;
        StartImportButton.Click += OnStartImportClicked;
        RebuildButton.Click += OnRebuildClicked;
        LibraryComboBox.SelectionChanged += OnLibrarySelectionChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LogInfo("应用已启动，日志系统就绪。");
        RefreshLibraryComboBox();

        if (!string.IsNullOrWhiteSpace(_globalSettings.LastTargetPath) &&
            Directory.Exists(_globalSettings.LastTargetPath))
        {
            TargetRootTextBox.Text = _globalSettings.LastTargetPath;
            ConfigureForTarget(_globalSettings.LastTargetPath);
            SaveSettings();
        }
    }

    private void OnBrowseTargetClicked(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择照片存储根目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            TargetRootTextBox.Text = dialog.SelectedPath;
            ConfigureForTarget(dialog.SelectedPath);
            SaveSettings();
        }
    }

    private void OnBrowseSourceClicked(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择相机内存卡或源文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SourcePathTextBox.Text = dialog.SelectedPath;
            SaveSettings();
        }
    }

    private async void OnStartImportClicked(object sender, RoutedEventArgs e)
    {
        var sourcePath = SourcePathTextBox.Text;
        var targetRoot = TargetRootTextBox.Text;

        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetRoot))
        {
            LogWarning("请先选择源路径和目标路径。");
            return;
        }

        EnsureConfiguredForTarget(targetRoot);

        SaveSettings();
        ToggleControls(false);

        try
        {
            var progress = new Progress<(int current, int total)>(value =>
            {
                ImportProgressBar.Maximum = value.total == 0 ? 1 : value.total;
                ImportProgressBar.Value = value.current;
            });

            await Task.Run(() => RunImportAsync(sourcePath, targetRoot, progress));
            LogInfo("导入完成。");
        }
        catch (Exception ex)
        {
            LogError($"导入失败: {ex.Message}");
        }
        finally
        {
            ToggleControls(true);
        }
    }

    private async void OnRebuildClicked(object sender, RoutedEventArgs e)
    {
        var targetRoot = TargetRootTextBox.Text;
        if (string.IsNullOrWhiteSpace(targetRoot))
        {
            LogWarning("请先选择目标路径。");
            return;
        }

        EnsureConfiguredForTarget(targetRoot);

        var confirm = System.Windows.MessageBox.Show(
            "将扫描目标根目录并重新登记已有照片，是否继续？",
            "重建历史数据库",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        ToggleControls(false);

        try
        {
            var rebuildService = _databaseRebuildService ?? throw new InvalidOperationException("应用尚未配置目标路径。");
            await Task.Run(() => rebuildService.Rebuild(targetRoot));
            LogInfo("历史数据库重建完成。");
        }
        catch (Exception ex)
        {
            LogError($"历史数据库重建失败: {ex.Message}");
        }
        finally
        {
            ToggleControls(true);
        }
    }

    private async Task RunImportAsync(string sourcePath, string targetRoot, IProgress<(int current, int total)> progress)
    {
        if (_scanService == null || _archiveService == null || _databaseService == null)
        {
            throw new InvalidOperationException("应用尚未配置目标路径。");
        }

        var stopwatch = Stopwatch.StartNew();
        var newPhotos = _scanService.ScanForNewPhotos(sourcePath);
        var total = newPhotos.Count;
        var processed = 0;
        var copied = 0;
        var skipped = 0;
        var failed = 0;
        var maxParallel = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
        using var semaphore = new SemaphoreSlim(maxParallel, maxParallel);
        var tasks = new List<Task>(newPhotos.Count);

        foreach (var photo in newPhotos)
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    var result = _archiveService.ArchivePhoto(photo, targetRoot);
                    if (result.WasCopied)
                    {
                        Interlocked.Increment(ref copied);
                    }
                    else
                    {
                        Interlocked.Increment(ref skipped);
                    }

                    _databaseService.Insert(result.Record);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failed);
                    LogWarning($"导入失败: {photo.FileName}. {ex.Message}");
                }
                finally
                {
                    var current = Interlocked.Increment(ref processed);
                    progress.Report((current, total));
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        stopwatch.Stop();
        LogInfo($"导入统计：共 {total} 张，成功复制 {copied} 张，跳过 {skipped} 张，失败 {failed} 张，耗时 {stopwatch.ElapsedMilliseconds} ms。");
    }

    private void SaveSettings()
    {
        if (string.IsNullOrWhiteSpace(_settingsFilePath))
        {
            return;
        }

        _settings.SourcePath = SourcePathTextBox.Text;
        _settings.TargetPath = TargetRootTextBox.Text;
        _settings.Save(_settingsFilePath);
    }

    private void SaveGlobalSettings(string targetRoot)
    {
        if (string.IsNullOrWhiteSpace(targetRoot))
        {
            return;
        }

        _globalSettings.LastTargetPath = targetRoot;
        _globalSettings.RecentTargets.RemoveAll(path =>
            string.Equals(path, targetRoot, StringComparison.OrdinalIgnoreCase));
        _globalSettings.RecentTargets.Insert(0, targetRoot);

        if (_globalSettings.RecentTargets.Count > MaxRecentTargets)
        {
            _globalSettings.RecentTargets = _globalSettings.RecentTargets
                .Take(MaxRecentTargets)
                .ToList();
        }

        _globalSettings.Save(_globalSettingsFilePath);
        RefreshLibraryComboBox();
    }

    private void RefreshLibraryComboBox()
    {
        _isUpdatingLibraryCombo = true;
        LibraryComboBox.ItemsSource = null;
        LibraryComboBox.ItemsSource = _globalSettings.RecentTargets.ToList();

        if (!string.IsNullOrWhiteSpace(_activeTargetRoot))
        {
            LibraryComboBox.SelectedItem = _globalSettings.RecentTargets
                .FirstOrDefault(path => string.Equals(path, _activeTargetRoot, StringComparison.OrdinalIgnoreCase));
        }
        _isUpdatingLibraryCombo = false;
    }

    private void OnLibrarySelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isUpdatingLibraryCombo)
        {
            return;
        }

        if (LibraryComboBox.SelectedItem is string selectedPath)
        {
            TargetRootTextBox.Text = selectedPath;
            ConfigureForTarget(selectedPath);
            SaveSettings();
        }
    }

    private void ConfigureForTarget(string targetRoot)
    {
        if (string.IsNullOrWhiteSpace(targetRoot))
        {
            return;
        }

        if (string.Equals(_activeTargetRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeTargetRoot = targetRoot;

        var appDataRoot = Path.Combine(targetRoot, AppDataFolderName);
        var logDirectory = Path.Combine(appDataRoot, LogsFolderName);
        var dataDirectory = Path.Combine(appDataRoot, DataFolderName);

        if (_logger != null)
        {
            _logger.LogEmitted -= OnLogEmitted;
        }
        _logger = new Logger(logDirectory);
        _logger.LogEmitted += OnLogEmitted;

        _databaseService = new DatabaseService(dataDirectory);
        _databaseRebuildService = new DatabaseRebuildService(_databaseService, _logger);
        _archiveService = new ArchiveService(_logger);
        _scanService = new ScanService(_databaseService, _logger);

        _settingsFilePath = Path.Combine(appDataRoot, SettingsFileName);
        _settings = AppSettings.Load(_settingsFilePath);

    SaveGlobalSettings(targetRoot);

        if (string.IsNullOrWhiteSpace(SourcePathTextBox.Text) && !string.IsNullOrWhiteSpace(_settings.SourcePath))
        {
            SourcePathTextBox.Text = _settings.SourcePath;
        }

        try
        {
            _databaseService.Initialize();
            LogInfo("SQLite 数据库已初始化。");
            var stats = _databaseService.EnsureSchemaUpToDate(msg => _logger?.Warning(msg));
            if (stats.Total > 0)
            {
                LogInfo($"旧库升级统计：共 {stats.Total} 条，升级 {stats.Upgraded} 条，缺失文件 {stats.SkippedMissingFile} 条，冲突跳过 {stats.SkippedConflict} 条。");
            }
        }
        catch (Exception ex)
        {
            LogError($"数据库初始化失败: {ex.Message}");
        }
    }

    private void EnsureConfiguredForTarget(string targetRoot)
    {
        if (_databaseService == null || !string.Equals(_activeTargetRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            ConfigureForTarget(targetRoot);
        }
    }

    private void ToggleControls(bool isEnabled)
    {
        BrowseTargetButton.IsEnabled = isEnabled;
        BrowseSourceButton.IsEnabled = isEnabled;
        StartImportButton.IsEnabled = isEnabled;
        RebuildButton.IsEnabled = isEnabled;
        SourcePathTextBox.IsEnabled = isEnabled;
        TargetRootTextBox.IsEnabled = isEnabled;
        LibraryComboBox.IsEnabled = isEnabled;
    }

    private void OnLogEmitted(object? sender, LogMessage logMessage)
    {
        Dispatcher.Invoke(() =>
        {
            LogTextBox.AppendText(logMessage + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        });
    }

    private void LogInfo(string message)
    {
        if (_logger == null)
        {
            AppendUiLog(LogLevel.Info, message);
            return;
        }

        _logger.Info(message);
    }

    private void LogWarning(string message)
    {
        if (_logger == null)
        {
            AppendUiLog(LogLevel.Warning, message);
            return;
        }

        _logger.Warning(message);
    }

    private void LogError(string message)
    {
        if (_logger == null)
        {
            AppendUiLog(LogLevel.Error, message);
            return;
        }

        _logger.Error(message);
    }

    private void AppendUiLog(LogLevel level, string message)
    {
        var logMessage = new LogMessage(DateTime.Now, level, message);
        Dispatcher.Invoke(() =>
        {
            LogTextBox.AppendText(logMessage + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        });
    }
}