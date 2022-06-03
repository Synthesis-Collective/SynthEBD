using Autofac;
using Noggog.Autofac;
using Xunit;

namespace SynthEBD.Tests;

public class ContainerTests
{
    private static ContainerBuilder GetBuilder()
    {
        var builder = new ContainerBuilder();
        builder.RegisterModule<MainModule>();
        return builder;
    }
        
    [Fact]
    public void GuiModule()
    {
        var builder = GetBuilder();
        var cont = builder.Build();
        cont.Validate(
            typeof(MainWindow_ViewModel));
    }
}