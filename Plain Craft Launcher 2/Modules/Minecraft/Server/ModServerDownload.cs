using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PCL.Core.Minecraft.Server;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

// Clock 新增：服务端核心下载（原版 / Paper / Purpur / Folia / Fabric）。
// 改写自 klnalortk/Plain-Craft-Launcher-Clock 的反编译代码，API 适配 PCL-CE：
//   ModBase -> Common，ModDownload -> GameDownload，ModLoader -> Loading，
//   版本列表改为直接请求 Mojang 官方 manifest（dlClientListLoader 在 PCL-CE 为强类型结果）。
public static class ModServerDownload
{
	private const string PaperApi = "https://fill.papermc.io/v3/projects";
	private const string UserAgent = "PCL-CE/2.15.0 (https://github.com/PCL-Community/PCL-CE)";

	public static string GetTypeName(ServerCoreType type) => type switch
	{
		ServerCoreType.Vanilla => "原版",
		ServerCoreType.Paper => "Paper",
		ServerCoreType.Purpur => "Purpur",
		ServerCoreType.Folia => "Folia",
		ServerCoreType.Fabric => "Fabric",
		_ => type.ToString()
	};

	public static string GetTypeDescription(ServerCoreType type) => type switch
	{
		ServerCoreType.Vanilla => "官方原版服务端，兼容性最好",
		ServerCoreType.Paper => "高性能原版分支，插件生态丰富",
		ServerCoreType.Purpur => "基于 Paper 的增强分支，功能更多",
		ServerCoreType.Folia => "Paper 的并发分支，支持分世界多线程",
		ServerCoreType.Fabric => "轻量模组加载器服务端",
		_ => ""
	};

	public static string GetTypeIcon(ServerCoreType type) => type switch
	{
		ServerCoreType.Vanilla => "lucide/package",
		ServerCoreType.Paper => "lucide/file-archive",
		ServerCoreType.Purpur => "lucide/sparkles",
		ServerCoreType.Folia => "lucide/layers",
		ServerCoreType.Fabric => "lucide/scroll",
		_ => "lucide/server"
	};

	public static Task<List<ServerVersionEntry>> GetVersionsAsync(ServerCoreType type)
		=> Task.Run(() => GetVersions(type));

	private static List<ServerVersionEntry> GetVersions(ServerCoreType type) => type switch
	{
		ServerCoreType.Vanilla => GetVanillaVersions(),
		ServerCoreType.Paper => GetPaperLikeVersions("paper"),
		ServerCoreType.Folia => GetPaperLikeVersions("folia"),
		ServerCoreType.Purpur => GetPurpurVersions(),
		ServerCoreType.Fabric => GetFabricVersions(),
		_ => throw new ArgumentOutOfRangeException(nameof(type))
	};

	#region 版本列表解析

	private static List<ServerVersionEntry> GetVanillaVersions()
	{
		var manifest = FetchJson("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json");
		if (manifest?["versions"] is not JsonArray jsonArray)
			throw new Exception("无法获取原版版本列表");
		var list = new List<ServerVersionEntry>();
		foreach (var item in jsonArray)
		{
			if (item is not JsonObject jsonObject || (string?)jsonObject["type"] != "release")
				continue;
			var id = (string?)jsonObject["id"];
			if (string.IsNullOrWhiteSpace(id))
				continue;
			DateTime? dateTime = null;
			if (DateTime.TryParse((string?)jsonObject["releaseTime"], CultureInfo.InvariantCulture,
					DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var result))
				dateTime = result.ToLocalTime();
			list.Add(new ServerVersionEntry
			{
				Version = id,
				Info = !dateTime.HasValue ? "正式版" : "发布于 " + dateTime.Value.ToString("d"),
				JavaMajorVersion = InferJavaMajor(id)
			});
		}
		return list;
	}

