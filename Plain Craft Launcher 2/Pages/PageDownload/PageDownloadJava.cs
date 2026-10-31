using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PCL;

// Clock 新增：下载 Java 页面。提供 Java 8/17/21/25 一键下载，
// 复用 PCL 内置的 JavaRuntime.GetJavaDownloadLoader()（Mojang Java 运行时 + BMCLAPI 镜像），走右下角下载中心。
public class PageDownloadJava : MyPageRight
{
	// (主版本, Mojang java-runtime 组件名, 适用说明)
	private static readonly (int Major, string Component, string Desc)[] JavaVersions =
	[
		(8, "jre-legacy", "旧版本游戏（1.12 及更早）与旧模组"),
		(17, "java-runtime-gamma", "1.17 ~ 1.20.4 的主流版本"),
		(21, "java-runtime-delta", "1.20.5 及更高版本推荐"),
		(25, "java-runtime-epsilon", "最新版，适合最新游戏与服务器"),
	];

	public PageDownloadJava()
	{
		var scroll = new MyScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
		var root = new StackPanel { Orientation = Orientation.Vertical };
		scroll.Content = root;
		Child = scroll;

		root.Children.Add(Text("下载 Java", 22, FontWeights.Bold, new Thickness(0, 0, 0, 6)));
		root.Children.Add(Text("Java 是运行 Minecraft 游戏与服务器的必需组件。选择需要的版本即可一键下载，完成后会自动加入启动器的 Java 列表。", 13, FontWeights.Normal, new Thickness(0, 0, 0, 14), 0.7));

		foreach (var (major, component, desc) in JavaVersions)
			root.Children.Add(BuildVersionCard(major, component, desc));

		root.Children.Add(Text("提示：下载的 Java 会保存到 .minecraft\\runtime 目录。服务器可在「服务器 → 高级配置 → Java 路径」中手动指定对应版本的 java.exe。", 12, FontWeights.Normal, new Thickness(0, 4, 0, 0), 0.55));
	}

	private Border BuildVersionCard(int major, string component, string desc)
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal };

		var info = new StackPanel { Width = 280, VerticalAlignment = VerticalAlignment.Center };
		info.Children.Add(Text($"Java {major}", 16, FontWeights.Bold));
		info.Children.Add(Text(desc, 12, FontWeights.Normal, new Thickness(0, 2, 0, 0), 0.6));
		row.Children.Add(info);

		var btn = new MyButton
		{
			Text = "下载",
			ColorType = MyButton.ColorState.Normal,
			Padding = new Thickness(20, 8, 20, 8),
			Cursor = Cursors.Hand,
			VerticalAlignment = VerticalAlignment.Center
		};
		btn.Click += (_, _) => DownloadJava(major, component, btn);
		row.Children.Add(btn);

		return Card(null, row);
	}

	private void DownloadJava(int major, string component, MyButton btn)
	{
		string runtimeDir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			".minecraft", "runtime", component);
		string javaExe = Path.Combine(runtimeDir, "bin", "java.exe");

		if (File.Exists(javaExe))
		{
			MessageBox.Show(
				$"Java {major} 已经下载完成，位于：\n{runtimeDir}",
				"已下载", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}

		try
		{
			var loaderCombo = JavaRuntime.GetJavaDownloadLoader();
			btn.IsEnabled = false;
			loaderCombo.OnStateChangedUi += (loader, newState, oldState) =>
			{
				Dispatcher.Invoke(() =>
				{
					if (newState == Common.LoadState.Finished)
					{
						btn.IsEnabled = true;
						MessageBox.Show(
							$"Java {major} 下载完成！\n位置：{runtimeDir}\n\n现在可在「设置 → Java」中看到并使用它。",
							"下载完成", MessageBoxButton.OK, MessageBoxImage.Information);
					}
					else if (newState is Common.LoadState.Failed or Common.LoadState.Aborted)
					{
						btn.IsEnabled = true;
					}
				});
			};
			loaderCombo.Start(component, true);
			Loading.LoaderTaskbarAdd(loaderCombo);
			Launcher.frmMain?.BtnExtraDownload.ShowRefresh();
		}
		catch (Exception ex)
		{
			btn.IsEnabled = true;
			MessageBox.Show("下载失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

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
}
