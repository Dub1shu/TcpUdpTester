using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TcpUdpTester.Core;
using TcpUdpTester.ViewModels;

namespace TcpUdpTester;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private ScrollViewer? _trafficScrollViewer;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        var settings = SettingsService.Load();
        ApplyWindowSettings(settings);
        _viewModel.ApplySettings(settings);

        // DataGrid のテンプレート適用後に ScrollViewer をキャッシュ
        TrafficGrid.Loaded += (_, _) => _trafficScrollViewer = FindScrollViewer(TrafficGrid);

        // トラフィックログに新規エントリ追加時に最下行へ自動スクロール
        _viewModel.FilteredLog.CollectionChanged += OnFilteredLogChanged;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject obj)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is ScrollViewer sv) return sv;
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    private void ApplyWindowSettings(AppSettings s)
    {
        if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop))
        {
            Left = s.WindowLeft;
            Top  = s.WindowTop;
        }
        Width  = s.WindowWidth;
        Height = s.WindowHeight;
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void OnFilteredLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || TrafficGrid.Items.Count == 0)
            return;

        // ScrollViewer を直接使うことで行仮想化の影響を受けずに最下行へスクロール
        (_trafficScrollViewer ??= FindScrollViewer(TrafficGrid))?.ScrollToBottom();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.FilteredLog.CollectionChanged -= OnFilteredLogChanged;

        var settings = _viewModel.CaptureSettings();
        var isNormal = WindowState == WindowState.Normal;
        settings.WindowLeft      = isNormal ? Left   : RestoreBounds.Left;
        settings.WindowTop       = isNormal ? Top    : RestoreBounds.Top;
        settings.WindowWidth     = isNormal ? Width  : RestoreBounds.Width;
        settings.WindowHeight    = isNormal ? Height : RestoreBounds.Height;
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        SettingsService.Save(settings);

        _viewModel.Dispose();
    }

    private void MenuItem_Exit(object sender, RoutedEventArgs e) => Close();

    private void MenuItem_About(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "NetTest Console v1.0\n" +
            ".NET 8 / WPF\n\n" +
            "TCP / UDP 通信テストツール\n" +
            "TCP Client / TCP Server / UDP モード対応",
            "バージョン情報",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
