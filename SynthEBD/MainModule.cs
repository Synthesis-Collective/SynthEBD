using Autofac;

namespace SynthEBD;

public class MainModule : Autofac.Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<MainWindow_ViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<MainWindow_ViewModel>().AsSelf().SingleInstance();
        builder.RegisterType<SaveLoader>().AsSelf();
        builder.RegisterType<VM_Settings_General>().AsSelf().SingleInstance();
        builder.RegisterType<MainState>().AsSelf().SingleInstance();
    }
}