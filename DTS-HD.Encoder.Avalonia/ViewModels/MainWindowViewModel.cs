using System.Collections.Generic;
using AvaloniaFluentUI.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DTSHD.Encoder.Avalonia.ViewModels;

/// <summary>
/// 主窗口壳 VM：管理 NavigationView 选中项与 CurrentViewModel（ViewLocator 解析为对应 View）。
/// 复刻 Avalonia-Fluent-UI Gallery.MainWindowViewModel 的工厂 + 缓存式导航。
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly Dictionary<string, ViewModelBase> _cache = new();

    /// <summary>工厂字典：Tag 字符串 → VM 构造委托。Settings 由 NavigationView 的 IsSettingsVisible=true 触发，Tag 固定为 "Settings"。</summary>
    private readonly Dictionary<string, System.Func<ViewModelBase>> _factories = new()
    {
        ["Encode"]   = () => new EncodePageViewModel(),
        ["Stream"]   = () => new StreamToolsPageViewModel(),
        ["Settings"] = () => new SettingsPageViewModel(),
    };

    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    [ObservableProperty]
    private object? _navigationViewSelectedItem;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    private bool _canGoBack;

    private readonly List<string> _history = new();

    /// <summary>用户在 NavigationView 里点选某项后，按 Tag 切换页面。</summary>
    partial void OnNavigationViewSelectedItemChanged(object? value)
    {
        if (value is NavigationViewItem item)
            TogglePage(item.Tag as string ?? "");
    }

    [RelayCommand]
    private void TogglePage(string page)
    {
        if (!_factories.TryGetValue(page, out var factory)) return;
        if (!_cache.TryGetValue(page, out var target))
        {
            target = factory();
            _cache[page] = target;
        }
        if (CurrentViewModel != null)
            _history.Add(GetKeyByViewModel(CurrentViewModel) ?? "");
        CurrentViewModel = target;
        CanGoBack = _history.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (_history.Count == 0) return;
        var last = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        if (!_cache.TryGetValue(last, out var target))
        {
            if (!_factories.TryGetValue(last, out var factory)) return;
            target = factory();
            _cache[last] = target;
        }
        CurrentViewModel = target;
        CanGoBack = _history.Count > 0;
    }

    /// <summary>当前活跃 VM 的回查 Tag（仅用于历史栈）。</summary>
    private string? GetKeyByViewModel(ViewModelBase vm)
    {
        foreach (var kv in _cache)
            if (ReferenceEquals(kv.Value, vm)) return kv.Key;
        return null;
    }

    /// <summary>启动时默认进入编码页（由 MainWindow.Loaded 调用）。</summary>
    public void Initialize()
    {
        if (CurrentViewModel == null)
            TogglePage("Encode");
    }
}
