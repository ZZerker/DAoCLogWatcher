using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
#if DEBUG
		this.AttachDeveloperTools();
#endif
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if(this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			var services = new ServiceCollection();
			services.AddSingleton<ISettingsService, SettingsService>();
			services.AddSingleton(sp => sp.GetRequiredService<ISettingsService>().Load());
			services.AddSingleton<ISessionHistoryService, SessionHistoryService>();
			services.AddSingleton<SessionHistoryRecorder>();
			services.AddSingleton<ILogWatcherFactory, LogWatcherFactory>();
			services.AddSingleton(_ => SpellRegistry.LoadFromEmbedded());
			services.AddSingleton<RealmPointSummary>();
			services.AddSingleton<RpsChartData>();
			services.AddSingleton<CombatSummary>();
			services.AddSingleton<IUpdateService, UpdateService>();
			services.AddSingleton<INotificationService, NotificationService>();
			services.AddSingleton<IDaocLogPathService, DaocLogPathService>();
			services.AddSingleton<IWatchSession, WatchSession>();
			services.AddSingleton<IRealmPointProcessor, RealmPointProcessor>();
			services.AddSingleton<ICombatProcessor, CombatProcessor>();
			services.AddSingleton<IFrontierMapService, FrontierMapService>();
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
}
