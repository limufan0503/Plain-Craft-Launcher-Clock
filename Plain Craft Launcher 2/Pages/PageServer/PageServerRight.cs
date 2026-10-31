using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.Minecraft.Server;

namespace PCL;

// Clock 新增：服务器管理右面板（详情 / 配置 / 启动 / 停止 / 打开目录 / 查看日志 / 删除）。
// 派生自 MyPageRight，以便 PageChangeAnim 能正确进行类型转换与切换动画。
// UI 采用与设置页一致的圆角卡片 + 主题色，开服走 ServerManager 的进程管理。
public class PageServerRight : MyPageRight
{
	private StackPanel _panel = null!;
	private ServerInfo? _current;

	public PageServerRight()
	{
		var scroll = new MyScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
		_panel = new StackPanel();
		scroll.Content = _panel;
		Child = scroll;
		PanScroll = scroll;
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

		// 标题 + 状态
		var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 14) };
		titleRow.Children.Add(Text(s.Name, 22, FontWeights.Bold));
		titleRow.Children.Add(StatusBadge(running));
		_panel.Children.Add(titleRow);

		// 操作卡片
		_panel.Children.Add(BuildActionCard(s, running));
		// 信息卡片
		_panel.Children.Add(BuildInfoCard(s));
		// 配置卡片
		_panel.Children.Add(BuildConfigCard(s));
	}

	public void RefreshStatus()
	{
		LoadServer(ServerManager.CurrentServer);
	}

	#region 卡片构建

	private Border BuildActionCard(ServerInfo s, bool running)
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal };

		if (running)
		{
			row.Children.Add(MakeButton("停止服务器", "ColorBrushRedDark", () =>
			{
				ServerManager.StopServer(s);
				LoadServer(s);
			}, 110));
		}
		else
		{
			row.Children.Add(MakeButton(s.HasCoreFile ? "启动服务器" : "请先下载核心", "ColorBrush2",
				() => Start(s), 110, s.HasCoreFile));
		}

		row.Children.Add(MakeButton("打开目录", "ColorBrush6", () => OpenFolder(s.ServerPath), 90));
		row.Children.Add(MakeButton("查看日志", "ColorBrush6", () => OpenLog(s), 90));
		return Card("操作", row);
	}

	private Border BuildInfoCard(ServerInfo s)
	{
		var sp = new StackPanel();
		sp.Children.Add(InfoRow("核心类型", string.IsNullOrWhiteSpace(s.CoreType) ? "未设置" : s.CoreType));
		sp.Children.Add(InfoRow("游戏版本", string.IsNullOrWhiteSpace(s.McVersion) ? "未设置" : s.McVersion));
		sp.Children.Add(InfoRow("核心文件", s.JarFileName + (s.HasCoreFile ? "（已就绪）" : "（缺失）")));
		sp.Children.Add(InfoRow("服务器目录", s.ServerPath));
		if (s.LastStartedAt.HasValue)
			sp.Children.Add(InfoRow("上次启动", s.LastStartedAt.Value.ToString("yyyy-MM-dd HH:mm")));
		return Card("基本信息", sp);
	}

	private Border BuildConfigCard(ServerInfo s)
	{
		var sp = new StackPanel();
		sp.Children.Add(EditRow("名称", s.Name, v => { s.Name = v; ServerManager.SaveServerConfig(s); }));
		sp.Children.Add(EditRow("内存 (MB)", s.MemoryMb.ToString(), v =>
		{
			if (int.TryParse(v, out var m) && m >= 256) { s.MemoryMb = m; ServerManager.SaveServerConfig(s); }
		}));
		sp.Children.Add(EditRow("JVM 参数", s.JvmArgs, v => { s.JvmArgs = v; ServerManager.SaveServerConfig(s); }));
		sp.Children.Add(EditRow("服务器参数", s.ServerArgs, v => { s.ServerArgs = v; ServerManager.SaveServerConfig(s); }));
		sp.Children.Add(EditRow("Java 路径", s.JavaPath, v => { s.JavaPath = v; ServerManager.SaveServerConfig(s); }));
		sp.Children.Add(Text("Java 路径留空则自动从 PATH 中查找；服务器参数默认 nogui。", 12, FontWeights.Normal, 0.6));

		var delBtn = MakeButton("删除服务器", "ColorBrushRedDark", () =>
		{
			if (MessageBox.Show($"确定删除服务器「{s.Name}」及其所有文件？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
			{
				ServerManager.DeleteServer(s);
				LoadServer(ServerManager.CurrentServer);
			}
		}, 110);
		delBtn.Margin = new Thickness(0, 10, 0, 0);
		sp.Children.Add(delBtn);

		return Card("配置", sp);
	}

	#endregion

	#region 启动 / 打开

	private void Start(ServerInfo s)
	{
		try
		{
			ServerManager.StartServer(s);
			LoadServer(s);
			MessageBox.Show($"服务器已启动（工作目录：{s.ServerPath}）。\n首次启动会自动同意 EULA 并生成服务器文件。", "启动成功", MessageBoxButton.OK, MessageBoxImage.Information);
		}
		catch (Exception ex)
		{
			MessageBox.Show("启动失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

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

	private static void OpenLog(ServerInfo s)
	{
		try
		{
			string log = s.LatestLogPath;
			if (!File.Exists(log))
			{
				MessageBox.Show("还没有服务器日志（服务器尚未运行过）。", "提示");
				return;
			}
			Process.Start(new ProcessStartInfo("notepad.exe", $"\"{log}\"") { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			MessageBox.Show("打开日志失败：" + ex.Message, "错误");
		}
	}

	#endregion

	#region UI 辅助

	private static Border Card(string? title, params UIElement[] children)
	{
		var border = new Border
		{
			CornerRadius = new CornerRadius(8),
			Margin = new Thickness(0, 0, 0, 12),
			Padding = new Thickness(16),
			BorderThickness = new Thickness(1)
		};
		border.SetResourceReference(Border.BackgroundProperty, "ColorBrush8");
		border.SetResourceReference(Border.BorderBrushProperty, "ColorBrush6");
		var sp = new StackPanel();
		if (!string.IsNullOrEmpty(title))
			sp.Children.Add(Text(title, 15, FontWeights.SemiBold, 1.0, new Thickness(0, 0, 0, 10)));
		foreach (var c in children)
			sp.Children.Add(c);
		border.Child = sp;
		return border;
	}

	private static TextBlock Text(string content, double fontSize = 13, FontWeight? weight = null, double opacity = 1.0, Thickness? margin = null)
	{
		var t = new TextBlock { Text = content, FontSize = fontSize, TextWrapping = TextWrapping.Wrap };
		if (weight.HasValue) t.FontWeight = weight.Value;
		if (margin.HasValue) t.Margin = margin.Value;
		t.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
		if (opacity < 1.0)
			t.Opacity = opacity;
		return t;
	}

	private static Border StatusBadge(bool running)
	{
		var b = new Border
		{
			CornerRadius = new CornerRadius(11),
			Padding = new Thickness(10, 3, 10, 3),
			Margin = new Thickness(10, 0, 0, 0),
			VerticalAlignment = VerticalAlignment.Center,
			Background = new SolidColorBrush(running ? Color.FromRgb(46, 160, 67) : Color.FromRgb(140, 140, 140))
		};
		b.Child = new TextBlock { Text = running ? "运行中" : "已停止", FontSize = 12, Foreground = Brushes.White };
		return b;
	}

	private static StackPanel InfoRow(string label, string value)
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
		row.Children.Add(Text(label + "：", 13, FontWeights.SemiBold));
		row.Children.Add(Text(value, 13));
		return row;
	}

	private static StackPanel EditRow(string label, string value, Action<string> onChanged)
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
		var lab = Text(label, 13, FontWeights.SemiBold, margin: new Thickness(0, 0, 8, 0));
		lab.Width = 96;
		lab.VerticalAlignment = VerticalAlignment.Center;
		row.Children.Add(lab);
		var tb = new TextBox
		{
			Text = value,
			Width = 320,
			Padding = new Thickness(6),
			VerticalContentAlignment = VerticalAlignment.Center
		};
		tb.SetResourceReference(TextBox.BackgroundProperty, "ColorBrush8");
		tb.SetResourceReference(TextBox.ForegroundProperty, "ColorBrush1");
		tb.SetResourceReference(TextBox.BorderBrushProperty, "ColorBrush6");
		tb.LostFocus += (_, _) => onChanged(tb.Text);
		row.Children.Add(tb);
		return row;
	}

	private static Button MakeButton(string text, string bgResource, Action onClick, double width, bool enabled = true)
	{
		var b = new Button
		{
			Content = text,
			Width = width,
			Margin = new Thickness(0, 0, 8, 0),
			Padding = new Thickness(0, 7, 0, 7),
			BorderThickness = new Thickness(0),
			Cursor = Cursors.Hand,
			IsEnabled = enabled
		};
		b.SetResourceReference(Button.BackgroundProperty, bgResource);
		b.Foreground = Brushes.White;
		b.Click += (_, _) => onClick();
		return b;
	}

	#endregion
}