	private static List<ServerVersionEntry> GetPaperLikeVersions(string project)
	{
		var json = FetchJson("https://fill.papermc.io/v3/projects/" + project);
		if (json?["versions"] is not JsonObject jsonObject)
			throw new Exception("无法获取 " + project + " 版本列表");
		var list = new List<ServerVersionEntry>();
		var label = project.Equals("paper", StringComparison.OrdinalIgnoreCase) ? "Paper" : "Folia";
		foreach (var item in jsonObject)
		{
			if (item.Value is not JsonArray jsonArray)
				continue;
			foreach (var item2 in jsonArray)
			{
				var text = item2?.ToString();
				if (!string.IsNullOrWhiteSpace(text) && IsStableVersion(text))
					list.Add(new ServerVersionEntry
					{
						Version = text,
						Info = label + " 稳定版",
						JavaMajorVersion = InferJavaMajor(text)
					});
			}
		}
		return list;
	}

	private static List<ServerVersionEntry> GetPurpurVersions()
	{
		var json = FetchJson("https://api.purpurmc.org/v2/purpur");
		if (json?["versions"] is not JsonArray jsonArray)
			throw new Exception("无法获取 Purpur 版本列表");
		var list = new List<ServerVersionEntry>();
		foreach (var item in jsonArray)
		{
			var text = item?.ToString();
			if (!string.IsNullOrWhiteSpace(text) && IsStableVersion(text))
				list.Add(new ServerVersionEntry
				{
					Version = text,
					Info = "Purpur 稳定版",
					JavaMajorVersion = InferJavaMajor(text)
				});
		}
		list.Reverse();
		return list;
	}

	private static List<ServerVersionEntry> GetFabricVersions()
	{
		var json = FetchJson("https://meta.fabricmc.net/v2/versions/game");
		if (json is not JsonArray jsonArray)
			throw new Exception("无法获取 Fabric 游戏版本列表");
		var list = new List<ServerVersionEntry>();
		foreach (var item in jsonArray)
		{
			if (item is not JsonObject jsonObject)
				continue;
			if (jsonObject["stable"]?.GetValue<bool>() != true)
				continue;
			var text = (string?)jsonObject["version"];
			if (!string.IsNullOrWhiteSpace(text) && IsStableVersion(text))
				list.Add(new ServerVersionEntry
				{
					Version = text,
					Info = "Fabric 稳定版",
					JavaMajorVersion = InferJavaMajor(text)
				});
		}
		return list;
	}

	#endregion

	#region 安装加载器

	public static Loading.LoaderCombo<string> CreateInstallLoader(ServerInfo target, ServerCoreType type, string mcVersion)
	{
		ServerDownloadInfo info = null;
		var loaderTask = new Loading.LoaderTask<string, List<DownloadFile>>("解析下载地址",
			task =>
			{
				info = ResolveDownload(target, type, mcVersion);
				target.JavaMajorVersion = info.JavaMajorVersion;
				task.output = info.Files;
			})
		{
			ProgressWeight = 1.0,
			show = false
		};

		var loaderDownload = new LoaderDownload("下载服务端核心", new List<DownloadFile>())
		{
			ProgressWeight = 8.0
		};

		var installTask = new Loading.LoaderTask<string, string>("安装服务端核心",
			task =>
			{
				if (info == null)
					throw new Exception("解析下载地址失败");
				task.Progress = 0.2;
				FinalizeInstall(target, info, task);
				task.Progress = 1.0;
			})
		{
			ProgressWeight = (type == ServerCoreType.Fabric) ? 3.0 : 1.0
		};

		var loaderCombo = new Loading.LoaderCombo<string>("下载服务端：" + GetTypeName(type) + " " + mcVersion,
			new Loading.LoaderBase[3] { loaderTask, loaderDownload, installTask });
		loaderCombo.OnStateChangedUi += (_, _, _) =>
		{
			Launcher.frmMain?.BtnExtraDownload.ShowRefresh();
		};
		return loaderCombo;
	}

