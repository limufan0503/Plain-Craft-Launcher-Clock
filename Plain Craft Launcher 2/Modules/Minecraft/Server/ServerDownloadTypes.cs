using System.Collections.Generic;
using PCL.Network;

namespace PCL.Core.Minecraft.Server;

// Clock 新增：服务端核心类型。
public enum ServerCoreType
{
	Vanilla,
	Paper,
	Purpur,
	Folia,
	Fabric
}

// 单个可下载的版本条目。
public sealed class ServerVersionEntry
{
	public required string Version { get; init; }

	public string? Label { get; init; }

	public string? Info { get; init; }

	public int JavaMajorVersion { get; init; }

	public override string ToString() => Version;
}

// 解析出的某核心某版本的一次性下载信息。
public sealed class ServerDownloadInfo
{
	public required ServerCoreType Type { get; init; }

	public required string McVersion { get; init; }

	public required string JarFileName { get; init; }

	public required List<DownloadFile> Files { get; init; }

	public int JavaMajorVersion { get; init; }

	public bool IsInstaller { get; init; }

	public string? InstallerPath { get; init; }

	public string? InstallerLoaderVersion { get; init; }
}
