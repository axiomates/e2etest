using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace E2ETest.RecordLauncher;

public partial class MainWindow : Window
{
    private readonly string _cliPath;

    public MainWindow()
    {
        InitializeComponent();
        string applicationDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        _cliPath = Path.Combine(applicationDirectory, "e2etest.exe");
        RootBox.Text = applicationDirectory;
        CliPathText.Text = $"CLI：{_cliPath}";
        Loaded += (_, _) => NameBox.Focus();
    }

    private void BrowseRoot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 E2ETest 数据根目录",
            Multiselect = false,
            InitialDirectory = Directory.Exists(RootBox.Text) ? RootBox.Text : AppContext.BaseDirectory,
        };
        if (dialog.ShowDialog(this) == true) RootBox.Text = dialog.FolderName;
    }

    private async void StartRecording_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBuildStartInfo(out var startInfo)) return;

        StartButton.IsEnabled = false;
        StatusText.Text = "正在启动录制…";
        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null) throw new InvalidOperationException("系统未能启动 e2etest.exe。");
            Hide();
            await process.WaitForExitAsync();
            int exitCode = process.ExitCode;
            Show();
            Activate();
            StatusText.Text = exitCode == 0 ? "录制完成。" : $"录制未完成，CLI 退出码：{exitCode}";
            if (exitCode != 0)
                MessageBox.Show(this, "录制未成功完成，请查看 CLI 窗口或 root 下的日志。", "录制未完成",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Show();
            Activate();
            StatusText.Text = "启动失败。";
            MessageBox.Show(this, ex.Message, "无法启动录制", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            process?.Dispose();
            StartButton.IsEnabled = true;
        }
    }

    private bool TryBuildStartInfo(out ProcessStartInfo startInfo)
    {
        try
        {
            startInfo = RecordProcessStartInfoFactory.Create(
                _cliPath, RootBox.Text, NameBox.Text, FocusBox.Text, CriteriaBox.Text);
            return true;
        }
        catch (Exception ex)
        {
            startInfo = new ProcessStartInfo();
            MessageBox.Show(this, ex.Message, "请检查录制信息", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}
