using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trayage.App.Notifications;
using Trayage.App.Tray;
using Trayage.App.ViewModels;
using Trayage.App.Views;
using Trayage.Core.Configuration;
using Trayage.Core.Inbox;
using Trayage.Core.Notifications;
using Trayage.Core.Providers.Bitbucket;
using Trayage.Core.Providers.GitHub;
using Trayage.Core.Security;

namespace Trayage.App;

/// <summary>
/// Central composition root. Each phase registers its services here so the host
/// stays the single place that knows how the app is wired together.
/// </summary>
internal static class ServiceRegistration
{
    public static HostApplicationBuilder ConfigureTrayageServices(this HostApplicationBuilder builder)
    {
        // Configuration & secure storage (Phase 2)
        builder.Services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        builder.Services.AddSingleton<ISecretStore, DpapiSecretStore>();

        // Inbox processing (Phase 2 & 4)
        builder.Services.AddSingleton<InboxAggregator>();
        builder.Services.AddSingleton<InboxDiffer>();
        builder.Services.AddSingleton<InboxState>();
        builder.Services.AddSingleton<InboxService>();

        // Notifications & polling (Phase 5)
        builder.Services.AddSingleton<NotificationRuleEngine>();
        builder.Services.AddSingleton<IToastNotifier, WindowsToastNotifier>();
        builder.Services.AddHostedService<InboxPollingService>();

        // Providers (Phase 3 & 7)
        builder.Services.AddHttpClient();

        builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection(GitHubOptions.SectionName));
        builder.Services.AddSingleton<GitHubProvider>();
        builder.Services.AddSingleton<IInboxProvider>(sp => sp.GetRequiredService<GitHubProvider>());

        builder.Services.Configure<BitbucketOptions>(builder.Configuration.GetSection(BitbucketOptions.SectionName));
        builder.Services.AddSingleton<BitbucketProvider>();
        builder.Services.AddSingleton<IInboxProvider>(sp => sp.GetRequiredService<BitbucketProvider>());

        // UI / shell (Phase 1, 4 & 6)
        builder.Services.AddSingleton<TrayIconService>();
        builder.Services.AddSingleton<InboxViewModel>();
        builder.Services.AddSingleton<InboxFlyout>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<SettingsWindow>();

        return builder;
    }
}
