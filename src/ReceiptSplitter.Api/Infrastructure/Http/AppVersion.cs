using System.Reflection;

namespace ReceiptSplitter.Api.Infrastructure.Http;

/// <summary>
/// The running version, from the assembly's informational version. The ci-cd workflow sets it with
/// <c>-p:InformationalVersion</c>: "1.2.3" for a release, "1.2.3+abc1234" for an unreleased build.
/// Local builds keep the default from Directory.Build.props.
/// </summary>
public static class AppVersion
{
    public static string Current { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "dev";
}
