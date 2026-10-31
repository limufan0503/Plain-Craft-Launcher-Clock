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
// 派生自 MyPageRight 以适配 PageChangeAnim；UI 采用标准 WPF 控件并套用主题色卡片。
public class PageDownloadServer : MyPageRight
{
	private readonly List<ServerVersionEntry> _allVersions = new();
	private ServerCoreType _selectedType = ServerCoreType.Vanilla;
	private ServerVersionEntry? _selectedVersion;
	private bool _isLoadingVersions;
	private bool _isInstalling;
	private ServerInfo? _installedServer;

	// UI 元素
	private StackPanel _panTypes = null!;
	private ComboBox _comboTarget = null!;
	private TextBox _txtNewName = null!;
	private TextBlock _hintTarget = null!;
	private TextBox _txtSearch = null!;
	private TextBlock _labVersionCount = null!;
	private StackPanel _panVersions = null!;
	private TextBlock _labLoading = null!;
	private Button _btnInstall = null!;
	private Button _btnOpenServer = null!;
	private ProgressBar _progInstall = null!;
	private TextBlock _labInstallStatus = null!;

	public PageDownloadServer()
	{
		var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
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

	private static Border Card(string title, params UIElement[] children)
	{
		var border = new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
			CornerRadius = new CornerRadius(10),
			Padding = new Thickness(14),
			Margin = new Thickness(0, 0, 0, 14)
		};
		var sp = new StackPanel();
		if (!string.IsNullOrEmpty(title))
			sp.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 10) });
		foreach (var c in children)
			sp.Children.Add(c);
		border.Child = sp;
		return border;
	}

	private void BuildTypeList(StackPanel root)
	{
		_panTypes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
		foreach (ServerCoreType type in Enum.GetValues<ServerCoreType>())
			_panTypes.Children.Add(MakeTypeButton(type));
		root.Children.Add(Card("服务端核心类型", _panTypes));
	}

	private Button MakeTypeButton(ServerCoreType type)
	{
		var btn = new Button
		{
			Content = ModServerDownload.GetTypeName(type),
			Margin = new Thickness(0, 0, 8, 0),
			Padding = new Thickness(14, 8, 14, 8),
			BorderThickness = new Thickness(0),
			Cursor = Cursors.Hand,
			Tag = type
		};
		btn.Click += (_, _) => SelectType(type);
		StyleTypeButton(btn, type == _selectedType);
		return btn;
	}

	private void StyleTypeButton(Button btn, bool selected)
	{
		btn.Background = new SolidColorBrush(selected
			? Color.FromRgb(60, 130, 220)
			: Color.FromArgb(55, 255, 255, 255));
		btn.Foreground = Brushes.White;
	}

	private void SelectType(ServerCoreType type, bool loadVersions = true)
	{
		_selectedType = type;
		foreach (var child in _panTypes.Children.OfType<Button>())
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
		_comboTarget = new ComboBox
		{
			Margin = new Thickness(0, 0, 0, 8),
			Foreground = Brushes.White,
			Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
		};
		_comboTarget.SelectionChanged += ComboTarget_SelectionChanged;

		_txtNewName = new TextBox
		{
			Width = 280,
			Margin = new Thickness(0, 0, 0, 8),
			Padding = new Thickness(6),
			Foreground = Brushes.White,
			Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255))
		};
		_txtNewName.TextChanged += (_, _) => UpdateTargetUi();

		_hintTarget = new TextBlock { Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), FontSize = 12, TextWrapping = TextWrapping.Wrap };

		root.Children.Add(Card("安装到", _comboTarget, _txtNewName, _hintTarget));
	}

	private void BuildVersionCard(StackPanel root)
	{
		var topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
		_txtSearch = new TextBox
		{
			Width = 220,
			Padding = new Thickness(6),
			Foreground = Brushes.White,
			Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
			Tag = "搜索版本"
		};
		_txtSearch.TextChanged += TxtSearch_TextChanged;
		var btnRefresh = new Button
		{
			Content = "刷新",
			Margin = new Thickness(8, 0, 0, 0),
			Padding = new Thickness(12, 6, 12, 6),
			BorderThickness = new Thickness(0),
			Cursor = Cursors.Hand,
			Background = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)),
			Foreground = Brushes.White
		};
		btnRefresh.Click += (_, _) => LoadVersionsAsync();
		topRow.Children.Add(_txtSearch);
		topRow.Children.Add(btnRefresh);

		_labVersionCount = new TextBlock { Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), FontSize = 12, Margin = new Thickness(0, 0, 0, 6) };
		_labLoading = new TextBlock { Text = "加载中…", Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), FontSize = 13, Margin = new Thickness(0, 4, 0, 4), Visibility = Visibility.Collapsed };
		_panVersions = new StackPanel { Orientation = Orientation.Vertical };

		root.Children.Add(Card("选择版本", topRow, _labVersionCount, _labLoading, _panVersions));
	}

	private void BuildActionCard(StackPanel root)
	{
		_btnInstall = new Button
		{
			Content = "下载并安装服务端",
			Padding = new Thickness(18, 9, 18, 9),
			BorderThickness = new Thickness(0),
			Cursor = Cursors.Hand,
			Background = new SolidColorBrush(Color.FromRgb(40, 140, 90)),
			Foreground = Brushes.White,
			IsEnabled = false
		};
		_btnInstall.Click += BtnInstall_Click;

		_btnOpenServer = new Button
		{
			Content = "前往服务器管理",
			Margin = new Thickness(8, 0, 0, 0),
			Padding = new Thickness(18, 9, 18, 9),
			BorderThickness = new Thickness(0),
			Cursor = Cursors.Hand,
			Background = new SolidColorBrush(Color.FromRgb(60, 130, 220)),
			Foreground = Brushes.White,
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

		_progInstall = new ProgressBar
		{
			Height = 8,
			Margin = new Thickness(0, 10, 0, 0),
			IsIndeterminate = true,
			Visibility = Visibility.Collapsed,
			Foreground = new SolidColorBrush(Color.FromRgb(60, 130, 220))
		};
		_labInstallStatus = new TextBlock { Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), FontSize = 13, Margin = new Thickness(0, 8, 0, 0) };

		var row = new StackPanel { Orientation = Orientation.Horizontal };
		row.Children.Add(_btnInstall);
		row.Children.Add(_btnOpenServer);

		var card = Card("", row, _progInstall, _labInstallStatus);
		root.Children.Add(card);
	}

	#endregion

	#region 目标服务器

	private void RefreshTargets()
	{
		var tag = (_comboTarget.SelectedItem as ComboBoxItem)?.Tag as ServerInfo;
		var prevFolder = tag?.FolderName;
		_comboTarget.Items.Clear();
		_comboTarget.Items.Add(new ComboBoxItem { Content = "新建服务器", Tag = null });
		foreach (var server in ServerManager.Servers)
			_comboTarget.Items.Add(new ComboBoxItem { Content = "覆盖：" + server.Name, Tag = server });
		int idx = 0;
		if (prevFolder != null)
			for (int i = 1; i < _comboTarget.Items.Count; i++)
				if ((_comboTarget.Items[i] as ComboBoxItem)?.Tag is ServerInfo s && string.Equals(s.FolderName, prevFolder, StringComparison.OrdinalIgnoreCase))
				{ idx = i; break; }
		_comboTarget.SelectedIndex = idx;
		UpdateTargetUi();
	}

	private void ComboTarget_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTargetUi();

	private void UpdateTargetUi()
	{
		bool isNew = _comboTarget.SelectedIndex <= 0;
		_txtNewName.IsEnabled = isNew;
		if (isNew)
		{
			_hintTarget.Text = "将为该服务端核心创建一个新的服务器实例。";
			if (string.IsNullOrWhiteSpace(_txtNewName.Text) || _txtNewName.Text == _autoName)
			{
				_autoName = ModServerDownload.GetTypeName(_selectedType) + ( _selectedVersion != null ? "-" + _selectedVersion.Version : "");
				_txtNewName.Text = _autoName;
			}
		}
		else if (_comboTarget.SelectedItem is ComboBoxItem { Tag: ServerInfo tag })
		{
			_hintTarget.Text = $"将覆盖服务器「{tag.Name}」的现有核心文件（{tag.JarFileName}）。";
		}
		_btnInstall.IsEnabled = !_isInstalling && _selectedVersion != null;
	}

	private string _autoName = "";

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
		_labVersionCount.Text = $"共 {list.Count} 个版本" + (string.IsNullOrEmpty(filter) ? "" : $"（筛选后）");
		foreach (var item in list)
			_panVersions.Children.Add(MakeVersionItem(item));
		if (list.Count == 0)
			_panVersions.Children.Add(new TextBlock { Text = "没有匹配的版本", Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), Margin = new Thickness(4, 8, 0, 8) });
		_btnInstall.IsEnabled = !_isInstalling && _selectedVersion != null;
	}

	private Border MakeVersionItem(ServerVersionEntry entry)
	{
		var border = new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
			CornerRadius = new CornerRadius(7),
			Margin = new Thickness(0, 0, 0, 6),
			Padding = new Thickness(12, 8, 12, 8),
			Cursor = Cursors.Hand,
			Tag = entry
		};
		var sp = new StackPanel();
		sp.Children.Add(new TextBlock { Text = entry.Version, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold });
		if (!string.IsNullOrEmpty(entry.Info))
			sp.Children.Add(new TextBlock { Text = entry.Info + $"  ·  Java {entry.JavaMajorVersion}", Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), FontSize = 12 });
		border.Child = sp;
		border.MouseLeftButtonUp += (_, _) =>
		{
			_selectedVersion = entry;
			HighlightSelectedVersion();
			_labInstallStatus.Text = "已选择：" + entry.Version;
			UpdateTargetUi();
		};
		if (entry == _selectedVersion)
			border.Background = new SolidColorBrush(Color.FromRgb(60, 130, 220));
		return border;
	}

	private void HighlightSelectedVersion()
	{
		foreach (var child in _panVersions.Children.OfType<Border>())
		{
			var selected = child.Tag is ServerVersionEntry e && e == _selectedVersion;
			child.Background = new SolidColorBrush(selected ? Color.FromRgb(60, 130, 220) : Color.FromArgb(45, 255, 255, 255));
		}
	}

	private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RebuildVersionList();

	#endregion

	#region 安装

	private void BtnInstall_Click(object sender, RoutedEventArgs e)
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
			if (_comboTarget.SelectedItem is not ComboBoxItem { Tag: ServerInfo tag })
				return;
			serverInfo = tag;
		}

		_isInstalling = true;
		_installedServer = serverInfo;
		_btnInstall.IsEnabled = false;
		_btnOpenServer.Visibility = Visibility.Collapsed;
		_labInstallStatus.Text = "正在下载并安装服务端…";
		_progInstall.Visibility = Visibility.Visible;

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
		_progInstall.Tag = loaderCombo;
		try { loaderCombo.Start(); }
		catch (Exception ex) { InstallFinished(false, ex.Message); }
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
		_progInstall.Visibility = Visibility.Collapsed;
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
