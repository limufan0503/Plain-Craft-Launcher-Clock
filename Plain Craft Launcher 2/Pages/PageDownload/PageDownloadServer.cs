using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PCL.Core.Minecraft.Server;

namespace PCL;

// Clock 新增：下载服务端右页面（选择核心类型 / 版本 / 目标服务器，一键下载并安装核心）。
// 派生自 MyPageRight 以适配 PageChangeAnim；下载任务走右下角下载中心（与其它下载一致）。
public class PageDownloadServer : MyPageRight
{
	private readonly List<ServerVersionEntry> _allVersions = new();
	private ServerCoreType _selectedType = ServerCoreType.Vanilla;
	private ServerVersionEntry? _selectedVersion;
	private bool _isLoadingVersions;
	private bool _isInstalling;
	private ServerInfo? _installedServer;
	private string _autoName = "";

	// UI 元素
	private StackPanel _panTypes = null!;
	private ComboBox _comboTarget = null!;
	private TextBox _txtNewName = null!;
	private TextBlock _hintTarget = null!;
	private TextBox _txtSearch = null!;
	private TextBlock _labVersionCount = null!;
	private StackPanel _panVersions = null!;
	private TextBlock _labLoading = null!;
	private MyButton _btnInstall = null!;
	private MyButton _btnOpenServer = null!;
	private TextBlock _labInstallStatus = null!;

	public PageDownloadServer()
	{
		var scroll = new MyScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
		var root = new StackPanel { Orientation = Orientation.Vertical };
		scroll.Content = root;
		Child = scroll;

		BuildTypeList(root);
		BuildTargetCard(root);
		BuildVersionCard(root);
		BuildActionCard(root);

		// 所有 UI 元素构建完成后再设置初始类型状态（否则会访问尚未初始化的控件导致崩溃）
		SelectType(ServerCoreType.Vanilla, loadVersions: false);

		PageEnter += () =>
		{
			ServerManager.Initialize();
			RefreshTargets();
			if (_allVersions.Count == 0 && !_isLoadingVersions)
				LoadVersionsAsync();
		};
		Loaded += (_, _) =>
		{
			if (_allVersions.Count == 0 && !_isLoadingVersions)
				LoadVersionsAsync();
		};
	}

	#region 构建 UI

	private static Border Card(string? title, params UIElement[] children)
	{
		var border = new Border
		{
			CornerRadius = new CornerRadius(8),
			Margin = new Thickness(0, 0, 0, 14),
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

	private void BuildTypeList(StackPanel root)
	{
		_panTypes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
		foreach (ServerCoreType type in Enum.GetValues<ServerCoreType>())
			_panTypes.Children.Add(MakeTypeButton(type));
		root.Children.Add(Card("服务端核心类型", _panTypes));
	}

	private MyButton MakeTypeButton(ServerCoreType type)
	{
		var btn = new MyButton
		{
			Text = ModServerDownload.GetTypeName(type),
			Margin = new Thickness(0, 0, 8, 0),
			Padding = new Thickness(14, 7, 14, 7),
			Cursor = Cursors.Hand,
			Tag = type,
			ToolTip = ModServerDownload.GetTypeDescription(type)
		};
		btn.Click += (_, _) => SelectType(type);
		StyleTypeButton(btn, type == _selectedType);
		return btn;
	}

	private void StyleTypeButton(MyButton btn, bool selected)
	{
		btn.ColorType = selected ? MyButton.ColorState.Highlight : MyButton.ColorState.Normal;
	}

	private void SelectType(ServerCoreType type, bool loadVersions = true)
	{
		_selectedType = type;
		foreach (var child in _panTypes.Children.OfType<MyButton>())
			StyleTypeButton(child, child.Tag is ServerCoreType t && t == type);
		_selectedVersion = null;
		_labInstallStatus.Text = "";
		_btnOpenServer.Visibility = Visibility.Collapsed;
		_txtSearch.Text = "";
		if (loadVersions)
			LoadVersionsAsync();
	}

	private void BuildTargetCard(StackPanel root)
	{
		_comboTarget = new MyComboBox { Margin = new Thickness(0, 0, 0, 8), Width = 300 };
		_comboTarget.SelectionChanged += (_, _) => UpdateTargetUi();

		_txtNewName = new MyTextBox { Width = 280, Margin = new Thickness(0, 0, 0, 8) };
		_txtNewName.TextChanged += (_, _) => UpdateTargetUi();

		_hintTarget = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.6 };
		_hintTarget.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");

		root.Children.Add(Card("安装到", _comboTarget, _txtNewName, _hintTarget));
	}

	private void BuildVersionCard(StackPanel root)
	{
		var topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
		_txtSearch = new MyTextBox { Width = 220 };
		_txtSearch.TextChanged += (_, _) => RebuildVersionList();
		var btnRefresh = new MyButton { Text = "刷新", ColorType = MyButton.ColorState.Normal, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6), Cursor = Cursors.Hand };
		btnRefresh.Click += (_, _) => LoadVersionsAsync();
		topRow.Children.Add(_txtSearch);
		topRow.Children.Add(btnRefresh);

		_labVersionCount = new TextBlock { FontSize = 12, Margin = new Thickness(0, 0, 0, 6), Opacity = 0.6 };
		_labVersionCount.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
		_labLoading = new TextBlock { Text = "加载中…", FontSize = 13, Margin = new Thickness(0, 4, 0, 4), Visibility = Visibility.Collapsed };
		_labLoading.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
		_panVersions = new StackPanel { Orientation = Orientation.Vertical };

		root.Children.Add(Card("选择版本", topRow, _labVersionCount, _labLoading, _panVersions));
	}

