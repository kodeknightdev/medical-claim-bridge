using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimBridge.ServiceDefaults.Phi;

public static class PhiExtensions
{
    public static IServiceCollection AddPhiProtection(this IServiceCollection services, IConfiguration config) =>
        services.AddSingleton<IPhiProtector>(_ => AesGcmPhiProtector.FromConfiguration(config));

    /// <summary>Last four characters only, for display and logs.</summary>
    public static string MaskTail(this string value, int visible = 4) =>
        value.Length <= visible ? new string('*', value.Length) : new string('*', value.Length - visible) + value[^visible..];
}
