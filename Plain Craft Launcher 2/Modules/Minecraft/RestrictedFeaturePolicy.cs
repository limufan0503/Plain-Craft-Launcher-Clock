using PCL.Core.App.Localization;
using PCL.Core.UI.Hints;
using PCL.Core.App.Region;

namespace PCL;

internal static class RestrictedFeaturePolicy
{
    internal static RegionAssessment Assess() => RegionPolicy.Evaluate(RegionPolicy.ReadWindowsSignals());

    // Clock 修改：关闭区域功能限制，使游戏下载、补全与整合包安装在任何地区均可用，
    // 并消除启动时顶部的“在你所在的国家或地区不可用”提示横幅。
    internal static bool IsRestricted => false;

    internal static bool Check(string feature, bool showHint = true) => true;

    internal static void ThrowIfRestricted(string feature)
    {
        if (Check(feature, false)) return;
        throw new Common.CancelledException();
    }
}