	private void BuildActionCard(StackPanel root)
	{
		_btnInstall = new MyButton
		{
			Text = "下载并安装服务端",
			ColorType = MyButton.ColorState.Normal,
			Padding = new Thickness(18, 9, 18, 9),
			Cursor = Cursors.Hand,
			IsEnabled = false
		};
		_btnInstall.Click += BtnInstall_Click;

		_btnOpenServer = new MyButton
		{
			Text = "前往服务器管理",
			ColorType = MyButton.ColorState.Highlight,
			Margin = new Thickness(8, 0, 0, 0),
			Padding = new Thickness(18, 9, 18, 9),
			Cursor = Cursors.Hand,
			Visibility = Visibility.Collapsed
		};
		_btnOpenServer.Click += (_, _) =>
		{
			if (_installedServer != null)
			{
				ServerManager.CurrentServer = _installedServer;
				Launcher.frmServerLeft?.SelectServerByFolder(_installedServer.FolderName);
				Launcher.frmServerRight?.LoadServer(_installedServer);
			}
			Launcher.frmMain?.PageChange(FormMain.PageType.Server);
		};

		_labInstallStatus = new TextBlock { FontSize = 13, Margin = new Thickness(0, 8, 0, 0) };
		_labInstallStatus.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");

		var row = new StackPanel { Orientation = Orientation.Horizontal };
		row.Children.Add(_btnInstall);
		row.Children.Add(_btnOpenServer);

		root.Children.Add(Card("", row, _labInstallStatus));
	}

	#endregion

	#region 目标服务器

	private void RefreshTargets()
	{
		var tag = (_comboTarget.SelectedItem as MyComboBoxItem)?.Tag as ServerInfo;
		var prevFolder = tag?.FolderName;
		_comboTarget.Items.Clear();
		_comboTarget.Items.Add(new MyComboBoxItem { Content = "新建服务器", Tag = null });
		foreach (var server in ServerManager.Servers)
			_comboTarget.Items.Add(new MyComboBoxItem { Content = "覆盖：" + server.Name, Tag = server });
		int idx = 0;
		if (prevFolder != null)
			for (int i = 1; i < _comboTarget.Items.Count; i++)
				if ((_comboTarget.Items[i] as MyComboBoxItem)?.Tag is ServerInfo s && string.Equals(s.FolderName, prevFolder, StringComparison.OrdinalIgnoreCase))
				{ idx = i; break; }
		_comboTarget.SelectedIndex = idx;
		UpdateTargetUi();
	}

