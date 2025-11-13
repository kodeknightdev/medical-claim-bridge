using ClaimBridge.ServiceDefaults.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ClaimBridge.Security.Tests;

public class PhiRedactionTests
{
    private sealed class Capture : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static (ILogger Logger, Capture Sink) Logger()
    {
        var sink = new Capture();
        return (new LoggerConfiguration().Enrich.With<PhiRedactionEnricher>().WriteTo.Sink(sink).CreateLogger(), sink);
    }

    [Fact]
    public void Named_phi_properties_are_masked()
    {
        var (log, sink) = Logger();
        log.Information("Eligibility for {MemberId} born {DateOfBirth} on {PayerId}", "NWH1000002", "1958-02-12", "PAYER-A");

        var e = sink.Events.Single();
        e.Properties["MemberId"].ToString().ShouldBe("\"[REDACTED]\"");
        e.Properties["DateOfBirth"].ToString().ShouldBe("\"[REDACTED]\"");
        e.Properties["PayerId"].ToString().ShouldBe("\"PAYER-A\"");
        e.RenderMessage().ShouldNotContain("NWH1000002");
    }

    [Fact]
    public void Phi_inside_destructured_objects_is_masked()
    {
        var (log, sink) = Logger();
        log.Information("Patient {@Patient}", new { FirstName = "Jamie", LastName = "Example", Gender = "F" });
        var rendered = sink.Events.Single().RenderMessage();
        rendered.ShouldNotContain("Jamie");
        rendered.ShouldNotContain("Example");
        rendered.ShouldContain("Gender");
    }

    [Fact]
    public void Free_text_dates_that_look_like_birthdates_are_masked_but_recent_dates_are_kept()
    {
        var (log, sink) = Logger();
        log.Information("Value {Value} and {ServiceDate}", "1975-06-01", DateTime.UtcNow.ToString("yyyy-MM-dd"));
        var e = sink.Events.Single();
        e.Properties["Value"].ToString().ShouldBe("\"[REDACTED]\"");
        e.Properties["ServiceDate"].ToString().ShouldNotContain("REDACTED");
    }
}
