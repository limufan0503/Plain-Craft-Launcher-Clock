using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.Minecraft.Server;

namespace PCL;

// Clock 新增：服务器管理左面板（服务器列表 / 新建服务器 / 下载服务端入口）。
// 纯代码构建 UI（Clock 原始 XAML 不可反编译），视觉后续接入 GlassStyle 增强。
public class PageServerLeft : Page
{
	private StackPanel _list = null!;
	private TextBlock _empty = null!;

	public PageServerLeft()
	{
		Background = Brushes.Transparent;
		var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 8, 12, 8) };
		var root = new StackPanel { Orientation = Orientation.Vertical };
		scroll.Content = root;

		root.Children.Add(new TextBlock { Text = "服务器", FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(4, 4, 0, 10) });

		var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
		btnRow.Children.Add(MakeButton("新建服务器", OnCreate));
		btnRow.Children.Add(MakeButton("下载服务端", OnDownload));
		root.Children.Add(btnRow);

		root.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 6), Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) });

		_empty = new TextBlock { Text = "还没有服务器，点击「新建服务器」开始", Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), Margin = new Thickness(4, 8, 0, 0) };
		root.Children.Add(_empty);

		_list = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 4, 0, 0) };
		root.Children.Add(_list);

		Content = scroll;
		Loaded += (s, e) => { ServerManager.Initialize(); Refresh(); };
		ServerManager.ServersChanged += () => Dispatcher.Invoke(Refresh);
	}

	private Button MakeButton(string text, RoutedEventHandler handler)
	{
		var b = new Button
		{
			Content = text,
			Margin = new Thickness(0, 0, 8, 0),
			Padding = new Thickness(14, 6, 14, 6),
			Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
			Foreground = Brushes.White,
			BorderThickness = new Thickness(0),
			Cursor = Cursors.Hand
		};
		b.Click += handler;
		return b;
	}

	public void Refresh()
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
			var item = new Border
			{
				Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
				CornerRadius = new CornerRadius(8),
				Margin = new Thickness(0, 0, 0, 8),
				Padding = new Thickness(12, 10, 12, 10),
				Cursor = Cursors.Hand
			};
			var sp = new StackPanel();
			sp.Children.Add(new TextBlock { Text = s.Name, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
			sp.Children.Add(new TextBlock { Text = s.CoreDescription, Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), FontSize = 12 });
			item.Child = sp;
			var captured = s;
			item.MouseLeftButtonUp += (sender, e) =>
			{
				ServerManager.CurrentServer = captured;
				Launcher.frmServerRight?.Refresh();
			};
			_list.Children.Add(item);
		}
	}

	private void OnCreate(object sender, RoutedEventArgs e)
	{
		var name = InputBox("新建服务器", "请输入服务器名称：", "新服务器");
		if (!string.IsNullOrWhiteSpace(name))
		{
			var s = ServerManager.CreateServer(name);
			ServerManager.CurrentServer = s;
			Refresh();
			Launcher.frmServerRight?.Refresh();
		}
	}

	private void OnDownload(object sender, RoutedEventArgs e)
	{
		// 下载服务端页将在下一阶段补充；此处先提示。
		MessageBox.Show("下载服务端（原版 / Paper / Purpur / Folia）功能将在下一版本接入。", "提示");
	}

	private string? InputBox(string title, string prompt, string defaultText)
	{
		var dlg = new Window
		{
			Title = title,
			Width = 360,
			Height = 170,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			ResizeMode = ResizeMode.NoResize,
			Background = new SolidColorBrush(Color.FromRgb(30, 30, 40))
		};
		var panel = new StackPanel { Margin = new Thickness(16) };
		var tb = new TextBox { Text = defaultText, Padding = new Thickness(6), Margin = new Thickness(0, 8, 0, 0) };
		var ok = new Button { Content = "确定", Width = 90, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
		string? result = null;
		ok.Click += (s, ev) => { result = tb.Text; dlg.DialogResult = true; };
		panel.Children.Add(new TextBlock { Text = prompt, Foreground = Brushes.White });
		panel.Children.Add(tb);
		panel.Children.Add(ok);
		dlg.Content = panel;
		dlg.Owner = Application.Current.MainWindow;
		if (dlg.ShowDialog() == true) return result;
		return null;
	}
}
