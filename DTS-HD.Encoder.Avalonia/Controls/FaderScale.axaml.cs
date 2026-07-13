using Avalonia.Controls;

namespace DTSHD.Encoder.Avalonia.Controls;

/// <summary>
/// 竖直推子旁的 dB 刻度尺（纯展示控件，无逻辑）。
/// 与 <see cref="DbFader"/> 并排显示，复刻官方下混/比特流推子的 0dB 刻度标记。
/// </summary>
public partial class FaderScale : UserControl
{
    public FaderScale() => InitializeComponent();
}
