using System.Net;
using System.Text.Json;
using ClaimBridge.Contracts.Webhooks;
using ClaimBridge.Notifications.Data;
using ClaimBridge.Notifications.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ClaimBridge.Notifications.Tests;

public class WebhookSenderTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Subscription = Guid.NewGuid();
    private const string Secret = "whsec_sender_test";

    private readonly NotificationsDb _db = new(new DbContextOptionsBuilder<NotificationsDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2025, 11, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly Receiver _receiver = new();
    private readonly ProvidersStub _providers = new();

    private WebhookSender Sender(WebhookOptions? options = null)
    {
        var cache = new ServiceCollection().AddHybridCache().Services.BuildServiceProvider().GetRequiredService<HybridCache>();
        var providers = new ProvidersClient(new HttpClient(_providers) { BaseAddress = new Uri("http://providers/") }, cache);
        return new WebhookSender(new Factory(_receiver), providers, Options.Create(options ?? new WebhookOptions()),
            new WebhookMetrics(new MeterFactory()), _clock, NullLogger<WebhookSender>.Instance);
    }

    private async Task<WebhookDelivery> Pending()
    {
        var d = new WebhookDelivery
        {
            Id = Guid.NewGuid(), SubscriptionId = Subscription, OrgId = Org, SourceEventId = Guid.NewGuid(), EventType = "claim.paid",
            Url = "http://receiver/hook", Payload = """{"type":"claim.paid"}""", NextAttemptAt = _clock.GetUtcNow(), CreatedAt = _clock.GetUtcNow()
        };
        _db.Deliveries.Add(d);
        await _db.SaveChangesAsync();
        return d;
    }

    [Fact]
    public async Task Successful_delivery_is_signed_and_logged()
    {
        var d = await Pending();
        await Sender().AttemptAsync(_db, d, manual: false, default);

        d.Status.ShouldBe(DeliveryStatus.Succeeded);
        d.AttemptLog.Single().StatusCode.ShouldBe(200);
        var request = _receiver.Requests.Single();
        request.Event.ShouldBe("claim.paid");
        WebhookSignature.Verify(Secret, request.Signature, request.Body, _clock.GetUtcNow()).ShouldBeTrue();
    }

    [Fact]
    public async Task Failure_schedules_the_next_attempt_with_backoff()
    {
        _receiver.Status = HttpStatusCode.InternalServerError;
        var d = await Pending();
        await Sender().AttemptAsync(_db, d, manual: false, default);

        d.Status.ShouldBe(DeliveryStatus.Pending);
        d.Attempts.ShouldBe(1);
        d.LastStatusCode.ShouldBe(500);
        (d.NextAttemptAt - _clock.GetUtcNow()).TotalSeconds.ShouldBeInRange(10, 11);
    }

    [Fact]
    public async Task Delivery_is_abandoned_after_the_attempt_budget()
    {
        _receiver.Status = HttpStatusCode.ServiceUnavailable;
        var d = await Pending();
        var sender = Sender(new WebhookOptions { BackoffSeconds = [1, 1], DisableAfterConsecutiveFailures = 100 });
        for (var i = 0; i < 3; i++) await sender.AttemptAsync(_db, d, manual: false, default);

        d.Status.ShouldBe(DeliveryStatus.Abandoned);
        d.AttemptLog.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Repeated_failures_auto_disable_the_subscription()
    {
        _receiver.Status = HttpStatusCode.BadGateway;
        var sender = Sender(new WebhookOptions { DisableAfterConsecutiveFailures = 3 });
        for (var i = 0; i < 3; i++) await sender.AttemptAsync(_db, await Pending(), manual: false, default);
        await _db.SaveChangesAsync();

        (await _db.SubscriptionHealth.SingleAsync()).DisabledAt.ShouldNotBeNull();
        _providers.DisableCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Success_resets_the_failure_streak()
    {
        var sender = Sender(new WebhookOptions { DisableAfterConsecutiveFailures = 3 });
        _receiver.Status = HttpStatusCode.InternalServerError;
        await sender.AttemptAsync(_db, await Pending(), manual: false, default);
        await sender.AttemptAsync(_db, await Pending(), manual: false, default);
        await _db.SaveChangesAsync();
        _receiver.Status = HttpStatusCode.OK;
        await sender.AttemptAsync(_db, await Pending(), manual: false, default);
        await _db.SaveChangesAsync();

        (await _db.SubscriptionHealth.SingleAsync()).ConsecutiveFailures.ShouldBe(0);
        _providers.DisableCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Deliveries_to_a_disabled_subscription_are_abandoned_unless_sent_manually()
    {
        _providers.Active = false;
        var d = await Pending();
        await Sender().AttemptAsync(_db, d, manual: false, default);
        d.Status.ShouldBe(DeliveryStatus.Abandoned);

        var manual = await Pending();
        await Sender().AttemptAsync(_db, manual, manual: true, default);
        manual.Status.ShouldBe(DeliveryStatus.Succeeded);
        manual.AttemptLog.Single().Manual.ShouldBeTrue();
    }

    private sealed class Receiver : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public List<(string Body, string Signature, string Event)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((await request.Content!.ReadAsStringAsync(ct), request.Headers.GetValues(WebhookSignature.HeaderName).Single(),
                request.Headers.GetValues(WebhookSignature.EventHeader).Single()));
            return new HttpResponseMessage(Status);
        }
    }

    private sealed class ProvidersStub : HttpMessageHandler
    {
        public bool Active { get; set; } = true;
        public int DisableCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                DisableCalls++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            var body = JsonSerializer.Serialize(new[]
            {
                new WebhookTarget(Subscription, Org, "http://receiver/hook", ["claim.paid"], Active, Secret)
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class MeterFactory : System.Diagnostics.Metrics.IMeterFactory
    {
        public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
