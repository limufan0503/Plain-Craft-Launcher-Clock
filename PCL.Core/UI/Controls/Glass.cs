namespace PCL.Core.UI.Controls;

// Clock: 液态玻璃（LiquidGlass）资源键与默认参数集中管理。
// 与 Application.xaml 中 ColorBrushGlass* / GlassRefractionScale 资源一一对应，
// 供代码内按名称引用，避免散落魔法字符串。
public static class Glass
{
    /// <summary>玻璃染色画刷资源键（淡蓝半透明）。</summary>
    public const string TintBrushKey = "ColorBrushGlassTint";

    /// <summary>玻璃顶部镜面高光画刷资源键。</summary>
    public const string HighlightBrushKey = "ColorBrushGlassHighlight";

    /// <summary>玻璃内描边高光画刷资源键。</summary>
    public const string BorderBrushKey = "ColorBrushGlassBorder";

    /// <summary>玻璃折射缩放资源键。</summary>
    public const string RefractionScaleKey = "GlassRefractionScale";

    /// <summary>默认折射缩放：轻微放大背景，模拟光线弯曲。</summary>
    public const double DefaultRefractionScale = 1.08;

    /// <summary>默认高光不透明度。</summary>
    public const double DefaultHighlightOpacity = 0.5;
}
