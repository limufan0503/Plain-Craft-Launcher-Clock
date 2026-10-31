using System.IO;
using PCL.Core.Minecraft.Server;

namespace PCL;

// Clock 新增：服务器运行辅助（开服 / 选 Java / 运行状态）。
// 实际进程管理由 ServerManager（PCL.Core）实现，这里做 UI 层薄封装。
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

	public static bool IsRunning(ServerInfo server) => ServerManager.IsRunning(server);

	public static void Stop(ServerInfo server, bool force = false) => ServerManager.StopServer(server);
}
