using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HammerOS.Core;
using HammerOS.Desktop;

namespace HammerOS;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new ShellWindow(new SystemState(), Program.Windowed);
        base.OnFrameworkInitializationCompleted();
    }
}
