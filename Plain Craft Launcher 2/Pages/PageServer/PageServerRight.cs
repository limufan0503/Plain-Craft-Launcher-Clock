using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.Minecraft.Server;

namespace PCL;

// Clock 新增：服务器管理右面板（选中服务器的详情 / 配置 / 启动）。
// 派生自 MyPageRight，以便 PageChangeAnim 能正确进行类型转换与切换动画。
// 开服通过启动 java 进程实现（一键开服）。
public class PageServerRight : MyPageRight
{
	private StackPanel _panel = null!;

	public PageServerRight()
	{
		var scroll = new MyScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
		_panel = new StackPanel();
		scroll.Content = _panel;
		Child = scroll;
		PanScroll = scroll;
		Loaded += (s, e) => LoadServer(ServerManager.CurrentServer);
		ServerManager.ServersChanged += () => Dispatcher.Invoke(() => LoadServer(ServerManager.CurrentServer));
	}

	public void LoadServer(ServerInfo? s)
	{
		_panel.Children.Clear();
		if (s == null)
		{
			_panel.Children.Add(new TextBlock { Text = "选择一个服务器以查看详情", Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), FontSize = 14, Margin = new Thickness(4, 8, 0, 0) });
			return;
		}

		_panel.Children.Add(new TextBlock { Text = s.Name, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12) });
		_panel.Children.Add(Field("核心类型", s.CoreType));
		_panel.Children.Add(Field("游戏版本", string.IsNullOrEmpty(s.McVersion) ? "（未设置）" : s.McVersion));
		_panel.Children.Add(Field("核心文件", s.JarFileName + (s.HasCoreFile ? "  ✓ 已就绪" : "  ✗ 缺失")));

		_panel.Children.Add(EditRow("名称", s.Name, v => { s.Name = v; ServerManager.SaveServerConfig(s); }));
		_panel.Children.Add(EditRow("内存 (MB)", s.MemoryMb.ToString(), v => { if (int.TryParse(v, out var m)) { s.MemoryMb = m; ServerManager.SaveServerConfig(s); } }));
		_panel.Children.Add(EditRow("JVM 参数", s.JvmArgs, v => { s.JvmArgs = v; ServerManager.SaveServerConfig(s); }));
		_panel.Children.Add(EditRow("服务器参数", s.ServerArgs, v => { s.ServerArgs = v; ServerManager.SaveServerConfig(s); }));
		_panel.Children.Add(EditRow("Java 路径", s.JavaPath, v => { s.JavaPath = v; ServerManager.SaveServerConfig(s); }));

		var startBtn = new Button
		{
			Content = s.HasCoreFile ? "启动服务器" : "请先放入/下载核心文件",
			Margin = new Thickness(0, 12, 0, 0),
			Padding = new Thickness(16, 8, 16, 8),
			Background = new SolidColorBrush(Color.FromRgb(40, 140, 90)),
			Foreground = Brushes.White,
			BorderThickness = new Thickness(0),
			Cursor = Cursors.Hand,
			IsEnabled = s.HasCoreFile
		};
		startBtn.Click += (sender, e) => Start(s);
		_panel.Children.Add(startBtn);

		var delBtn = new Button
		{
			Content = "删除服务器",
			Margin = new Thickness(0, 8, 0, 0),
			Padding = new Thickness(16, 8, 16, 8),
			Background = new SolidColorBrush(Color.FromRgb(150, 50, 50)),
			Foreground = Brushes.White,
			BorderThickness = new Thickness(0),
			Cursor = Cursors.Hand
		};
		delBtn.Click += (sender, e) =>
		{
			if (MessageBox.Show("确定删除该服务器？", "确认", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
			{
				ServerManager.DeleteServer(s);
				LoadServer(ServerManager.CurrentServer);
			}
		};
		_panel.Children.Add(delBtn);
	}

	public void RefreshStatus()
	{
		LoadServer(ServerManager.CurrentServer);
	}

	private void Start(ServerInfo s)
	{
		if (!s.HasCoreFile)
		{
			MessageBox.Show("服务器目录下找不到核心 jar 文件，请先放入或下载服务端核心。", "无法启动");
			return;
		}
		try
		{
			var java = string.IsNullOrWhiteSpace(s.JavaPath) ? "java" : s.JavaPath;
			var args = $"-Xmx{s.MemoryMb}M {s.JvmArgs} -jar \"{s.JarPath}\" {s.ServerArgs}";
			var psi = new ProcessStartInfo(java, args) { WorkingDirectory = s.ServerPath, UseShellExecute = false };
			Process.Start(psi);
			s.LastStartedAt = DateTime.Now;
			ServerManager.SaveServerConfig(s);
			MessageBox.Show("服务器已启动（在独立窗口运行）。", "启动成功");
		}
		catch (Exception ex)
		{
			MessageBox.Show("启动失败：" + ex.Message, "错误");
		}
	}

	private static TextBlock Field(string label, string value)
	{
		return new TextBlock { Text = label + "：" + value, Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), Margin = new Thickness(0, 0, 0, 6) };
	}

	private static StackPanel EditRow(string label, string value, Action<string> onChanged)
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
		row.Children.Add(new TextBlock { Text = label, Width = 90, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
		var tb = new TextBox
		{
			Text = value,
			Width = 320,
			Padding = new Thickness(6),
			Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
			Foreground = Brushes.White,
			BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255))
		};
		tb.LostFocus += (s, e) => onChanged(tb.Text);
		row.Children.Add(tb);
		return row;
	}
}
