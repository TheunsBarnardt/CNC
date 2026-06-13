using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Backend.Import;
using Backend.Import.Bitmap;
using Backend.Import.Dxf;
using Backend.Import.Svg;
using Backend.Machine;
using Backend.Post;
using Backend.Services;
using Desktop.ViewModels;
using Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop;

public sealed class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var sc = new ServiceCollection();
        ConfigureServices(sc);
        Services = sc.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection sc)
    {
        // Backend services — direct in-process, no HTTP.
        sc.AddSingleton<ProjectService>();
        sc.AddSingleton<IFileImporter, SvgImporter>();
        sc.AddSingleton<IFileImporter, DxfImporter>();
        sc.AddSingleton<IFileImporter, BitmapImporter>();
        sc.AddSingleton<FileImportService>();

        sc.AddSingleton<IPostProcessor, GrblPlasmaPostProcessor>();
        sc.AddSingleton<IPostProcessor, GrblLaserPostProcessor>();
        sc.AddSingleton<IPostProcessor, GrblVinylPostProcessor>();
        sc.AddSingleton<PostProcessorRegistry>();

        sc.AddSingleton<ProfileStore>();
        sc.AddSingleton<TemplateStore>();
        sc.AddSingleton<ElementStore>();
        sc.AddSingleton<CheckpointService>();

        sc.AddSingleton<FakeMachineConnection>();
        sc.AddSingleton<MachineConnectionManager>();

        // ViewModels — sub-VMs first so MainViewModel can inject them.
        sc.AddSingleton<MachineViewModel>();
        sc.AddSingleton<CutSettingsViewModel>();
        sc.AddSingleton<MainViewModel>(sp => new MainViewModel(
            sp.GetRequiredService<ProjectService>(),
            sp.GetRequiredService<FileImportService>(),
            sp.GetRequiredService<PostProcessorRegistry>(),
            sp.GetRequiredService<MachineViewModel>(),
            sp.GetRequiredService<CutSettingsViewModel>(),
            sp.GetService<TemplateStore>(),
            sp.GetService<ElementStore>()));
    }
}
