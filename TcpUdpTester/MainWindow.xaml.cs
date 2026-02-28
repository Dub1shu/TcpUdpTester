using System.Collections.Specialized;
using System.Windows;
using TcpUdpTester.ViewModels;

namespace TcpUdpTester;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        // トラフィックログに新規エントリ追加時に最下行へ自動スクロール
        _viewModel.FilteredLog.CollectionChanged += OnFilteredLogChanged;
    }

    private void OnFilteredLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && TrafficGrid.Items.Count > 0)
        {
            TrafficGrid.ScrollIntoView(TrafficGrid.Items[^1]);
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.FilteredLog.CollectionChanged -= OnFilteredLogChanged;
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
