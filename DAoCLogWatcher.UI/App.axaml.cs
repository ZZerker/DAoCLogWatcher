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
			var mainWindow = new MainWindow
			                 {
					                 DataContext = vm
			                 };
			desktop.MainWindow = mainWindow;
			desktop.Exit += (_, _) => vm.Dispose();

			// Ask for / apply the KWin overlay rule once the main window is up (KDE Wayland only).
			mainWindow.Opened += async (_, _) => await RunKWinRuleConsentAsync(mainWindow, provider);
		}

		base.OnFrameworkInitializationCompleted();
	}

	// On a KDE Plasma Wayland session, keep the (XWayland) overlay above fullscreen games via a KWin
	// window rule — but only with the user's consent. No-op elsewhere; failures are logged, not thrown.
	private static async System.Threading.Tasks.Task RunKWinRuleConsentAsync(Avalonia.Controls.Window owner, IServiceProvider provider)
	{
		try
		{
			if(!KWinOverlayRuleInstaller.IsNeeded())
			{
				return;
			}

			var settings = provider.GetRequiredService<AppSettings>();
			var settingsService = provider.GetRequiredService<ISettingsService>();

			if(settings.KWinRuleConsent == KWinRuleConsent.Granted)
			{
				KWinOverlayRuleInstaller.Install();
				return;
			}

			if(settings.KWinRuleConsent != KWinRuleConsent.NotAsked)
			{
				return; // Declined — never ask again.
			}

			// null = dialog dismissed via titlebar close: save nothing, ask again next launch.
			var apply = await new KWinRuleConsentDialog().ShowDialog<bool?>(owner);
			if(apply == true)
			{
				settings.KWinRuleConsent = KWinRuleConsent.Granted;
				settingsService.Save(settings);
				KWinOverlayRuleInstaller.Install();
			}
			else if(apply == false)
			{
				settings.KWinRuleConsent = KWinRuleConsent.Declined;
				settingsService.Save(settings);
			}
		}
		catch(Exception ex)
		{
			AppLog.Exception("App.RunKWinRuleConsent", ex);
		}
	}
}
