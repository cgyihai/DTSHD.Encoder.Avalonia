using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;

namespace DTSHD.Encoder.Avalonia.Services;

/// <summary>
/// 运行时语言切换：把对应语言的字符串资源字典合并进 Application.Resources，
/// 界面中用 {DynamicResource Lang.Xxx} 引用；切换语言时替换该字典，所有 DynamicResource
/// 会自动实时刷新，无需重启（这是 Avalonia 最稳妥的实时本地化方式，与主题切换同机制）。
/// </summary>
public static class LocalizationManager
{
    /// <summary>受支持的语言：简体中文 / 英文。</summary>
    public static readonly IReadOnlyList<(string Code, string Display)> Languages = new[]
    {
        ("zh-Hans", "简体中文"),
        ("en", "English"),
    };

    private const string BaseUri = "avares://DTS-HD.Encoder.Avalonia/Assets/Lang/";

    /// <summary>当前语言代码（zh-Hans / en）。</summary>
    public static string Current { get; private set; } = "zh-Hans";

    /// <summary>语言变化后触发（供 VM 刷新非 DynamicResource 的动态文案，如引擎状态）。</summary>
    public static event Action? LanguageChanged;

    // 当前合并进 Application.Resources 的语言字典，切换时先移除旧的再加入新的。
    private static ResourceInclude? _currentDict;

    /// <summary>把配置里的语言设置规整为受支持的代码（System/空/未知 → 跟随系统，非中文即英文）。</summary>
    public static string Resolve(string? setting)
    {
        if (string.Equals(setting, "en", StringComparison.OrdinalIgnoreCase)) return "en";
        if (string.Equals(setting, "zh-Hans", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
        // System / 其它：按当前 UI 文化判断
        try
        {
            var name = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return string.Equals(name, "zh", StringComparison.OrdinalIgnoreCase) ? "zh-Hans" : "en";
        }
        catch { return "zh-Hans"; }
    }

    /// <summary>应用启动时按设置初始化语言（在 App.Styles 之后、主窗口创建前调用）。</summary>
    public static void Initialize(string? setting) => Apply(Resolve(setting), persist: false);

    /// <summary>切换语言（实时生效）。code 为 zh-Hans / en。</summary>
    public static void SetLanguage(string code, bool persist = true) => Apply(Resolve(code), persist);

    private static void Apply(string code, bool persist)
    {
        var app = Application.Current;
        if (app == null) return;

        var dict = new ResourceInclude((Uri?)null)
        {
            Source = new Uri(BaseUri + code + ".axaml"),
        };

        // 先移除旧语言字典再加入新的（避免同时存在两套键）
        if (_currentDict != null)
            app.Resources.MergedDictionaries.Remove(_currentDict);
        app.Resources.MergedDictionaries.Add(dict);
        _currentDict = dict;
        Current = code;

        if (persist)
        {
            AppServices.Settings.Language = code;
            AppServices.SaveSettings();
        }
        LanguageChanged?.Invoke();
    }

    /// <summary>取当前语言下某个键的字符串（供代码里动态拼接文案时使用）。</summary>
    public static string Get(string key, string fallback = "")
    {
        try
        {
            if (Application.Current?.Resources.TryGetResource(key, null, out var v) == true && v is string s)
                return s;
        }
        catch { }
        return fallback;
    }
}
