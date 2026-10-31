using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.Minecraft.Server;

namespace PCL;

// Clock 新增：服务器管理右面板（详情 / 内嵌控制台 / 启动停止重启 / 基本设置 / 拖拽安装 / 删除）。
// 派生自 MyPageRight，UI 使用 PCL 自带的 My* 圆角主题控件。
public class PageServerRight : MyPageRight
{
	private StackPanel _panel = null!;
	private ServerInfo? _current;
	private TextBox _console = null!;
	private MyTextBox _cmdInput = null!;
	private bool _loadingProps;

	private const int MaxConsoleChars = 20000;

	public PageServerRight()
	{
		var scroll = new MyScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
		_panel = new StackPanel();
		scroll.Content = _panel;
		Child = scroll;
		PanScroll = scroll;

		AllowDrop = true;
		DragOver += OnDragOver;
		Drop += OnDrop;

		Loaded += (_, _) => LoadServer(ServerManager.CurrentServer);
		ServerManager.ServersChanged += () => Dispatcher.Invoke(() => LoadServer(ServerManager.CurrentServer));
	}

	public void LoadServer(ServerInfo? s)
	{
		_current = s;
		_panel.Children.Clear();
		if (s == null)
		{
			_panel.Children.Add(Text("选择一个服务器以查看详情", 14));
			return;
		}

		bool running = ServerManager.IsRunning(s);

		var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 14) };
		titleRow.Children.Add(Text(s.Name, 22, FontWeights.Bold));
		titleRow.Children.Add(StatusBadge(running));
		_panel.Children.Add(titleRow);

		_panel.Children.Add(BuildActionCard(s, running));
		_panel.Children.Add(BuildConsoleCard(s, running));
		_panel.Children.Add(BuildBasicSettingsCard(s));
		_panel.Children.Add(BuildConfigCard(s));
	}

	public void RefreshStatus() => LoadServer(ServerManager.CurrentServer);

	#region 卡片

	private Border BuildActionCard(ServerInfo s, bool running)
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal };

		if (running)
		{
			row.Children.Add(ActionButton("重启服务器", MyButton.ColorState.Highlight, () => Restart(s)));
			row.Children.Add(ActionButton("停止服务器", MyButton.ColorState.Red, () => { ServerManager.StopServer(s); LoadServer(s); }));
		}
		else
		{
			row.Children.Add(ActionButton(s.HasCoreFile ? "启动服务器" : "请先下载核心", MyButton.ColorState.Normal, () => Start(s)));
		}

		row.Children.Add(ActionButton("打开文件夹", MyButton.ColorState.Normal, () => OpenFolder(s.ServerPath)));
		return Card("操作", row);
	}

	private Border BuildConsoleCard(ServerInfo s, bool running)
	{
		_console = new TextBox
		{
			IsReadOnly = true,
			TextWrapping = TextWrapping.NoWrap,
			Height = 200,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			FontFamily = new FontFamily("Consolas"),
			FontSize = 12,
			AcceptsReturn = true
		};
		_console.SetResourceReference(TextBox.BackgroundProperty, "ColorBrush7");
		_console.SetResourceReference(TextBox.ForegroundProperty, "ColorBrush1");
		_console.Text = running ? "服务器运行中，日志实时显示在下方…\n" : "服务器未运行。启动后日志将显示在这里。\n";

		var cmdRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
		_cmdInput = new MyTextBox { HintText = "输入控制台命令（如 stop、list、say …）", Margin = new Thickness(0, 0, 8, 0), Width = 320 };
		_cmdInput.KeyDown += (_, ev) => { if (ev.Key == Key.Enter && running) SendCommand(s); };
		var sendBtn = ActionButton("发送", MyButton.ColorState.Normal, () => SendCommand(s));
		sendBtn.Margin = new Thickness(0, 0, 0, 0);
		cmdRow.Children.Add(_cmdInput);
		cmdRow.Children.Add(sendBtn);

		return Card("控制台", _console, cmdRow);
	}

	private Border BuildBasicSettingsCard(ServerInfo s)
	{
		_loadingProps = true;
		var sp = new StackPanel();

		sp.Children.Add(ComboPropRow("游戏模式", new[] { ("生存", "survival"), ("创造", "creative"), ("冒险", "adventure"), ("旁观", "spectator") },
			ServerManager.GetServerProperty(s, "gamemode") ?? "survival", v => ServerManager.SetServerProperty(s, "gamemode", v)));
		sp.Children.Add(ComboPropRow("难度", new[] { ("和平", "peaceful"), ("简单", "easy"), ("普通", "normal"), ("困难", "hard") },
			ServerManager.GetServerProperty(s, "difficulty") ?? "easy", v => ServerManager.SetServerProperty(s, "difficulty", v)));
		sp.Children.Add(ComboPropRow("正版验证", new[] { ("开启", "true"), ("关闭", "false") },
			ServerManager.GetServerProperty(s, "online-mode") ?? "true", v => ServerManager.SetServerProperty(s, "online-mode", v)));
		sp.Children.Add(ComboPropRow("白名单", new[] { ("关闭", "false"), ("开启", "true") },
			ServerManager.GetServerProperty(s, "white-list") ?? "false", v => ServerManager.SetServerProperty(s, "white-list", v)));
		sp.Children.Add(ComboPropRow("PVP", new[] { ("开启", "true"), ("关闭", "false") },
			ServerManager.GetServerProperty(s, "pvp") ?? "true", v => ServerManager.SetServerProperty(s, "pvp", v)));
		sp.Children.Add(TextPropRow("服务器描述", ServerManager.GetServerProperty(s, "motd") ?? "A Minecraft Server", v => ServerManager.SetServerProperty(s, "motd", v)));
		sp.Children.Add(TextPropRow("端口", ServerManager.GetServerProperty(s, "server-port") ?? "25565", v => { if (int.TryParse(v, out _)) ServerManager.SetServerProperty(s, "server-port", v); }));
		sp.Children.Add(TextPropRow("最大玩家", ServerManager.GetServerProperty(s, "max-players") ?? "20", v => { if (int.TryParse(v, out _)) ServerManager.SetServerProperty(s, "max-players", v); }));

		sp.Children.Add(Text("以上为 server.properties 基础设置，重启服务器后生效。", 12, FontWeights.Normal, new Thickness(0, 6, 0, 0), 0.6));
		_loadingProps = false;
		return Card("基本设置", sp);
	}

	private Border BuildConfigCard(ServerInfo s)
	{
		var sp = new StackPanel();
		sp.Children.Add(TextPropRow("内存 (MB)", s.MemoryMb.ToString(), v => { if (int.TryParse(v, out var m) && m >= 256) { s.MemoryMb = m; ServerManager.SaveServerConfig(s); } }));
		sp.Children.Add(TextPropRow("JVM 参数", s.JvmArgs, v => { s.JvmArgs = v; ServerManager.SaveServerConfig(s); }));
		sp.Children.Add(TextPropRow("Java 路径", s.JavaPath, v => { s.JavaPath = v; ServerManager.SaveServerConfig(s); }));
		sp.Children.Add(Text("Java 路径留空则自动从 PATH 查找。", 12, FontWeights.Normal, new Thickness(0, 4, 0, 0), 0.6));

		var del = ActionButton("删除服务器", MyButton.ColorState.Red, () =>
		{
			if (MessageBox.Show($"确定删除服务器「{s.Name}」及其所有文件？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
			{
				ServerManager.DeleteServer(s);
				LoadServer(ServerManager.CurrentServer);
			}
		});
		del.Margin = new Thickness(0, 12, 0, 0);
		sp.Children.Add(del);

		return Card("高级配置", sp);
	}

	#endregion

	#region 启动 / 停止 / 重启 / 命令

	private void Start(ServerInfo s)
	{
		try
		{
			ServerManager.StartServer(s,
				onOutput: AppendConsole,
				onExit: () => Dispatcher.Invoke(() => { AppendConsole(">>> 服务器已退出"); LoadServer(s); }));
			AppendConsole(">>> 正在启动服务器…");
			LoadServer(s);
		}
		catch (Exception ex)
		{
			AppendConsole(">>> 启动失败：" + ex.Message);
			MessageBox.Show("启动失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	private async void Restart(ServerInfo s)
	{
		if (ServerManager.IsRunning(s))
		{
			ServerManager.SendCommand(s, "stop");
			for (int i = 0; i < 15 && ServerManager.IsRunning(s); i++)
				await System.Threading.Tasks.Task.Delay(200);
			if (ServerManager.IsRunning(s))
				ServerManager.StopServer(s);
			await System.Threading.Tasks.Task.Delay(500);
		}
		Start(s);
	}

	private void SendCommand(ServerInfo s)
	{
		if (!ServerManager.IsRunning(s))
		{
			AppendConsole(">>> 服务器未运行");
			return;
		}
		var cmd = _cmdInput.Text.Trim();
		if (string.IsNullOrEmpty(cmd))
			return;
		AppendConsole("> " + cmd);
		ServerManager.SendCommand(s, cmd);
		_cmdInput.Text = "";
	}

	private void AppendConsole(string line)
	{
		Dispatcher.Invoke(() =>
		{
			if (_console == null) return;
			_console.AppendText(line + "\r\n");
			if (_console.Text.Length > MaxConsoleChars)
				_console.Text = _console.Text.Substring(_console.Text.Length - MaxConsoleChars);
			_console.ScrollToEnd();
		});
	}

	#endregion

	#region 打开 / 拖拽

	private static void OpenFolder(string path)
	{
		try
		{
			if (Directory.Exists(path))
				Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			MessageBox.Show("打开目录失败：" + ex.Message, "错误");
		}
	}

	private void OnDragOver(object sender, DragEventArgs e)
	{
		e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
		e.Handled = true;
	}

	private void OnDrop(object sender, DragEventArgs e)
	{
		if (_current == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
		var files = e.Data.GetData(DataFormats.FileDrop) as string[];
		if (files == null || files.Length == 0) return;

		bool isPluginServer = _current.CoreType is "Paper" or "Purpur" or "Folia";
		var targetDir = isPluginServer ? Path.Combine(_current.ServerPath, "plugins") : Path.Combine(_current.ServerPath, "mods");
		Directory.CreateDirectory(targetDir);

		var copied = new List<string>();
		foreach (var f in files)
		{
			try
			{
				var ext = Path.GetExtension(f).ToLowerInvariant();
				if (ext != ".jar") continue;
				var dest = Path.Combine(targetDir, Path.GetFileName(f));
				File.Copy(f, dest, overwrite: true);
				copied.Add(Path.GetFileName(f));
			}
			catch (Exception ex)
			{
				AppendConsole(">>> 拖拽安装失败：" + ex.Message);
			}
		}

		if (copied.Count > 0)
		{
			AppendConsole($">>> 已安装 {copied.Count} 个文件到 {(isPluginServer ? "plugins" : "mods")}/：{string.Join(", ", copied)}");
			MessageBox.Show($"已安装 {copied.Count} 个文件到 {(isPluginServer ? "plugins" : "mods")} 文件夹。", "安装成功", MessageBoxButton.OK, MessageBoxImage.Information);
		}
		else if (files.Any(f => Path.GetExtension(f).ToLowerInvariant() == ".jar"))
		{
			MessageBox.Show("安装失败，请检查文件是否被占用。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	#endregion

	#region UI 辅助

	private static Border Card(string? title, params UIElement[] children)
	{
		var border = new Border { CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(16), BorderThickness = new Thickness(1) };
		border.SetResourceReference(Border.BackgroundProperty, "ColorBrush8");
		border.SetResourceReference(Border.BorderBrushProperty, "ColorBrush6");
		var sp = new StackPanel();
		if (!string.IsNullOrEmpty(title))
			sp.Children.Add(Text(title, 15, FontWeights.SemiBold, new Thickness(0, 0, 0, 10)));
		foreach (var c in children)
			sp.Children.Add(c);
		border.Child = sp;
		return border;
	}

	private static TextBlock Text(string content, double fontSize = 13, FontWeight? weight = null, Thickness? margin = null, double opacity = 1.0)
	{
		var t = new TextBlock { Text = content, FontSize = fontSize, TextWrapping = TextWrapping.Wrap };
		if (weight.HasValue) t.FontWeight = weight.Value;
		if (margin.HasValue) t.Margin = margin.Value;
		t.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
		t.Opacity = opacity;
		return t;
	}

	private static MyButton ActionButton(string text, MyButton.ColorState colorType, Action onClick)
	{
		var b = new MyButton { Text = text, ColorType = colorType, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
		b.Click += (_, _) => onClick();
		return b;
	}

	private static Border StatusBadge(bool running)
	{
		var b = new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(running ? Color.FromRgb(46, 160, 67) : Color.FromRgb(140, 140, 140)) };
		b.Child = new TextBlock { Text = running ? "运行中" : "已停止", FontSize = 12, Foreground = Brushes.White };
		return b;
	}

	private static StackPanel ComboPropRow(string label, (string display, string value)[] options, string currentValue, Action<string> onChanged)
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
		var lab = Text(label, 13, FontWeights.SemiBold, new Thickness(0, 0, 8, 0));
		lab.Width = 80;
		lab.VerticalAlignment = VerticalAlignment.Center;
		row.Children.Add(lab);
		var combo = new MyComboBox { Width = 220 };
		foreach (var (display, value) in options)
			combo.Items.Add(new MyComboBoxItem { Content = display, Tag = value });
		for (int i = 0; i < combo.Items.Count; i++)
			if (combo.Items[i] is MyComboBoxItem item && string.Equals(item.Tag?.ToString(), currentValue, StringComparison.OrdinalIgnoreCase))
			{ combo.SelectedIndex = i; break; }
		combo.SelectionChanged += (_, _) =>
		{
			if (combo.SelectedItem is MyComboBoxItem it && it.Tag is string v)
				onChanged(v);
		};
		row.Children.Add(combo);
		return row;
	}

	private static StackPanel TextPropRow(string label, string value, Action<string> onChanged)
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
		var lab = Text(label, 13, FontWeights.SemiBold, new Thickness(0, 0, 8, 0));
		lab.Width = 80;
		lab.VerticalAlignment = VerticalAlignment.Center;
		row.Children.Add(lab);
		var tb = new MyTextBox { Text = value, Width = 260 };
		tb.LostFocus += (_, _) => onChanged(tb.Text);
		row.Children.Add(tb);
		return row;
	}

	#endregion
}
