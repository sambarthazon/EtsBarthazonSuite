using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Barthazon.Portal.Hosting;

/// <summary>
/// Blazor Server : alimente <see cref="CurrentUserAccessor"/> avec l'utilisateur du circuit
/// avant chaque activité entrante (clic, saisie…).
/// </summary>
internal sealed class UserCircuitHandler(AuthenticationStateProvider authenticationStateProvider, CurrentUserAccessor accessor)
    : CircuitHandler, IDisposable
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next) =>
        async context =>
        {
            var state = await authenticationStateProvider.GetAuthenticationStateAsync();
            accessor.SetUser(state.User);
            await next(context);
        };

    public void Dispose() => authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task) => _ = UpdateAsync(task);

    private async Task UpdateAsync(Task<AuthenticationState> task)
    {
        var state = await task;
        accessor.SetUser(state.User);
    }
}
