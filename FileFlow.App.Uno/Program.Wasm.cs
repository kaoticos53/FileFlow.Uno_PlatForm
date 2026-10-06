#if __WASM__
using System.Threading.Tasks;
using Uno.UI.Hosting;

namespace FileFlow.App.Uno;

/// <summary>Punto de entrada del host para WebAssembly (navegador).</summary>
public static class WasmProgram
{
    public static async Task Main(string[] args)
    {
        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseWebAssembly()
            .Build();

        await host.RunAsync();
    }
}
#endif
