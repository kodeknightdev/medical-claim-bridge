using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimBridge.ServiceDefaults.Storage;

public sealed record BlobRef(string Container, string Name)
{
    public override string ToString() => $"{Container}/{Name}";
}

/// <summary>Thin wrapper over Blob Storage (Azurite locally) for the claim-check pattern.</summary>
public interface IBlobStore
{
    Task<BlobRef> UploadAsync(string container, string name, Stream content, string contentType, CancellationToken ct = default);
    Task<BlobRef> UploadTextAsync(string container, string name, string content, string contentType = "text/plain", CancellationToken ct = default);
    Task<Stream> OpenReadAsync(BlobRef blob, CancellationToken ct = default);
    Task<string> ReadTextAsync(BlobRef blob, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(string container, string prefix, CancellationToken ct = default);
    Task<BlobRef> MoveAsync(BlobRef source, string targetName, CancellationToken ct = default);
    Task<bool> ExistsAsync(BlobRef blob, CancellationToken ct = default);
}

public sealed class AzureBlobStore(BlobServiceClient client) : IBlobStore
{
    private readonly HashSet<string> _created = [];

    public async Task<BlobRef> UploadAsync(string container, string name, Stream content, string contentType, CancellationToken ct = default)
    {
        var c = await ContainerAsync(container, ct);
        await c.GetBlobClient(name).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, ct);
        return new BlobRef(container, name);
    }

    public async Task<BlobRef> UploadTextAsync(string container, string name, string content, string contentType = "text/plain", CancellationToken ct = default)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        return await UploadAsync(container, name, stream, contentType, ct);
    }

    public async Task<Stream> OpenReadAsync(BlobRef blob, CancellationToken ct = default)
    {
        var c = await ContainerAsync(blob.Container, ct);
        return await c.GetBlobClient(blob.Name).OpenReadAsync(cancellationToken: ct);
    }

    public async Task<string> ReadTextAsync(BlobRef blob, CancellationToken ct = default)
    {
        var c = await ContainerAsync(blob.Container, ct);
        var result = await c.GetBlobClient(blob.Name).DownloadContentAsync(ct);
        return result.Value.Content.ToString();
    }

    public async Task<IReadOnlyList<string>> ListAsync(string container, string prefix, CancellationToken ct = default)
    {
        var c = await ContainerAsync(container, ct);
        var names = new List<string>();
        await foreach (var item in c.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct))
            names.Add(item.Name);
        return names;
    }

    public async Task<BlobRef> MoveAsync(BlobRef source, string targetName, CancellationToken ct = default)
    {
        var c = await ContainerAsync(source.Container, ct);
        var from = c.GetBlobClient(source.Name);
        var content = await from.DownloadContentAsync(ct);
        await c.GetBlobClient(targetName).UploadAsync(content.Value.Content, overwrite: true, ct);
        await from.DeleteIfExistsAsync(cancellationToken: ct);
        return new BlobRef(source.Container, targetName);
    }

    public async Task<bool> ExistsAsync(BlobRef blob, CancellationToken ct = default)
    {
        var c = await ContainerAsync(blob.Container, ct);
        return await c.GetBlobClient(blob.Name).ExistsAsync(ct);
    }

    private async Task<BlobContainerClient> ContainerAsync(string name, CancellationToken ct)
    {
        var c = client.GetBlobContainerClient(name);
        if (_created.Add(name))
            await c.CreateIfNotExistsAsync(cancellationToken: ct);
        return c;
    }
}

public static class BlobStoreExtensions
{
    public static IServiceCollection AddClaimBridgeBlobStorage(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("Blob");
        if (string.IsNullOrWhiteSpace(cs)) cs = "UseDevelopmentStorage=true";
        services.AddSingleton(new BlobServiceClient(cs));
        services.AddSingleton<IBlobStore, AzureBlobStore>();
        services.AddHealthChecks().AddAzureBlobStorage(name: "blob", tags: [ServiceDefaultsExtensions.ReadyTag]);
        return services;
    }
}
