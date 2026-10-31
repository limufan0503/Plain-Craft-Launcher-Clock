using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PCL.Core.App;
using PCL.Core.Logging;
using PCL.Core.Utils.OS;

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

	/// <summary>最近一次由启动器启动的服务器进程 PID（用于重启启动器后恢复运行状态 / 检测残留进程）。</summary>
	public int Pid { get; set; }

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
			// Clock: 重启启动器后恢复运行状态——接管记录 PID 仍存活的 java 进程
			foreach (ServerInfo s in Servers)
				TryAdoptRecordedProcess(s);
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

	#region server.properties 读写（Clock 新增）

	public static string ServerPropertiesPath(ServerInfo server)
		=> Path.Combine(server.ServerPath, "server.properties");

	/// <summary>读取 server.properties 中的某个键值（不存在返回 null）。</summary>
	public static string? GetServerProperty(ServerInfo server, string key)
	{
		string path = ServerPropertiesPath(server);
		if (!File.Exists(path))
			return null;
		foreach (string line in File.ReadAllLines(path))
		{
			string trimmed = line.Trim();
			if (trimmed.StartsWith("#") || !trimmed.Contains('='))
				continue;
			int idx = trimmed.IndexOf('=');
			string k = trimmed.Substring(0, idx).Trim();
			string v = trimmed.Substring(idx + 1).Trim();
			if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
				return v;
		}
		return null;
	}

	/// <summary>写入 server.properties 中的某个键值（不存在则追加）。</summary>
	public static void SetServerProperty(ServerInfo server, string key, string value)
	{
		string path = ServerPropertiesPath(server);
		var lines = File.Exists(path) ? new List<string>(File.ReadAllLines(path)) : new List<string>();
		bool found = false;
		for (int i = 0; i < lines.Count; i++)
		{
			string trimmed = lines[i].Trim();
			if (trimmed.StartsWith("#") || !trimmed.Contains('='))
				continue;
			int idx = trimmed.IndexOf('=');
			string k = trimmed.Substring(0, idx).Trim();
			if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
			{
				lines[i] = key + "=" + value;
				found = true;
				break;
			}
		}
		if (!found)
			lines.Add(key + "=" + value);
		File.WriteAllLines(path, lines);
	}

	#endregion

	#region 进程管理（Clock 新增）

	private static readonly Dictionary<string, Process> RunningProcesses = new();

	private static readonly Dictionary<string, StringBuilder> ServerLogs = new();
	private const int MaxServerLogChars = 100000;

	private static void AppendServerLog(ServerInfo server, string line)
	{
		lock (ServerLogs)
		{
			if (!ServerLogs.TryGetValue(server.FolderName, out var sb))
			{
				sb = new StringBuilder();
				ServerLogs[server.FolderName] = sb;
			}
			sb.AppendLine(line);
			if (sb.Length > MaxServerLogChars)
				sb.Remove(0, sb.Length - MaxServerLogChars);
		}
	}

	/// <summary>获取某服务器缓存在内存中的控制台日志（供内嵌控制台与网页面板查询）。</summary>
	public static string GetServerLog(ServerInfo server)
	{
		lock (ServerLogs)
		{
			return ServerLogs.TryGetValue(server.FolderName, out var sb) ? sb.ToString() : "";
		}
	}

	/// <summary>服务器是否正在运行。</summary>
	public static bool IsRunning(ServerInfo server)
		=> RunningProcesses.TryGetValue(server.FolderName, out var p) && !p.HasExited;

	/// <summary>获取正在运行的服务器进程（未运行则返回 null）。</summary>
	public static Process? GetRunningProcess(ServerInfo server)
		=> RunningProcesses.TryGetValue(server.FolderName, out var p) && !p.HasExited ? p : null;

	/// <summary>
	/// 确保已同意 Minecraft EULA。Minecraft 服务器首次启动若 eula=false 会立即退出，
	/// 这是「刚启动控制台就消失」的常见原因，这里在开服前自动写入 eula=true。
	/// </summary>
	public static void EnsureEulaAccepted(ServerInfo server)
	{
		try
		{
			string eulaPath = Path.Combine(server.ServerPath, "eula.txt");
			bool accepted = false;
			if (File.Exists(eulaPath))
			{
				string content = File.ReadAllText(eulaPath);
				accepted = Regex.IsMatch(content, @"eula\s*=\s*true", RegexOptions.IgnoreCase);
			}
			if (!accepted)
			{
				File.WriteAllText(eulaPath,
					"# 本文件由 Clock 启动器自动生成，表示你已阅读并同意 Minecraft EULA\n" +
					"# 原文：https://aka.ms/MinecraftEULA\n" +
					"eula=true\n");
			}
			if (!server.EulaAccepted)
			{
				server.EulaAccepted = true;
				SaveServerConfig(server, notify: false);
			}
		}
		catch (Exception ex)
		{
			LogWrapper.Error(ex, "ServerManager", "写入 eula.txt 失败");
		}
	}

	/// <summary>
	/// 启动服务器（内嵌控制台：重定向 stdout/stderr 到回调，stdin 支持命令输入）。
	/// 返回启动的进程对象；抛异常表示启动失败。
	/// onOutput/onExit 在后台线程回调，UI 层需自行 Dispatcher.Invoke。
	/// </summary>
	public static Process StartServer(ServerInfo server, out bool adopted, Action<string>? onOutput = null, Action? onExit = null)
	{
		adopted = false;
		if (IsRunning(server))
			throw new InvalidOperationException("服务器已在运行中");

		// Clock: 启动前检测残留 java 进程（上次启动器退出时未清理的孤儿进程）。
		// 若存在则接管而不是重复启动——否则新进程会因 world/session.lock 目录锁被旧进程占用而立即崩溃退出（“莫名停止”）。
		Process? residual = FindResidualProcess(server);
		if (residual != null)
		{
			AdoptProcess(server, residual, onExit);
			adopted = true;
			server.LastStartedAt = DateTime.Now;
			server.LastExitCode = null;
			SaveServerConfig(server, notify: false);
			LogWrapper.Info("ServerManager", $"已接管运行中的服务器进程：{server.Name}（PID {residual.Id}）");
			return residual;
		}

		if (!server.HasCoreFile)
			throw new InvalidOperationException("服务器核心文件不存在，请先下载服务端核心");
		EnsureEulaAccepted(server);

		string java = string.IsNullOrWhiteSpace(server.JavaPath) ? "java" : server.JavaPath;
		string args = $"-Xmx{server.MemoryMb}M {server.JvmArgs} -jar \"{server.JarPath}\" {server.ServerArgs}";
		var psi = new ProcessStartInfo(java, args)
		{
			WorkingDirectory = server.ServerPath,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8,
			StandardInputEncoding = Encoding.UTF8
		};
		var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
		process.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) { AppendServerLog(server, e.Data); onOutput?.Invoke(e.Data); } };
		process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) { AppendServerLog(server, e.Data); onOutput?.Invoke(e.Data); } };
		process.Exited += (_, _) =>
		{
			try { server.LastExitCode = process.ExitCode; } catch { }
			server.Pid = 0;
			RunningProcesses.Remove(server.FolderName);
			SaveServerConfig(server, notify: false);
			onExit?.Invoke();
		};
		if (!process.Start())
			throw new InvalidOperationException("无法启动 java 进程，请检查 Java 路径是否正确");
		try
		{
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
		}
		catch { }
		RunningProcesses[server.FolderName] = process;
		server.LastStartedAt = DateTime.Now;
		server.LastExitCode = null;
		server.Pid = process.Id;
		SaveServerConfig(server, notify: false);
		return process;
	}

	/// <summary>
	/// 扫描所有 java/javaw 进程，返回命令行中同时包含该服务器 jar 文件名与文件夹名的残留进程（可能为 null）。
	/// </summary>
	public static Process? FindResidualProcess(ServerInfo server)
	{
		try
		{
			foreach (string name in new[] { "java", "javaw" })
			{
				Process[] procs;
				try { procs = Process.GetProcessesByName(name); }
				catch { continue; }
				foreach (Process p in procs)
				{
					try
					{
						string? cmd = ProcessInterop.GetCommandLine(p.Id);
						if (string.IsNullOrEmpty(cmd))
							continue;
						if (cmd.Contains(server.JarFileName, StringComparison.OrdinalIgnoreCase)
							&& cmd.Contains(server.FolderName, StringComparison.OrdinalIgnoreCase))
							return p;
					}
					catch { }
				}
			}
		}
		catch { }
		return null;
	}

	/// <summary>接管一个已存在的服务器进程（加入运行字典并监听退出），用于恢复运行状态。</summary>
	private static void AdoptProcess(ServerInfo server, Process process, Action? onExit)
	{
		process.EnableRaisingEvents = true;
		process.Exited += (_, _) =>
		{
			try { server.LastExitCode = process.ExitCode; } catch { }
			server.Pid = 0;
			RunningProcesses.Remove(server.FolderName);
			SaveServerConfig(server, notify: false);
			onExit?.Invoke();
		};
		RunningProcesses[server.FolderName] = process;
		server.Pid = process.Id;
	}

	/// <summary>根据记录在配置中的 PID 尝试接管仍存活的进程（用于重启启动器后恢复“运行中”状态）。</summary>
	private static void TryAdoptRecordedProcess(ServerInfo server)
	{
		if (server.Pid <= 0)
			return;
		try
		{
			Process p = Process.GetProcessById(server.Pid);
			if (p.HasExited || (p.ProcessName != "java" && p.ProcessName != "javaw"))
			{
				server.Pid = 0;
				return;
			}
			AdoptProcess(server, p, onExit: null);
			LogWrapper.Info("ServerManager", $"恢复运行状态：{server.Name}（PID {server.Pid}）");
		}
		catch
		{
			server.Pid = 0;
		}
	}

	/// <summary>向正在运行的服务器发送控制台命令（如 stop、say 等）。</summary>
	public static void SendCommand(ServerInfo server, string command)
	{
		if (string.IsNullOrWhiteSpace(command))
			return;
		if (RunningProcesses.TryGetValue(server.FolderName, out var p) && !p.HasExited)
		{
			try
			{
				p.StandardInput.WriteLine(command);
				p.StandardInput.Flush();
			}
			catch { }
		}
	}

	/// <summary>停止服务器。</summary>
	public static void StopServer(ServerInfo server)
	{
		// Clock: 优先通过控制台发 stop 优雅关闭，失败再强杀（entireProcessTree 确保子进程一并清理）
		bool killed = false;
		if (RunningProcesses.TryGetValue(server.FolderName, out var p) && !p.HasExited)
		{
			try { p.StandardInput.WriteLine("stop"); p.StandardInput.Flush(); } catch { }
			try { if (!p.WaitForExit(5000)) { p.Kill(entireProcessTree: true); killed = true; } } catch { }
		}
		// 兜底：扫描并强杀残留 java 进程（可能字典里没有但进程仍存活）
		Process? residual = FindResidualProcess(server);
		if (residual != null)
		{
			try { residual.Kill(entireProcessTree: true); killed = true; } catch { }
		}
		RunningProcesses.Remove(server.FolderName);
		server.Pid = 0;
		SaveServerConfig(server, notify: false);
		if (killed)
			LogWrapper.Info("ServerManager", $"已强制停止服务器进程：{server.Name}");
	}

	#endregion
}