	private static ServerDownloadInfo ResolveDownload(ServerInfo target, ServerCoreType type, string mcVersion) => type switch
	{
		ServerCoreType.Vanilla => ResolveVanilla(target, mcVersion),
		ServerCoreType.Paper => ResolvePaperLike(target, "paper", mcVersion),
		ServerCoreType.Folia => ResolvePaperLike(target, "folia", mcVersion),
		ServerCoreType.Purpur => ResolvePurpur(target, mcVersion),
		ServerCoreType.Fabric => ResolveFabric(target, mcVersion),
		_ => throw new ArgumentOutOfRangeException(nameof(type))
	};

	private static ServerDownloadInfo ResolveVanilla(ServerInfo target, string mcVersion)
	{
		var manifest = FetchJson("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json");
		if (manifest?["versions"] is not JsonArray jsonArray)
			throw new Exception("无法获取原版版本列表");
		JsonObject? jsonObject = null;
		foreach (var item in jsonArray)
			if (item is JsonObject jsonObject2 && (string?)jsonObject2["id"] == mcVersion)
			{
				jsonObject = jsonObject2;
				break;
			}
		var text = (string?)jsonObject?["url"];
		if (string.IsNullOrWhiteSpace(text))
			throw new Exception("找不到版本 " + mcVersion + " 的信息");
		JsonNode? jsonNode = null;
		Exception? innerException = null;
		foreach (var source in GameDownload.DlSourceLauncherOrMetaGet(text))
		{
			try
			{
				jsonNode = FetchJson(source);
			}
			catch (Exception ex)
			{
				innerException = ex;
				continue;
			}
			break;
		}
		if (jsonNode == null)
			throw new Exception("无法获取版本 " + mcVersion + " 的详情", innerException);
		if (jsonNode["downloads"]?["server"] is not JsonObject jsonObject3)
			throw new Exception("版本 " + mcVersion + " 不提供官方服务端下载");
		var url = (string?)jsonObject3["url"];
		if (string.IsNullOrWhiteSpace(url))
			throw new Exception("版本 " + mcVersion + " 的服务端下载地址为空");
		var actualSize = (long?)jsonObject3["size"] ?? -1;
		var sha1 = (string?)jsonObject3["sha1"];
		var javaMajor = (int?)jsonNode["javaVersion"]?["majorVersion"] ?? InferJavaMajor(mcVersion);
		var jarName = mcVersion + "-server.jar";
		var localPath = Path.Combine(target.ServerPath, jarName);
		return new ServerDownloadInfo
		{
			Type = ServerCoreType.Vanilla,
			McVersion = mcVersion,
			JarFileName = jarName,
			JavaMajorVersion = javaMajor,
			Files = new List<DownloadFile>
			{
				new DownloadFile(GameDownload.DlSourceLauncherOrMetaGet(url), localPath,
					new Common.FileChecker(1024L, actualSize, sha1 ?? ""))
			}
		};
	}

