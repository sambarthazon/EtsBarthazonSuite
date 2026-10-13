using Barthazon.Catalog.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Testing;

/// <summary>Seconde « session » (portée DI distincte) pour simuler un autre utilisateur simultané.</summary>
public sealed class TestScope(AsyncServiceScope scope) : IAsyncDisposable
{
    public IRevisionService Revisions => scope.ServiceProvider.GetRequiredService<IRevisionService>();

    public IPartService Parts => scope.ServiceProvider.GetRequiredService<IPartService>();

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}

public static class TestScopeExtensions
{
    public static TestScope CreateAsyncScopeForTest(this IServiceProvider services) =>
        new(services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope());
}
