using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace DTSHD.Encoder.Avalonia.Controls;

/// <summary>仿官方的垂直 dB 推子；最低档=INF(静音)；底部数值框可手动输入。</summary>
public partial class DbFader : UserControl
{
    public const double InfThreshold = -59.5;
    private bool _sync;

    /// <summary>可绑定 dB 值（字符串，如 "3.0" / "INF"），供 View 双向绑定到 VM。</summary>
    public static readonly StyledProperty<string> DbValueProperty =
        AvaloniaProperty.Register<DbFader, string>(nameof(DbValue), "INF");

    public string DbValue
    {
        get => GetValue(DbValueProperty);
        set => SetValue(DbValueProperty, value);
    }

    /// <summary>DbValue 变化时同步滑块/文本框（VM→控件方向）；_sync 防止双向回环。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DbValueProperty && !_sync)
        {
            SyncFromDbValue(change.NewValue as string);
        }
    }

    private void SyncFromDbValue(string? dbValue)
    {
        if (Sld == null) return;   // 模板未应用（XAML 解析期可能先于 OnApplyTemplate）
        _sync = true;
        Sld.Value = ParseDb(dbValue);
        if (ValBox != null) ValBox.Text = DbText;
        _sync = false;
    }

    public DbFader()
    {
        InitializeComponent();
        ValBox.LostFocus += (_, _) => CommitText();
        ValBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitText(); e.Handled = true; } };
        SyncText();
    }

    public string Caption { get => CaptionText.Text!; set => CaptionText.Text = value; }
    public double Minimum { get => Sld.Minimum; set => Sld.Minimum = value; }
    public double Maximum { get => Sld.Maximum; set => Sld.Maximum = value; }

    public string DbText => Sld.Value <= InfThreshold ? "INF" : Sld.Value.ToString("0.0", CultureInfo.InvariantCulture);

    public void SetDb(string? s)
    {
        _sync = true;
        Sld.Value = ParseDb(s);
        ValBox.Text = DbText;
        DbValue = DbText;
        _sync = false;
    }

    private double ParseDb(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Sld.Minimum;
        s = s.Trim();
        if (s.Equals("INF", StringComparison.OrdinalIgnoreCase) || s.Equals("-INF", StringComparison.OrdinalIgnoreCase))
            return Sld.Minimum;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            return Math.Clamp(v, Sld.Minimum, Sld.Maximum);
        return Sld.Value;
    }

    private void OnSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_sync) return;
        // Snap to 0.5 increments — replaces WinUI Slider.StepFrequency (not available in Avalonia).
        var snapped = Math.Round(Sld.Value * 2) / 2;
        if (Math.Abs(snapped - Sld.Value) > 1e-9)
        {
            _sync = true;
            Sld.Value = snapped;
            _sync = false;
        }
        SyncText();
        _sync = true;
        DbValue = DbText;
        _sync = false;
    }

    private void CommitText()
    {
        _sync = true;
        Sld.Value = ParseDb(ValBox.Text);
        ValBox.Text = DbText;
        DbValue = DbText;
        _sync = false;
    }

    private void SyncText()
    {
        if (ValBox != null) ValBox.Text = DbText;
    }
}
