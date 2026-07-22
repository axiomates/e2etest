using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using E2ETest.Core.Model;
using E2ETest.Core.Reporting;
using Microsoft.Win32;

namespace E2ETest.ReportViewer;

public partial class MainWindow : Window
{
    private readonly string[] _filters = ["需要关注", "全部用例", "已通过"];
    private IReadOnlyList<ReportRoundView> _rounds = [];
    private ReportRoundView? _currentRound;
    private TestCaseComparisonResult? _currentCase;
    private ShotComparisonResult? _currentShot;
    private PixelRegion? _currentRegion;
    private string? _currentEvidencePath;
    private bool _changingSelection;

    public MainWindow()
    {
        InitializeComponent();
        FilterCombo.ItemsSource = _filters;
        DetailRoot.Visibility = Visibility.Collapsed;
        AiPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        Loaded += (_, _) => LoadInitialReports();
    }

    private void LoadInitialReports()
    {
        string? argument = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal));
        string? initial = argument is null ? FindDefaultReportsDirectory() : Path.GetFullPath(argument);
        if (initial is not null) LoadReports(initial, showErrors: false);
    }

    private void OpenReports_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 reports 根目录或一个 round 目录",
            Multiselect = false,
            InitialDirectory = Directory.Exists(OpenedPathText.Text) ? OpenedPathText.Text : Environment.CurrentDirectory,
        };
        if (dialog.ShowDialog(this) == true) LoadReports(dialog.FolderName, showErrors: true);
    }

    private void ReloadReports_Click(object sender, RoutedEventArgs e)
    {
        string path = OpenedPathText.Text;
        if (!string.IsNullOrWhiteSpace(path)) LoadReports(path, showErrors: true);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var assembly = Assembly.GetExecutingAssembly();
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+', 2)[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "未知";
        MessageBox.Show(this,
            $"E2ETest 报告分析\n版本 {version}\n\n用于只读查看 replay/compare 生成的结构化测试报告。",
            "关于 E2ETest 报告分析",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            ReloadReports_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            OpenReports_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void LoadReports(string path, bool showErrors)
    {
        try
        {
            _rounds = ReportCatalogLoader.Load(path);
            OpenedPathText.Text = Path.GetFullPath(path);
            RoundCombo.ItemsSource = _rounds;
            if (_rounds.Count == 0)
            {
                ClearRound();
                if (showErrors) MessageBox.Show(this, "该目录中没有找到可读取的对比报告。", "没有报告", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            RoundCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            ClearRound();
            if (showErrors) MessageBox.Show(this, ex.Message, "无法打开报告", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RoundCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoundCombo.SelectedItem is not ReportRoundView round) return;
        _currentRound = round;
        AttentionCountText.Text = round.AttentionCount.ToString();
        FailedCountText.Text = round.FailedCount.ToString();
        PassedCountText.Text = round.PassedCount.ToString();
        TotalCountText.Text = round.TotalCount.ToString();
        _changingSelection = true;
        FilterCombo.SelectedIndex = round.AttentionCount > 0 ? 0 : 1;
        _changingSelection = false;
        RefreshCases();
        if (round.Warnings.Count > 0)
            OpenedPathText.ToolTip = string.Join(Environment.NewLine, round.Warnings);
        else OpenedPathText.ToolTip = null;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshCases();
    private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_changingSelection) RefreshCases();
    }

    private void RefreshCases()
    {
        if (_currentRound is null) return;
        string search = SearchBox.Text.Trim();
        string filter = FilterCombo.SelectedItem?.ToString() ?? "全部用例";
        var cases = _currentRound.TestCases.Where(testCase =>
                (search.Length == 0 || testCase.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)) &&
                (filter == "全部用例" || filter == "需要关注" && ReportCatalogLoader.NeedsAttention(testCase) || filter == "已通过" && testCase.FinalVerdict == "passed"))
            .ToList();
        CaseList.ItemsSource = cases;
        CaseList.SelectedIndex = cases.Count > 0 ? 0 : -1;
        if (cases.Count == 0) ClearCase();
    }

    private void CaseList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CaseList.SelectedItem is not TestCaseComparisonResult testCase) { ClearCase(); return; }
        _currentCase = testCase;
        DetailRoot.Visibility = Visibility.Visible;
        CaseNameText.Text = testCase.Name;
        CaseVerdictText.Text = VerdictText(testCase.FinalVerdict);
        CaseVerdictBadge.Background = VerdictBrush(testCase.FinalVerdict);
        LocalFinalText.Text = $"本地 {VerdictText(testCase.Status)}  →  最终 {VerdictText(testCase.FinalVerdict)}";
        CaseGuidanceText.Text = Guidance(testCase);
        CaseBanner.Background = BannerBrush(testCase.FinalVerdict);

        var shots = testCase.Shots.OrderBy(shot => shot.Ordinal).ToList();
        ShotList.ItemsSource = shots;
        var preferred = shots.FirstOrDefault(NeedsAttention) ?? shots.FirstOrDefault(shot => (shot.Pixel?.ChangedPixels ?? 0) > 0) ?? shots.FirstOrDefault();
        ShotList.SelectedItem = preferred;
        if (preferred is null) ClearShot();
    }

    private void ShotList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ShotList.SelectedItem is not ShotComparisonResult shot) { ClearShot(); return; }
        _currentShot = shot;
        ShotTitleText.Text = $"步骤 {shot.Ordinal} · {VerdictText(shot.FinalVerdict)}";
        ShotMetaText.Text = $"{RoleText(shot.Role)}  ·  shotIndex={shot.ShotIndex}  ·  {(shot.AtMs ?? 0) / 1000d:0.0}s  ·  差异像素 {shot.Pixel?.ChangedPixels ?? 0:N0}";
        bool exactMatch = shot.Pixel?.ExactPixelMatch == true;
        ExactMatchBadge.Visibility = exactMatch ? Visibility.Visible : Visibility.Collapsed;
        EvidenceModeCombo.Visibility = exactMatch ? Visibility.Collapsed : Visibility.Visible;
        RegionPanel.Visibility = exactMatch ? Visibility.Collapsed : Visibility.Visible;
        var regions = (shot.Pixel?.Regions ?? []).OrderByDescending(region => region.ChangedPixels).ToList();
        int detectedRegionCount = Math.Max(regions.Count, shot.Pixel?.DetectedRegionCount ?? 0);
        RegionHeadingText.Text = detectedRegionCount == regions.Count
            ? $"差异区域（{regions.Count}）"
            : $"差异区域（显示 {regions.Count} / 共 {detectedRegionCount}）";
        RegionList.ItemsSource = regions;
        RegionList.SelectedIndex = regions.Count > 0 ? 0 : -1;
        if (regions.Count == 0)
        {
            _currentRegion = null;
            RefreshEvidenceChoices();
            RefreshAiPanel();
        }
        RefreshErrorPanel();
    }

    private void RegionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentRegion = RegionList.SelectedItem as PixelRegion;
        RefreshEvidenceChoices();
        RefreshAiPanel();
    }

    private void RegionList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (RegionList.Items.Count < 2) return;
        int current = Math.Max(0, RegionList.SelectedIndex);
        int next = Math.Clamp(current + (e.Delta < 0 ? 1 : -1), 0, RegionList.Items.Count - 1);
        if (next == current) return;
        RegionList.SelectedIndex = next;
        RegionList.ScrollIntoView(RegionList.SelectedItem);
        e.Handled = true;
    }

    private void RefreshEvidenceChoices()
    {
        var choices = new List<EvidenceChoice>();
        if (_currentShot?.Pixel?.ExactPixelMatch == true)
        {
            Add("完全一致", _currentShot.ReplayPath);
        }
        else if (_currentRegion is not null)
        {
            Add("AI 四宫格", _currentRegion.AiEvidencePath);
            AddPair("左右对比", _currentShot?.BaselinePath, _currentShot?.ReplayPath);
            Add("差异叠加", _currentRegion.OverlayCropPath);
            Add("基准区域", _currentRegion.BaselineCropPath);
            Add("回放区域", _currentRegion.ReplayCropPath);
            Add("差异区域", _currentRegion.DiffCropPath);
        }
        else if (_currentShot is not null)
        {
            Add("差异叠加", _currentShot.OverlayPath);
            AddPair("左右对比", _currentShot.BaselinePath, _currentShot.ReplayPath);
            Add("基准全图", _currentShot.BaselinePath);
            Add("回放全图", _currentShot.ReplayPath);
            Add("差异全图", _currentShot.DiffPath);
        }

        EvidenceModeCombo.ItemsSource = choices;
        EvidenceModeCombo.SelectedIndex = choices.Count > 0 ? 0 : -1;
        if (choices.Count == 0) ShowEvidence(null);
        return;

        void Add(string label, string? path)
        {
            if (!string.IsNullOrWhiteSpace(path)) choices.Add(new EvidenceChoice(label, path));
        }

        void AddPair(string label, string? baselinePath, string? replayPath)
        {
            if (!string.IsNullOrWhiteSpace(baselinePath) && !string.IsNullOrWhiteSpace(replayPath))
                choices.Add(new EvidenceChoice(label, baselinePath, replayPath));
        }
    }

    private void EvidenceModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowEvidence(EvidenceModeCombo.SelectedItem as EvidenceChoice);
        RefreshAiPanel();
    }

    private void ShowEvidence(EvidenceChoice? choice)
    {
        string? path = choice?.Path;
        string? secondaryPath = choice?.SecondaryPath;
        _currentEvidencePath = secondaryPath ?? path;
        EvidencePathText.Text = secondaryPath is null ? path ?? "" : $"{path}  ↕  {secondaryPath}";
        EvidenceImage.Source = null;
        BaselineComparisonImage.Source = null;
        ReplayComparisonImage.Source = null;
        EvidenceImage.Visibility = Visibility.Visible;
        SideBySideComparisonGrid.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(path))
        {
            ImageEmptyText.Text = "没有可显示的证据图";
            ImageEmptyText.Visibility = Visibility.Visible;
            return;
        }
        if (!File.Exists(path) || secondaryPath is not null && !File.Exists(secondaryPath))
        {
            ImageEmptyText.Text = "证据图不存在";
            ImageEmptyText.Visibility = Visibility.Visible;
            return;
        }
        try
        {
            if (secondaryPath is null)
            {
                EvidenceImage.Source = LoadBitmap(path);
            }
            else
            {
                BaselineComparisonImage.Source = LoadBitmap(path);
                ReplayComparisonImage.Source = LoadBitmap(secondaryPath);
                EvidenceImage.Visibility = Visibility.Collapsed;
                SideBySideComparisonGrid.Visibility = Visibility.Visible;
            }
            ImageEmptyText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ImageEmptyText.Text = $"图片无法读取：{ex.Message}";
            ImageEmptyText.Visibility = Visibility.Visible;
        }
    }

    private static BitmapImage LoadBitmap(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void RefreshAiPanel()
    {
        bool sideBySideMode = (EvidenceModeCombo.SelectedItem as EvidenceChoice)?.SecondaryPath is not null;
        if (_currentShot?.Pixel?.ExactPixelMatch == true || sideBySideMode)
        {
            AiPanel.Visibility = Visibility.Collapsed;
            UpdateSideColumn();
            return;
        }

        AiAssessment? assessment = HasNarrative(_currentRegion?.Ai) ? _currentRegion!.Ai :
            HasNarrative(_currentShot?.Ai) ? _currentShot!.Ai :
            HasNarrative(_currentCase?.Ai) ? _currentCase!.Ai : null;
        AiPanel.Visibility = assessment is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateSideColumn();
        if (assessment is null) return;
        AiConfidenceText.Text = assessment.Confidence is null ? VerdictText(assessment.Verdict) : $"{VerdictText(assessment.Verdict)}  ·  置信度 {assessment.Confidence:P0}";
        AiObservationText.Text = assessment.Observation ?? "未提供观察描述";
        AiReasonText.Text = assessment.Reason ?? "未提供判断原因";
    }

    private void RefreshErrorPanel()
    {
        var errors = new[]
        {
            _currentCase?.Error,
            _currentShot?.HardFailureCode is null ? null : $"硬失败: {_currentShot.HardFailureCode}",
            _currentShot?.Error,
        }.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        ErrorPanel.Visibility = errors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ErrorText.Text = string.Join(Environment.NewLine, errors);
        UpdateSideColumn();
    }

    private void UpdateSideColumn()
    {
        bool visible = AiPanel.Visibility == Visibility.Visible || ErrorPanel.Visibility == Visibility.Visible;
        AiGapColumn.Width = visible ? new GridLength(10) : new GridLength(0);
        AiColumn.Width = visible ? new GridLength(280) : new GridLength(0);
    }

    private void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentEvidencePath is null || !File.Exists(_currentEvidencePath)) return;
        Process.Start(new ProcessStartInfo(_currentEvidencePath) { UseShellExecute = true });
    }

    private void RevealImage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentEvidencePath is null || !File.Exists(_currentEvidencePath)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_currentEvidencePath}\"") { UseShellExecute = true });
    }

    private void ClearRound()
    {
        _currentRound = null;
        RoundCombo.ItemsSource = null;
        AttentionCountText.Text = FailedCountText.Text = PassedCountText.Text = TotalCountText.Text = "0";
        CaseList.ItemsSource = null;
        ClearCase();
    }

    private void ClearCase()
    {
        _currentCase = null;
        DetailRoot.Visibility = Visibility.Collapsed;
        ShotList.ItemsSource = null;
        ClearShot();
    }

    private void ClearShot()
    {
        _currentShot = null;
        _currentRegion = null;
        RegionList.ItemsSource = null;
        EvidenceModeCombo.ItemsSource = null;
        ExactMatchBadge.Visibility = Visibility.Collapsed;
        EvidenceModeCombo.Visibility = Visibility.Visible;
        RegionPanel.Visibility = Visibility.Visible;
        ShowEvidence(null);
        AiPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        UpdateSideColumn();
    }

    private static bool NeedsAttention(ShotComparisonResult shot) => shot.FinalVerdict is "failed" or "needs_review" or "uncertain" || shot.HardFailureCode is not null;
    private static bool HasNarrative(AiAssessment? ai) => ai?.Status == "completed" && (!string.IsNullOrWhiteSpace(ai.Observation) || !string.IsNullOrWhiteSpace(ai.Reason));
    private static string Guidance(TestCaseComparisonResult testCase) => testCase.FinalVerdict switch
    {
        "failed" => "发现明确偏离。建议优先查看默认选中的步骤和证据。",
        "needs_review" or "uncertain" => "证据还不足以自动定论，需要测试人员确认。",
        "passed" when testCase.Status != "passed" => "本地发现像素差异，AI 已结合项目背景复核为可接受。",
        "passed" => "本地与最终结果均通过，无需优先处理。",
        "cancelled" => "本次对比被取消，结果不完整。",
        "skipped" => "该用例未参与本轮比较。",
        _ => "结果状态未知，请查看技术信息。",
    };

    private static string VerdictText(string? verdict) => verdict switch
    {
        "failed" => "失败", "needs_review" or "uncertain" => "待确认", "passed" => "已通过",
        "cancelled" => "已取消", "skipped" => "已跳过", _ => "未知",
    };

    private static string RoleText(string role) => role switch { "first" => "流程开始", "last" => "流程结束", _ => "中间步骤" };
    private static Brush VerdictBrush(string verdict) => Solid(verdict switch { "failed" => "#B42318", "needs_review" or "uncertain" => "#B54708", "passed" => "#067647", _ => "#667085" });
    private static Brush BannerBrush(string verdict) => Solid(verdict switch { "failed" => "#FEF3F2", "needs_review" or "uncertain" => "#FFFAEB", "passed" => "#ECFDF3", _ => "#F9FAFB" });
    private static SolidColorBrush Solid(string value) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze(); return brush; }

    private static string? FindDefaultReportsDirectory()
    {
        string current = Path.Combine(Environment.CurrentDirectory, "reports");
        if (Directory.Exists(current)) return current;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (int level = 0; level < 5 && directory is not null; level++, directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "reports");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private sealed record EvidenceChoice(string Label, string Path, string? SecondaryPath = null);
}
