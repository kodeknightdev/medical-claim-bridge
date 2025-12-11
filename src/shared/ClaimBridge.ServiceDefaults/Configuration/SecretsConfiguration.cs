using Microsoft.Extensions.Configuration;

namespace ClaimBridge.ServiceDefaults.Configuration;

/// <summary>
/// Chooses where secrets come from via <c>Secrets:Provider</c>.
/// <c>local</c> (default): environment variables, .env and user secrets, plus <c>Phi:KeyFile</c> for the PHI key.
/// <c>keyvault</c>: one file per secret mounted by the Key Vault CSI driver, named after the configuration key with
/// <c>--</c> for <c>:</c> (for example <c>ConnectionStrings--ClaimsDb</c>).
/// </summary>
public static class SecretsConfiguration
{
    public const string Local = "local";
    public const string KeyVault = "keyvault";
    public const string DefaultMountPath = "/mnt/secrets-store";

    public static IConfigurationManager AddClaimBridgeSecrets(this IConfigurationManager config)
    {
        switch ((config["Secrets:Provider"] ?? Local).ToLowerInvariant())
        {
            case Local:
                if (string.IsNullOrWhiteSpace(config["Phi:Key"]) && config["Phi:KeyFile"] is { Length: > 0 } keyFile)
                    config.AddInMemoryCollection([new("Phi:Key", File.ReadAllText(keyFile).Trim())]);
                break;
            case KeyVault:
                var path = config["Secrets:MountPath"] ?? DefaultMountPath;
                if (!Directory.Exists(path))
                    throw new InvalidOperationException($"Secrets:Provider is keyvault but nothing is mounted at {path}.");
                config.AddInMemoryCollection(ReadMounted(path));
                break;
            default:
                throw new InvalidOperationException($"Unknown Secrets:Provider '{config["Secrets:Provider"]}'. Use local or keyvault.");
        }
        return config;
    }

    public static IEnumerable<KeyValuePair<string, string?>> ReadMounted(string path) =>
        Directory.EnumerateFiles(path)
            // the CSI driver keeps its own ..data bookkeeping next to the secrets
            .Where(f => !Path.GetFileName(f).StartsWith('.'))
            .Select(f => new KeyValuePair<string, string?>(Path.GetFileName(f).Replace("--", ":"), File.ReadAllText(f).Trim()))
            .ToList();
}
