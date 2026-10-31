using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.Minecraft.Server;

namespace PCL;

// Clock 新增：服务器管理左面板（服务器列表 / 新建服务器 / 下载服务端入口）。
// 派生自 MyPageLeft，以便 PageChangeAnim 能正确进行类型转换与切换动画。
public class PageServerLeft : MyPageLeft
{
	private StackPanel _list = null!;
	private TextBlock _empty = null!;

	public PageServerLeft()
	{
		var scroll = new MyScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 8, 12, 8) };
		var root = new StackPanel { Orientation = Orientation.Vertical };
		scroll.Content = root;
		Children.Add(scroll);

		var title = new TextBlock { Text = "服务器", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 4, 0, 10) };
		title.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
		root.Children.Add(title);

		var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
		btnRow.Children.Add(MakeButton("新建服务器", OnCreate));
		btnRow.Children.Add(MakeButton("下载服务端", OnDownload));
		root.Children.Add(btnRow);

		var sep = new Separator { Margin = new Thickness(0, 10, 0, 6) };
		sep.SetResourceReference(Separator.BackgroundProperty, "ColorBrush6");
		root.Children.Add(sep);

		_empty = new TextBlock { Text = "还没有服务器，点击「新建服务器」开始", Margin = new Thickness(4, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
		_empty.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
		_empty.Opacity = 0.6;
		root.Children.Add(_empty);

		_list = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 4, 0, 0) };
		root.Children.Add(_list);

		Loaded += (_, _) =>
		{
			ServerManager.Initialize();
			RefreshList();
			EnsureSelection();
		};
		ServerManager.ServersChanged += OnServersChanged;
	}

	private void OnServersChanged()
	{
		Dispatcher.Invoke(RefreshList);
		Dispatcher.Invoke(EnsureSelection);
	}

	private static MyButton MakeButton(string text, Action onClick)
	{
		var b = new MyButton
		{
			Text = text,
			ColorType = MyButton.ColorState.Normal,
			Padding = new Thickness(14, 6, 14, 6),
			Margin = new Thickness(0, 0, 8, 0),
			Cursor = Cursors.Hand
		};
		b.Click += (_, _) => onClick();
		return b;
	}

	public void RefreshList()
	{
		_list.Children.Clear();
		if (ServerManager.Servers.Count == 0)
		{
			_empty.Visibility = Visibility.Visible;
			return;
		}
		_empty.Visibility = Visibility.Collapsed;
		foreach (var s in ServerManager.Servers)
		{
			bool selected = ReferenceEquals(s, ServerManager.CurrentServer);
			var item = new Border
			{
				CornerRadius = new CornerRadius(8),
				Margin = new Thickness(0, 0, 0, 8),
				Padding = new Thickness(12, 10, 12, 10),
				Cursor = Cursors.Hand,
				BorderThickness = new Thickness(selected ? 2 : 1)
			};
			item.SetResourceReference(Border.BackgroundProperty, selected ? "ColorBrush7" : "ColorBrush8");
			item.SetResourceReference(Border.BorderBrushProperty, selected ? "ColorBrush3" : "ColorBrush6");

			var sp = new StackPanel();
			var nameTb = new TextBlock { Text = s.Name, FontWeight = FontWeights.SemiBold };
			nameTb.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
			var infoTb = new TextBlock { Text = GetServerInfoText(s), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
			infoTb.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrushGray3");
			sp.Children.Add(nameTb);
			sp.Children.Add(infoTb);
			item.Child = sp;

			var captured = s;
			item.MouseLeftButtonUp += (_, _) =>
			{
				ServerManager.CurrentServer = captured;
				RefreshList();
				Launcher.frmServerRight?.LoadServer(captured);
			};
			_list.Children.Add(item);
		}
	}

	private static string GetServerInfoText(ServerInfo server)
	{
		string state;
		if (ServerManager.IsRunning(server))
			state = "运行中";
		else if (server.HasCoreFile)
			state = "已停止";
		else
			state = "无核心文件";
		return string.IsNullOrWhiteSpace(server.McVersion)
			? $"{state} · {server.CoreType}"
			: $"{state} · {server.CoreType} {server.McVersion}";
	}

	public void EnsureSelection()
	{
		var server = ServerManager.CurrentServer;
		if (server == null || !ServerManager.Servers.Contains(server))
			server = ServerManager.Servers.Count > 0 ? ServerManager.Servers[0] : null;
		if (server == null)
		{
			Launcher.frmServerRight?.LoadServer(null);
			return;
		}
		ServerManager.CurrentServer = server;
		Launcher.frmServerRight?.LoadServer(server);
	}

	public void SelectServerByFolder(string folderName)
	{
		var server = ServerManager.Servers.Find(s => string.Equals(s.FolderName, folderName, StringComparison.OrdinalIgnoreCase));
		if (server != null)
		{
			ServerManager.CurrentServer = server;
			RefreshList();
			SelectServer(server);
		}
	}

	private void SelectServer(ServerInfo server)
	{
		ServerManager.CurrentServer = server;
		Launcher.frmServerRight?.LoadServer(server);
	}

	private void OnCreate()
	{
		var name = InputBox("新建服务器", "请输入服务器名称：", "新服务器");
		if (!string.IsNullOrWhiteSpace(name))
		{
			var s = ServerManager.CreateServer(name);
			ServerManager.CurrentServer = s;
			RefreshList();
			Launcher.frmServerRight?.LoadServer(s);
		}
	}

	private void OnDownload()
	{
		Launcher.frmMain?.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadServer);
	}

	private static string? InputBox(string title, string prompt, string defaultText)
	{
		var dlg = new Window
		{
			Title = title,
			Width = 380,
			Height = 180,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			ResizeMode = ResizeMode.NoResize,
			ShowInTaskbar = false
		};
		dlg.SetResourceReference(Window.BackgroundProperty, "ColorBrush8");

		var panel = new StackPanel { Margin = new Thickness(16) };
		var promptTb = new TextBlock { Text = prompt };
		promptTb.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
		var tb = new TextBox { Text = defaultText, Padding = new Thickness(6), Margin = new Thickness(0, 10, 0, 0) };
		tb.SetResourceReference(TextBox.BackgroundProperty, "ColorBrush7");
		tb.SetResourceReference(TextBox.ForegroundProperty, "ColorBrush1");
		tb.SetResourceReference(TextBox.BorderBrushProperty, "ColorBrush6");
		var ok = new Button { Content = "确定", Width = 90, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, BorderThickness = new Thickness(0) };
		ok.SetResourceReference(Button.BackgroundProperty, "ColorBrush2");
		ok.Foreground = Brushes.White;
		string? result = null;
		ok.Click += (_, _) => { result = tb.Text; dlg.DialogResult = true; };
		tb.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) { result = tb.Text; dlg.DialogResult = true; } };
		panel.Children.Add(promptTb);
		panel.Children.Add(tb);
		panel.Children.Add(ok);
		dlg.Content = panel;
		dlg.Owner = Application.Current.MainWindow;
		return dlg.ShowDialog() == true ? result : null;
	}
}