	private void UpdateTargetUi()
	{
		bool isNew = _comboTarget.SelectedIndex <= 0;
		_txtNewName.IsEnabled = isNew;
		if (isNew)
		{
			_hintTarget.Text = "将为该服务端核心创建一个新的服务器实例。";
			if (string.IsNullOrWhiteSpace(_txtNewName.Text) || _txtNewName.Text == _autoName)
			{
				_autoName = ModServerDownload.GetTypeName(_selectedType) + (_selectedVersion != null ? "-" + _selectedVersion.Version : "");
				_txtNewName.Text = _autoName;
			}
		}
		else if (_comboTarget.SelectedItem is MyComboBoxItem { Tag: ServerInfo tag })
		{
			_hintTarget.Text = $"将覆盖服务器「{tag.Name}」的现有核心文件（{tag.JarFileName}）。";
		}
		_btnInstall.IsEnabled = !_isInstalling && _selectedVersion != null;
	}

	#endregion

	#region 版本列表

	private async void LoadVersionsAsync()
	{
		if (_isLoadingVersions) return;
		var type = _selectedType;
		_isLoadingVersions = true;
		_allVersions.Clear();
		_selectedVersion = null;
		_panVersions.Children.Clear();
		_labLoading.Visibility = Visibility.Visible;
		_panVersions.Visibility = Visibility.Collapsed;
		_btnInstall.IsEnabled = false;
		_labVersionCount.Text = "";
		try
		{
			var versions = await ModServerDownload.GetVersionsAsync(type);
			if (_selectedType == type)
			{
				_allVersions.AddRange(versions);
				RebuildVersionList();
			}
		}
		catch (Exception ex)
		{
			_labLoading.Text = "版本列表加载失败：" + ex.Message;
			_labLoading.Visibility = Visibility.Visible;
			_panVersions.Visibility = Visibility.Collapsed;
		}
		finally
		{
			_isLoadingVersions = false;
			if (_selectedType == type)
			{
				_labLoading.Visibility = Visibility.Collapsed;
				_panVersions.Visibility = Visibility.Visible;
			}
		}
	}