	private static ServerDownloadInfo ResolvePaperLike(ServerInfo target, string project, string mcVersion)
	{
		var json = FetchJson($"{"https://fill.papermc.io/v3/projects"}/{project}/versions/{mcVersion}/builds");
		if (json is not JsonArray { Count: not 0 } jsonArray)
			throw new Exception("找不到 " + project + " " + mcVersion + " 的构建");
		JsonObject? jsonObject = null;
		foreach (var item in jsonArray)
			if (item is JsonObject jsonObject2 && (string?)jsonObject2["channel"] == "STABLE")
			{
				jsonObject = jsonObject2;
				break;
			}
		jsonObject ??= jsonArray[0] as JsonObject;
		if (jsonObject?["downloads"]?["server:default"] is not JsonObject jsonObject3)
			throw new Exception("找不到 " + project + " " + mcVersion + " 的下载");
		var url = (string?)jsonObject3["url"];
		var name = (string?)jsonObject3["name"] ?? (project + "-" + mcVersion + ".jar");
		var actualSize = (long?)jsonObject3["size"] ?? -1;
		var sha256 = (string?)jsonObject3["checksums"]?["sha256"];
		if (string.IsNullOrWhiteSpace(url))
			throw new Exception("找不到 " + project + " " + mcVersion + " 的下载地址");
		return new ServerDownloadInfo
		{
			Type = !project.Equals("folia", StringComparison.OrdinalIgnoreCase) ? ServerCoreType.Paper : ServerCoreType.Folia,
			McVersion = mcVersion,
			JarFileName = name,
			JavaMajorVersion = InferJavaMajor(mcVersion),
			Files = new List<DownloadFile>
			{
				new DownloadFile(new[] { url }, Path.Combine(target.ServerPath, name),
					new Common.FileChecker(1024L, actualSize, sha256 ?? ""), useBrowserUserAgent: false,
					customUserAgent: UserAgent)
			}
		};
	}

	private static ServerDownloadInfo ResolvePurpur(ServerInfo target, string mcVersion)
	{
		var build = (string?)FetchJson("https://api.purpurmc.org/v2/purpur/" + mcVersion)?["builds"]?["latest"];
		if (string.IsNullOrWhiteSpace(build))
			throw new Exception("找不到 Purpur " + mcVersion + " 的构建");
		var url = $"https://api.purpurmc.org/v2/purpur/{mcVersion}/{build}/download";
		var name = $"purpur-{mcVersion}-{build}.jar";
		return new ServerDownloadInfo
		{
			Type = ServerCoreType.Purpur,
			McVersion = mcVersion,
			JarFileName = name,
			JavaMajorVersion = InferJavaMajor(mcVersion),
			Files = new List<DownloadFile>
			{
				new DownloadFile(new[] { url }, Path.Combine(target.ServerPath, name),
					new Common.FileChecker(1024L, -1L))
			}
		};
	}

	private static ServerDownloadInfo ResolveFabric(ServerInfo target, string mcVersion)
	{
		var rawLoader = FetchJson("https://meta.fabricmc.net/v2/versions/loader/" + mcVersion);
		if (rawLoader is not JsonArray { Count: not 0 } jsonArray)
			throw new Exception("找不到 Fabric " + mcVersion + " 的加载器");
		JsonObject? loader = null;
		foreach (var item in jsonArray)
			if (item is JsonObject jsonObject2 && jsonObject2["loader"]?["stable"]?.GetValue<bool>() == true)
			{
				loader = jsonObject2;
				break;
			}
		loader ??= jsonArray[0] as JsonObject;
		var loaderVersion = (string?)loader?["loader"]?["version"];
		if (string.IsNullOrWhiteSpace(loaderVersion))
			throw new Exception("找不到 Fabric " + mcVersion + " 的加载器版本");
		var rawInstaller = FetchJson("https://meta.fabricmc.net/v2/versions/installer");
		if (rawInstaller is not JsonArray { Count: not 0 } installerArray)
			throw new Exception("找不到 Fabric 安装器");
		JsonObject? installer = null;
		foreach (var item in installerArray)
			if (item is JsonObject jsonObject4 && jsonObject4["stable"]?.GetValue<bool>() == true)
			{
				installer = jsonObject4;
				break;
			}
		installer ??= installerArray[0] as JsonObject;
		var installerUrl = (string?)installer?["url"];
		if (string.IsNullOrWhiteSpace(installerUrl))
			throw new Exception("找不到 Fabric 安装器下载地址");
		var installerPath = Path.Combine(target.ServerPath, "fabric-installer.jar");
		return new ServerDownloadInfo
		{
			Type = ServerCoreType.Fabric,
			McVersion = mcVersion,
			JarFileName = "fabric-server-launch.jar",
			JavaMajorVersion = InferJavaMajor(mcVersion),
			IsInstaller = true,
			InstallerPath = installerPath,
			InstallerLoaderVersion = loaderVersion,
			Files = new List<DownloadFile>
			{
				new DownloadFile(new[] { installerUrl }, installerPath, new Common.FileChecker(1024L, -1L))
			}
		};
	}

	#endregion

	#region 安装完成 / Fabric 安装器

	private static void FinalizeInstall(ServerInfo target, ServerDownloadInfo info, Loading.LoaderBase task)
	{
		Directory.CreateDirectory(target.ServerPath);
		target.JavaMajorVersion = info.JavaMajorVersion;
		if (info.IsInstaller)
			RunFabricInstaller(target, info, task);
		var jarPath = Path.Combine(target.ServerPath, info.JarFileName);
		var minSize = info.IsInstaller ? 64L : 1024L;
		if (!File.Exists(jarPath) || new FileInfo(jarPath).Length < minSize)
			throw new Exception("服务端核心文件缺失或过小：" + info.JarFileName);
		if (info.InstallerPath != null && File.Exists(info.InstallerPath))
		{
			try { File.Delete(info.InstallerPath); }
			catch (Exception ex) { Common.Log(ex, "删除服务端安装器失败"); }
		}
		target.CoreType = info.Type.ToString();
		target.McVersion = info.McVersion;
		target.JarFileName = info.JarFileName;
		if (target.MemoryMb <= 0)
			target.MemoryMb = 2048;
		ServerManager.RefreshCoreFile(target);
		ServerManager.SaveServerConfig(target, notify: false);
		Common.Log($"[ServerDownload] 服务端安装完成：{target.Name}（{target.CoreType} {target.McVersion}）");
	}

	private static void RunFabricInstaller(ServerInfo target, ServerDownloadInfo info, Loading.LoaderBase task)
	{
		var java = ModServer.SelectJavaPath(target) ?? throw new Exception("未找到可用的 Java 运行环境");
		if (info.InstallerPath == null || !File.Exists(info.InstallerPath))
			throw new Exception("Fabric 安装器文件缺失");
		var psi = new ProcessStartInfo
		{
			FileName = java,
			WorkingDirectory = target.ServerPath,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		foreach (var arg in new[]
		{
			"-jar", info.InstallerPath, "server",
			"-dir", target.ServerPath,
			"-mcversion", info.McVersion,
			"-loader", info.InstallerLoaderVersion ?? "",
			"-downloadMinecraft"
		})
			psi.ArgumentList.Add(arg);
		using var process = new Process { StartInfo = psi };
		process.OutputDataReceived += (_, e) =>
		{
			if (!string.IsNullOrWhiteSpace(e.Data))
				Common.Log("[Fabric Installer] " + e.Data, Common.LogLevel.Debug);
		};
		process.ErrorDataReceived += (_, e) =>
		{
			if (!string.IsNullOrWhiteSpace(e.Data))
				Common.Log("[Fabric Installer] " + e.Data, Common.LogLevel.Debug);
		};
		if (!process.Start())
			throw new Exception("无法启动 Fabric 安装器");
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		task.Progress = 0.5;
		if (!process.WaitForExit(600000))
		{
			try { process.Kill(true); } catch { }
			throw new TimeoutException("Fabric 安装器执行超时");
		}
		if (process.ExitCode != 0)
			throw new Exception("Fabric 安装器返回错误码 " + process.ExitCode);
	}

	#endregion

	#region 工具

	private static JsonNode? FetchJson(string url)
		=> Requester.FetchJson(url);

	private static bool IsStableVersion(string version)
	{
		var text = version.ToLowerInvariant();
		return !text.Contains("-pre") && !text.Contains("-rc") && !text.Contains("snapshot")
			&& !text.Contains("alpha") && !text.Contains("beta");
	}

	public static int InferJavaMajor(string mcVersion)
	{
		if (int.TryParse(mcVersion.Split('.', '-')[0], out var result) && result >= 26)
			return 25;
		if (McVersionComparer.CompareVersionGe(mcVersion, "1.20.5"))
			return 21;
		if (McVersionComparer.CompareVersionGe(mcVersion, "1.18"))
			return 17;
		if (McVersionComparer.CompareVersionGe(mcVersion, "1.17"))
			return 16;
		return 8;
	}

	#endregion
}
