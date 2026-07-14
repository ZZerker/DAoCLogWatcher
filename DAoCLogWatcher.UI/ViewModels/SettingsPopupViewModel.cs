using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;

namespace DAoCLogWatcher.UI.ViewModels;

public sealed partial class SettingsPopupViewModel: ObservableObject
{
	private readonly AppSettings settings;
	private readonly ISettingsService settingsService;

	public SettingsPopupViewModel(AppSettings settings, ISettingsService settingsService)
	{
		this.settings = settings;
		this.settingsService = settingsService;
		this.customChatLogPath = settings.CustomChatLogPath;
	}

	public string AppVersion { get; } = ReadAppVersion();

	private static string ReadAppVersion()
	{
		var assembly = Assembly.GetExecutingAssembly();
		var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		if(!string.IsNullOrWhiteSpace(informational))
		{
			// Strip the "+<commit hash>" source-revision suffix appended by the build.
			var plusIndex = informational.IndexOf('+');
			return $"v{(plusIndex >= 0?informational[..plusIndex]:informational)}";
		}

		var version = assembly.GetName().Version;
		return version != null?$"v{version.ToString(3)}":"unknown";
	}

	[ObservableProperty] private bool isSettingsPopupVisible;

	[RelayCommand]
	private void ToggleSettingsPopup()
	{
		this.IsSettingsPopupVisible = !this.IsSettingsPopupVisible;
	}

	[RelayCommand]
	private void CloseSettingsPopup()
	{
		this.IsSettingsPopupVisible = false;
	}

	[ObservableProperty] private string? customChatLogPath;

	partial void OnCustomChatLogPathChanged(string? value)
	{
		this.settings.CustomChatLogPath = string.IsNullOrWhiteSpace(value)?null:value;
		this.settingsService.Save(this.settings);
	}

	[RelayCommand]
	private async Task BrowseChatLogPath(IStorageProvider? storageProvider)
	{
		if(storageProvider == null)
		{
			return;
		}

		var options = new FilePickerOpenOptions
		              {
				              Title = "Select DAoC Chat Log",
				              AllowMultiple = false,
				              FileTypeFilter =
				              [
						              new FilePickerFileType("Log Files")
						              {
								              Patterns = ["*.log"]
						              },
						              new FilePickerFileType("All Files")
						              {
								              Patterns = ["*.*"]
						              }
				              ]
		              };
		var result = await storageProvider.OpenFilePickerAsync(options);
		if(result.Count > 0)
		{
			this.CustomChatLogPath = result[0].Path.LocalPath;
		}
	}

	[RelayCommand]
	private void OpenLogFolder()
	{
		AppLog.OpenLogFolder();
	}

	[RelayCommand]
	private void ClearChatLogPath()
	{
		this.CustomChatLogPath = null;
	}

	[ObservableProperty] private bool isDarkTheme = true;

	public string ThemeIcon => this.IsDarkTheme?"☀":"🌙";

	public string ThemeTooltip => this.IsDarkTheme?"Switch to light theme":"Switch to dark theme";

	partial void OnIsDarkThemeChanged(bool value)
	{
		this.OnPropertyChanged(nameof(this.ThemeIcon));
		this.OnPropertyChanged(nameof(this.ThemeTooltip));
	}

	[RelayCommand]
	private void ToggleTheme()
	{
		this.IsDarkTheme = !this.IsDarkTheme;
	}

	[ObservableProperty] private bool isSidebarVisible = true;

	public string SidebarToggleIcon => "◀";

	partial void OnIsSidebarVisibleChanged(bool value)
	{
		this.OnPropertyChanged(nameof(this.SidebarToggleIcon));
	}

	[RelayCommand]
	private void ToggleSidebar()
	{
		this.IsSidebarVisible = !this.IsSidebarVisible;
	}
}