	private void RebuildVersionList()
	{
		_panVersions.Children.Clear();
		var filter = _txtSearch.Text.Trim();
		var list = string.IsNullOrEmpty(filter)
			? _allVersions
			: _allVersions.Where(v => v.Version.Contains(filter, StringComparison.OrdinalIgnoreCase)
				|| (v.Info?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
		_labVersionCount.Text = $"共 {list.Count} 个版本" + (string.IsNullOrEmpty(filter) ? "" : "（筛选后）");
		foreach (var item in list)
			_panVersions.Children.Add(MakeVersionItem(item));
		if (list.Count == 0)
			_panVersions.Children.Add(Text("没有匹配的版本", 13, null, new Thickness(4, 8, 0, 8), 0.6));
		_btnInstall.IsEnabled = !_isInstalling && _selectedVersion != null;
	}

	private Border MakeVersionItem(ServerVersionEntry entry)
	{
		bool selected = entry == _selectedVersion;
		var border = new Border
		{
			CornerRadius = new CornerRadius(7),
			Margin = new Thickness(0, 0, 0, 6),
			Padding = new Thickness(12, 8, 12, 8),
			Cursor = Cursors.Hand,
			Tag = entry,
			BorderThickness = new Thickness(selected ? 2 : 1)
		};
		border.SetResourceReference(Border.BackgroundProperty, selected ? "ColorBrush7" : "ColorBrush8");
		border.SetResourceReference(Border.BorderBrushProperty, selected ? "ColorBrush3" : "ColorBrush6");

		var sp = new StackPanel();
		var nameTb = new TextBlock { Text = entry.Version, FontWeight = FontWeights.SemiBold };
		nameTb.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
		sp.Children.Add(nameTb);
		if (!string.IsNullOrEmpty(entry.Info))
		{
			var infoTb = new TextBlock { Text = entry.Info + "  ·  Java " + entry.JavaMajorVersion, FontSize = 12, Margin = new Thickness(0, 2, 0, 0), Opacity = 0.6 };
			infoTb.SetResourceReference(TextBlock.ForegroundProperty, "ColorBrush1");
			sp.Children.Add(infoTb);
		}
		border.Child = sp;
		border.MouseLeftButtonUp += (_, _) =>
		{
			_selectedVersion = entry;
			HighlightSelectedVersion();
			_labInstallStatus.Text = "已选择：" + entry.Version;
			UpdateTargetUi();
		};
		return border;
	}

	private void HighlightSelectedVersion()
	{
		foreach (var child in _panVersions.Children.OfType<Border>())
		{
			bool selected = child.Tag is ServerVersionEntry e && e == _selectedVersion;
			child.SetResourceReference(Border.BackgroundProperty, selected ? "ColorBrush7" : "ColorBrush8");
			child.SetResourceReference(Border.BorderBrushProperty, selected ? "ColorBrush3" : "ColorBrush6");
			child.BorderThickness = new Thickness(selected ? 2 : 1);
		}
	}

	#endregion

	#region 安装

	private void BtnInstall_Click(object sender, MouseButtonEventArgs e)
	{
		if (_isInstalling || _selectedVersion == null) return;
		ServerInfo serverInfo;
		if (_comboTarget.SelectedIndex <= 0)
		{
			var name = string.IsNullOrWhiteSpace(_txtNewName.Text)
				? (ModServerDownload.GetTypeName(_selectedType) + "-" + _selectedVersion.Version)
				: _txtNewName.Text.Trim();
			try { serverInfo = ServerManager.CreateServer(name); }
			catch (Exception ex)
			{
				_labInstallStatus.Text = "创建服务器失败：" + ex.Message;
				return;
			}
		}
		else
		{
			if (_comboTarget.SelectedItem is not MyComboBoxItem { Tag: ServerInfo tag })
				return;
			serverInfo = tag;
		}

		_isInstalling = true;
		_installedServer = serverInfo;
		_btnInstall.IsEnabled = false;
		_btnOpenServer.Visibility = Visibility.Collapsed;
		_labInstallStatus.Text = "已加入下载中心，请在右下角查看进度";

		Loading.LoaderCombo<string> loaderCombo;
		try
		{
			loaderCombo = ModServerDownload.CreateInstallLoader(serverInfo, _selectedType, _selectedVersion.Version);
		}
		catch (Exception ex)
		{
			InstallFinished(false, ex.Message);
			return;
		}
		loaderCombo.OnStateChangedUi += InstallLoader_StateChanged;
		try
		{
			loaderCombo.Start();
			Loading.LoaderTaskbarAdd(loaderCombo);
			Launcher.frmMain?.BtnExtraDownload.ShowRefresh();
		}
		catch (Exception ex)
		{
			InstallFinished(false, ex.Message);
		}
	}

	private void InstallLoader_StateChanged(Loading.LoaderBase loader, Common.LoadState newState, Common.LoadState oldState)
	{
		switch (newState)
		{
			case Common.LoadState.Finished:
				InstallFinished(true, "");
				break;
			case Common.LoadState.Failed:
				InstallFinished(false, loader.Error?.Message ?? "安装失败");
				break;
			case Common.LoadState.Aborted:
				InstallFinished(false, "安装已取消");
				break;
		}
	}

	private void InstallFinished(bool success, string message)
	{
		_isInstalling = false;
		_btnInstall.IsEnabled = _selectedVersion != null;
		RefreshTargets();
		if (!success)
		{
			_labInstallStatus.Text = "安装失败：" + message;
			return;
		}
		if (_installedServer != null)
		{
			ServerManager.CurrentServer = _installedServer;
			ServerManager.Reload();
			Launcher.frmServerLeft?.RefreshList();
			Launcher.frmServerLeft?.EnsureSelection();
			Launcher.frmServerRight?.LoadServer(_installedServer);
			_labInstallStatus.Text = "安装成功：" + _installedServer.Name;
			_btnOpenServer.Visibility = Visibility.Visible;
		}
	}

	#endregion
}
