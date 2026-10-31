using System.IO;
using PCL.Core.Minecraft.Server;

namespace PCL;

// Clock 新增：服务器运行辅助（开服 / 选 Java / 运行状态）。
// 本移植版采用「一键开服」独立进程模型：启动 java 进程后不长期持有句柄，
// 因此 IsRunning 默认返回 false；如需真正的进程追踪可在此扩展。
public static class ModServer
{
	// 为给定服务器选择可执行的 Java 路径。
	public static string? SelectJavaPath(ServerInfo target)
	{
		if (!string.IsNullOrWhiteSpace(target.JavaPath) && File.Exists(target.JavaPath))
			return target.JavaPath;
		// 回退到 PATH 中的 java
		return "java";
	}

	public static bool IsRunning(ServerInfo server) => false;

	public static void Stop(ServerInfo server, bool force = false)
	{
		// 简化模型下不追踪进程，留空。
	}
}
