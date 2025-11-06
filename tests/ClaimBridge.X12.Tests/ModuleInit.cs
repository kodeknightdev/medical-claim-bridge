using System.Runtime.CompilerServices;

namespace ClaimBridge.X12.Tests;

public static class ModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        // snapshots live next to the tests as *.verified.txt
        VerifierSettings.DontScrubDateTimes();
        UseProjectRelativeDirectory("Snapshots");
    }
}
