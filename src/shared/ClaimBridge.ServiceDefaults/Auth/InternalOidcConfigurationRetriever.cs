using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace ClaimBridge.ServiceDefaults.Auth;

// the discovery document advertises jwks_uri on the public issuer host, which containers can't reach
internal sealed class InternalOidcConfigurationRetriever(string internalAuthority) : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var document = await retriever.GetDocumentAsync(address, cancel);
        var config = OpenIdConnectConfiguration.Create(document);

        var jwksPath = new Uri(config.JwksUri).PathAndQuery;
        var keys = await retriever.GetDocumentAsync(new Uri(new Uri(internalAuthority), jwksPath).ToString(), cancel);
        config.JsonWebKeySet = JsonWebKeySet.Create(keys);
        foreach (var key in config.JsonWebKeySet.GetSigningKeys())
            config.SigningKeys.Add(key);
        return config;
    }
}
