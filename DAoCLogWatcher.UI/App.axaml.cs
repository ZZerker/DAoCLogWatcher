using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using DAoCLogWatcher.Core;
using DAoCLogWatcher.UI.Models;
using DAoCLogWatcher.UI.Services;
using DAoCLogWatcher.UI.ViewModels;
using DAoCLogWatcher.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DAoCLogWatcher.UI;

public partial class App: Application
{
	public IServiceProvider Services { get; private set; } = null!;

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if(this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			// Avoid duplicate validations from both Avalonia and the CommunityToolkit.
			// More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
			this.DisableAvaloniaDataAnnotationValidation();

			var services = new ServiceCollection();
			services.AddSingleton<ISettingsService, SettingsService>();
			services.AddSingleton(sp => sp.GetRequiredService<ISettingsService>().Load());
			services.AddSingleton<ILogWatcherFactory, LogWatcherFactory>();
			services.AddSingleton<RealmPointSummary>();
			services.AddSingleton<RpsChartData>();
			services.AddSingleton<CombatSummary>();
			services.AddSingleton<IUpdateService, UpdateService>();
			services.AddSingleton<INotificationService, NotificationService>();
			services.AddSingleton<IDaocLogPathService, DaocLogPathService>();
			services.AddSingleton<IWatchSession, WatchSession>();
			services.AddSingleton<IRealmPointProcessor, RealmPointProcessor>();
			services.AddSingleton<ICombatProcessor, CombatProcessor>();
			services.AddSingleton<FrontierMapService>();
			services.AddSingleton<ZoneMapService>();
			services.AddSingleton<WarmapWebSocketService>();
			services.AddSingleton<MainWindowViewModel>();
			var provider = services.BuildServiceProvider();
			this.Services = provider;

			var warmap = provider.GetRequiredService<WarmapWebSocketService>();
			warmap.Start();

			var vm = provider.GetRequiredService<MainWindowViewModel>();
			desktop.MainWindow = new MainWindow
			                     {
					                     DataContext = vm
			                     };
			desktop.Exit += (_, _) => vm.Dispose();
		}

		base.OnFrameworkInitializationCompleted();
	}

	private void DisableAvaloniaDataAnnotationValidation()
	{
		var dataValidationPluginsToRemove = BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

		foreach(var plugin in dataValidationPluginsToRemove)
		{
			BindingPlugins.DataValidators.Remove(plugin);
		}
	}
}
