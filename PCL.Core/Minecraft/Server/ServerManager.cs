using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PCL.Core.App;
using PCL.Core.Logging;

namespace PCL.Core.Minecraft.Server;

// Clock 新增：本地 Minecraft 服务器实例管理（原 PCL-CE 仅含服务器查询，不含实例管理与开服）。
// 移植自 klnalortk/Plain-Craft-Launcher-Clock，JSON 序列化由 Newtonsoft.Json 改写为 System.Text.Json。

public class ServerInfo
{
	public string Name { get; set; } = "新服务器";

	public string FolderName { get; set; } = "server";

	public string CoreType { get; set; } = "Vanilla";

	public string McVersion { get; set; } = "";

	public string JarFileName { get; set; } = "server.jar";

	public int MemoryMb { get; set; } = 2048;

	public string JvmArgs { get; set; } = "-XX:+UseG1GC -XX:+UseCompressedOops";

	public string ServerArgs { get; set; } = "nogui";

	public string JavaPath { get; set; } = "";

	public int JavaMajorVersion { get; set; }

	public bool EulaAccepted { get; set; }

	public bool AutoRestart { get; set; }

	public bool FrpAutoStart { get; set; }

	public string FrpExecutablePath { get; set; } = "";

	public string FrpConfigPath { get; set; } = "";

	public string FrpWorkingDirectory { get; set; } = "";

	public string FrpArguments { get; set; } = "";

	public DateTime CreatedAt { get; set; } = DateTime.Now;

	public DateTime? LastStartedAt { get; set; }

	public int? LastExitCode { get; set; }

	[JsonIgnore]
	public string ServerPath => Path.Combine(ServerManager.BasePath, FolderName);

	[JsonIgnore]
	public string JarPath => Path.Combine(ServerPath, JarFileName);

	[JsonIgnore]
	public string ConfigPath => Path.Combine(ServerPath, "server_config.json");

	[JsonIgnore]
	public string LogFolder => Path.Combine(ServerPath, "logs");

	[JsonIgnore]
	public string LatestLogPath => Path.Combine(LogFolder, "latest.log");

	[JsonIgnore]
	public string FrpLogPath => Path.Combine(LogFolder, "frp-latest.log");

	[JsonIgnore]
	public bool HasCoreFile => !string.IsNullOrWhiteSpace(JarFileName) && File.Exists(JarPath);

	[JsonIgnore]
	public string CoreDescription => string.IsNullOrWhiteSpace(McVersion) ? CoreType : (CoreType + " " + McVersion);

	public override string ToString()
	{
		return Name + " (" + FolderName + ")";
	}
}

public static class ServerManager
{
	private const string BaseFolderName = "PCLSERVER";

	private const string ConfigFileName = "server_config.json";

	private static string? _basePath;

	private static bool _initialized;

	public static string BasePath
	{
		get
		{
			if (string.IsNullOrWhiteSpace(_basePath))
			{
				_basePath = Path.Combine(Basics.ExecutableDirectory, "PCLSERVER");
			}
			return _basePath;
		}
	}

	public static List<ServerInfo> Servers { get; } = new List<ServerInfo>();

	public static ServerInfo? CurrentServer { get; set; }

	public static event Action? ServersChanged;

	public static void Initialize(bool forceReload = false)
	{
		if (!_initialized || forceReload)
		{
			_initialized = true;
			try
			{
				Directory.CreateDirectory(BasePath);
			}
			catch (Exception ex)
			{
				LogWrapper.Error(ex, "ServerManager", "无法创建服务器根目录：" + BasePath);
				return;
			}
			Reload();
		}
	}

