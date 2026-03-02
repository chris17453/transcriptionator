using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Transcriptonator.Data;
using Transcriptonator.Services;
using Transcriptonator.ViewModels;
using Transcriptonator.Views;

namespace Transcriptonator;

public partial class App : Application
{
    public static ServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                var services = new ServiceCollection();
                ConfigureServices(services);
                Services = services.BuildServiceProvider();

                // Run migrations
                using (var scope = Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<TranscriptonatorDbContext>();
                    db.Database.Migrate();
                }

                var mainVm = Services.GetRequiredService<MainWindowViewModel>();
                var configService = Services.GetRequiredService<IConfigService>();
                var modelManager = Services.GetRequiredService<IModelManagerService>();
                var config = configService.Load();

                // Wire up child ViewModels
                mainVm.Transcribe = Services.GetRequiredService<TranscribeViewModel>();
                mainVm.Transcriptions = Services.GetRequiredService<TranscriptionsViewModel>();
                mainVm.Search = Services.GetRequiredService<SearchViewModel>();
                mainVm.Settings = Services.GetRequiredService<SettingsViewModel>();

                // Check if setup is needed
                if (!modelManager.AreAllModelsReady(config.WhisperModelSize))
                {
                    mainVm.IsSetupRequired = true;
                    mainVm.Setup = new SetupViewModel(modelManager, configService, () =>
                    {
                        mainVm.IsSetupRequired = false;
                    });

                    // Auto-start download
                    _ = mainVm.Setup.StartDownloadCommand.ExecuteAsync(null);
                }

                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainVm
                };
            }
            catch (Exception ex)
            {
                Program.ShowFatalError(
                    "Transcriptonator failed to initialize.\n\n" +
                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton<IModelManagerService, ModelManagerService>();

        // Database
        services.AddDbContext<TranscriptonatorDbContext>((sp, options) =>
        {
            var config = sp.GetRequiredService<IConfigService>();
            options.UseSqlite($"Data Source={config.DatabasePath}");
        });
        services.AddDbContextFactory<TranscriptonatorDbContext>((sp, options) =>
        {
            var config = sp.GetRequiredService<IConfigService>();
            options.UseSqlite($"Data Source={config.DatabasePath}");
        }, ServiceLifetime.Singleton);

        // Data services
        services.AddTransient<IFileTrackingService, FileTrackingService>();

        // AI services
        services.AddTransient<ITranscriptionService, TranscriptionService>();
        services.AddSingleton<IEmbeddingService, EmbeddingService>();
        services.AddSingleton<IVectorSearchService, VectorSearchService>();
        services.AddSingleton<ILlmService, LlmService>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<TranscribeViewModel>();
        services.AddTransient<TranscriptionsViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<SettingsViewModel>();
    }
}
