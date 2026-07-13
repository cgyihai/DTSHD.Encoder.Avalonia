using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace DTSHD.Encoder.Avalonia.Controls;

/// <summary>声道布局图示：随主题的方形圆角面板内，按位置点亮各扬声器（圆角方块）。</summary>
public sealed class SpeakerLayout : UserControl
{
    private static readonly Color Accent = Color.FromArgb(0xFF, 0xF5, 0xA6, 0x23); // DTS 橙
    private readonly Grid _grid;

    private static readonly Dictionary<string, (int r, int c)> Pos = new()
    {
        ["L"] = (0, 0), ["C"] = (0, 1), ["R"] = (0, 2),
        ["Lw"] = (1, 0), ["Rw"] = (1, 2),
        ["Ls"] = (2, 0), ["Rs"] = (2, 2), ["Lss"] = (2, 0), ["Rss"] = (2, 2),
        ["LFE"] = (2, 1),
        ["Lsr"] = (3, 0), ["Rsr"] = (3, 2), ["Lrs"] = (3, 0), ["Rrs"] = (3, 2),
        ["Lb"] = (3, 0), ["Rb"] = (3, 2), ["Cs"] = (3, 1), ["S"] = (3, 1),
    };

    public SpeakerLayout()
    {
        _grid = new Grid { RowSpacing = 4, ColumnSpacing = 6 };
        for (int i = 0; i < 4; i++) _grid.RowDefinitions.Add(new RowDefinition());
        for (int i = 0; i < 3; i++) _grid.ColumnDefinitions.Add(new ColumnDefinition());

        Content = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = Res("CardBackgroundFillColorSecondaryBrush", Color.FromArgb(0x10, 0x80, 0x80, 0x80)),
            BorderBrush = Res("CardStrokeColorDefaultBrush", Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Width = 150,
            Height = 150,           // 四等边正方（缩小）
            Child = _grid,
        };
    }

    private static Brush Res(string key, Color fallback)
    {
        var app = Application.Current;
        var theme = app?.ActualThemeVariant ?? ThemeVariant.Default;
        if (app != null && app.Resources.TryGetResource(key, theme, out var v) && v is Brush b) return b;
        return new SolidColorBrush(fallback);
    }

    public void SetChannels(IEnumerable<string> channels)
    {
        _grid.Children.Clear();
        var extras = new List<string>();
        foreach (var ch in channels)
        {
            if (Pos.TryGetValue(ch, out var p))
            {
                var cell = MakeSpeaker(ch, ch == "LFE");
                Grid.SetRow(cell, p.r);
                Grid.SetColumn(cell, p.c);
                _grid.Children.Add(cell);
            }
            else extras.Add(ch);
        }
        if (extras.Count > 0)
        {
            var wrap = new TextBlock
            {
                Text = string.Join(" · ", extras),
                Foreground = new SolidColorBrush(Accent),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(wrap, 3); Grid.SetColumn(wrap, 0); Grid.SetColumnSpan(wrap, 3);
            _grid.Children.Add(wrap);
        }
    }

    private static Border MakeSpeaker(string label, bool lfe)
    {
        return new Border
        {
            Width = 30,
            Height = 26,                       // 圆角方块（缩小）
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(lfe ? Color.FromArgb(0xFF, 0xC8, 0x86, 0x12) : Accent),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x20, 0x20, 0x20)),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }
}