	public static void Reload()
	{
		try
		{
			Directory.CreateDirectory(BasePath);
			string currentFolder = CurrentServer?.FolderName;
			Servers.Clear();
			string[] directories = Directory.GetDirectories(BasePath);
			foreach (string text in directories)
			{
				string fileName = Path.GetFileName(text);
				if (string.Equals(fileName, ".git", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				try
				{
					ServerInfo serverInfo = LoadServer(text, fileName);
					if (serverInfo != null)
					{
						Servers.Add(serverInfo);
					}
				}
				catch (Exception ex)
				{
					LogWrapper.Error(ex, "ServerManager", "加载服务器文件夹失败：" + text);
				}
			}
			Servers.Sort((ServerInfo a, ServerInfo b) =>
			{
				int num = a.CreatedAt.CompareTo(b.CreatedAt);
				return (num != 0) ? num : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
			});
			CurrentServer = Servers.FirstOrDefault((ServerInfo s) => string.Equals(s.FolderName, currentFolder, StringComparison.OrdinalIgnoreCase)) ?? Servers.FirstOrDefault();
			LogWrapper.Info("ServerManager", $"已加载 {Servers.Count} 个服务器实例");
			ServersChanged?.Invoke();
		}
		catch (Exception ex2)
		{
			LogWrapper.Error(ex2, "ServerManager", "扫描服务器目录失败");
		}
	}

	private static ServerInfo? LoadServer(string dir, string folderName)
	{
		string text = Path.Combine(dir, "server_config.json");
		ServerInfo serverInfo = null;
		if (File.Exists(text))
		{
			try
			{
				string value = File.ReadAllText(text);
				serverInfo = JsonSerializer.Deserialize<ServerInfo>(value);
			}
			catch (Exception ex)
			{
				LogWrapper.Error(ex, "ServerManager", "读取服务器配置失败：" + text);
			}
		}
		if (serverInfo == null)
		{
			serverInfo = new ServerInfo
			{
				Name = folderName,
				FolderName = folderName,
				CreatedAt = Directory.GetCreationTime(dir)
			};
		}
		serverInfo.FolderName = folderName;
		if (string.IsNullOrWhiteSpace(serverInfo.Name))
		{
			serverInfo.Name = folderName;
		}
		if (string.IsNullOrWhiteSpace(serverInfo.CoreType))
		{
			serverInfo.CoreType = "Vanilla";
		}
		string jvmArgs = serverInfo.JvmArgs;
		int memoryMb = serverInfo.MemoryMb;
		string jarFileName = serverInfo.JarFileName;
		string coreType = serverInfo.CoreType;
		NormalizeLegacyConfig(serverInfo);
		RefreshCoreFile(serverInfo);
		if (!File.Exists(text) || NeedsRewrite(text, serverInfo) || jvmArgs != serverInfo.JvmArgs || memoryMb != serverInfo.MemoryMb || jarFileName != serverInfo.JarFileName || coreType != serverInfo.CoreType)
		{
			SaveServerConfig(serverInfo, notify: false);
		}
		return serverInfo;
	}

	private static bool NeedsRewrite(string configPath, ServerInfo server)
	{
		try
		{
			return new FileInfo(configPath).Length < 20;
		}
		catch
		{
			return true;
		}
	}

	public static ServerInfo CreateServer(string name, string? folderName = null)
	{
		name = (string.IsNullOrWhiteSpace(name) ? "新服务器" : name.Trim());
		string baseName = (string.IsNullOrWhiteSpace(folderName) ? SanitizeFolderName(name) : SanitizeFolderName(folderName));
		string uniqueFolderName = GetUniqueFolderName(baseName);
		ServerInfo serverInfo = new ServerInfo
		{
			Name = name,
			FolderName = uniqueFolderName,
			CreatedAt = DateTime.Now
		};
		Directory.CreateDirectory(serverInfo.ServerPath);
		Directory.CreateDirectory(serverInfo.LogFolder);
		SaveServerConfig(serverInfo, notify: false);
		Servers.Add(serverInfo);
		CurrentServer = serverInfo;
		LogWrapper.Info("ServerManager", $"已创建服务器：{serverInfo.Name}（{serverInfo.FolderName}）");
		ServersChanged?.Invoke();
		return serverInfo;
	}

	public static void SaveServerConfig(ServerInfo server, bool notify = true)
	{
		try
		{
			Directory.CreateDirectory(server.ServerPath);
			string contents = JsonSerializer.Serialize(server, new JsonSerializerOptions { WriteIndented = true });
			string text = server.ConfigPath + ".tmp";
			File.WriteAllText(text, contents);
			File.Move(text, server.ConfigPath, overwrite: true);
			if (notify)
			{
				ServersChanged?.Invoke();
			}
		}
		catch (Exception ex)
		{
			LogWrapper.Error(ex, "ServerManager", "保存服务器配置失败：" + server.ConfigPath);
		}
	}

	public static bool DeleteServer(ServerInfo server, bool deleteFiles = true)
	{
		try
		{
			if (deleteFiles && Directory.Exists(server.ServerPath))
			{
				Directory.Delete(server.ServerPath, recursive: true);
			}
			Servers.Remove(server);
			if (CurrentServer == server)
			{
				CurrentServer = Servers.FirstOrDefault();
			}
			LogWrapper.Info("ServerManager", "已删除服务器：" + server.Name);
			ServersChanged?.Invoke();
			return true;
		}
		catch (Exception ex)
		{
			LogWrapper.Error(ex, "ServerManager", "删除服务器失败：" + server.ServerPath);
			return false;
		}
	}

	public static void RenameServer(ServerInfo server, string newName)
	{
		if (!string.IsNullOrWhiteSpace(newName))
		{
			server.Name = newName.Trim();
			SaveServerConfig(server);
		}
	}

	public static bool RefreshCoreFile(ServerInfo server)
	{
		if (!Directory.Exists(server.ServerPath))
		{
			return false;
		}
		string path = server.ServerPath + Path.DirectorySeparatorChar + server.JarFileName;
		if (!string.IsNullOrWhiteSpace(server.JarFileName) && File.Exists(path))
		{
			return true;
		}
		IEnumerable<string> coreCandidates = GetCoreCandidates(server.ServerPath);
		string text = coreCandidates.FirstOrDefault();
		if (text == null)
		{
			return false;
		}
		server.JarFileName = Path.GetFileName(text);
		string text2 = GuessCoreType(server.JarFileName);
		if (!string.IsNullOrEmpty(text2))
		{
			server.CoreType = text2;
		}
		return true;
	}

	private static IEnumerable<string> GetCoreCandidates(string path)
	{
		string[] preferred = new string[3] { "fabric-server-launch.jar", "quilt-server-launch.jar", "server.jar" };
		string[] array = preferred;
		foreach (string name in array)
		{
			string full = Path.Combine(path, name);
			if (File.Exists(full))
			{
				yield return full;
			}
		}
		string[] jars = (from f in Directory.GetFiles(path, "*.jar", SearchOption.TopDirectoryOnly)
			where !Path.GetFileName(f).Contains("installer", StringComparison.OrdinalIgnoreCase)
			select f).OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
		string[] array2 = jars;
		foreach (string jar in array2)
		{
			if (!MemoryExtensions.Contains(value: Path.GetFileName(jar), span: preferred, comparer: StringComparer.OrdinalIgnoreCase))
			{
				yield return jar;
			}
		}
	}

	public static string? DetectCoreType(string fileName)
	{
		return GuessCoreType(fileName);
	}

	private static string? GuessCoreType(string fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return null;
		}
		string text = fileName.ToLowerInvariant();
		if (text.StartsWith("paper"))
		{
			return "Paper";
		}
		if (text.StartsWith("purpur"))
		{
			return "Purpur";
		}
		if (text.StartsWith("folia"))
		{
			return "Folia";
		}
		if (text.StartsWith("fabric"))
		{
			return "Fabric";
		}
		if (text.StartsWith("quilt"))
		{
			return "Quilt";
		}
		if (text.StartsWith("neoforge"))
		{
			return "NeoForge";
		}
		if (text.StartsWith("forge"))
		{
			return "Forge";
		}
		if (text.StartsWith("minecraft_server") || text == "server.jar")
		{
			return "Vanilla";
		}
		return null;
	}

	private static void NormalizeLegacyConfig(ServerInfo server)
	{
		if (string.IsNullOrWhiteSpace(server.JarFileName))
		{
			server.JarFileName = "server.jar";
		}
		ServerInfo serverInfo = server;
		if (serverInfo.JvmArgs == null)
		{
			string text = (serverInfo.JvmArgs = "");
		}
		serverInfo = server;
		if (serverInfo.ServerArgs == null)
		{
			string text = (serverInfo.ServerArgs = "nogui");
		}
		serverInfo = server;
		if (serverInfo.JavaPath == null)
		{
			string text = (serverInfo.JavaPath = "");
		}
		serverInfo = server;
		if (serverInfo.FrpExecutablePath == null)
		{
			string text = (serverInfo.FrpExecutablePath = "");
		}
		serverInfo = server;
		if (serverInfo.FrpConfigPath == null)
		{
			string text = (serverInfo.FrpConfigPath = "");
		}
		serverInfo = server;
		if (serverInfo.FrpWorkingDirectory == null)
		{
			string text = (serverInfo.FrpWorkingDirectory = "");
		}
		serverInfo = server;
		if (serverInfo.FrpArguments == null)
		{
			string text = (serverInfo.FrpArguments = "");
		}
		Match match = Regex.Match(server.JvmArgs, "-Xmx(?<size>\\d+)(?<unit>[MmGg])");
		if (match.Success && int.TryParse(match.Groups["size"].Value, out var result))
		{
			result = (match.Groups["unit"].Value.Equals("G", StringComparison.OrdinalIgnoreCase) ? (result * 1024) : result);
			server.MemoryMb = Math.Clamp(result, 256, 65536);
			server.JvmArgs = Regex.Replace(server.JvmArgs, "-Xm[sx]\\d+[MmGg]\\s*", "").Trim();
		}
		if (server.MemoryMb <= 0)
		{
			server.MemoryMb = 2048;
		}
		if (string.IsNullOrWhiteSpace(server.JvmArgs))
		{
			server.JvmArgs = "-XX:+UseG1GC -XX:+UseCompressedOops";
		}
	}

	public static string GetUniqueFolderName(string baseName)
	{
		baseName = SanitizeFolderName(baseName);
		string name = baseName;
		int num = 1;
		while (Directory.Exists(Path.Combine(BasePath, name)) || Servers.Any((ServerInfo s) => string.Equals(s.FolderName, name, StringComparison.OrdinalIgnoreCase)))
		{
			name = $"{baseName}{num}";
			num++;
		}
		return name;
	}

	public static string SanitizeFolderName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return "server";
		}
		char[] invalid = Path.GetInvalidFileNameChars();
		string text = new string((from c in name.Trim()
			select invalid.Contains(c) ? '_' : c).ToArray());
		text = text.Replace(' ', '_').Trim(new char[2] { '_', '.' });
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "server";
		}
		if (text.Length > 64)
		{
			text = text.Substring(0, 64);
		}
		return text;
	}

	public static string GetLogPath(ServerInfo server)
	{
		Directory.CreateDirectory(server.LogFolder);
		return server.LatestLogPath;
	}
}
