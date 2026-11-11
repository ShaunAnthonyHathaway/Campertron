using System.Reflection;
using System.Runtime.InteropServices;
using CampertronLibrary.function.Base;

namespace CampertronUnitTests.function.Base
{
    // RuntimeInfo backs the "--version" argument of CampertronConsole and CampertronGUI. No user
    // directories, network or database are used.
    public class RuntimeInfoTests
    {
        [Test]
        public void IsVersionRequest_OnlyExactlyVersionArgument()
        {
            Assert.That(RuntimeInfo.IsVersionRequest(new[] { "--version" }), Is.True);

            Assert.That(RuntimeInfo.IsVersionRequest(null), Is.False);
            Assert.That(RuntimeInfo.IsVersionRequest(Array.Empty<string>()), Is.False);
            Assert.That(RuntimeInfo.IsVersionRequest(new[] { "--VERSION" }), Is.False);
            Assert.That(RuntimeInfo.IsVersionRequest(new[] { "-v" }), Is.False);
            Assert.That(RuntimeInfo.IsVersionRequest(new[] { "--version", "extra" }), Is.False);
        }

        [Test]
        public void Describe_ReportsAssemblyVersionAndRuntime()
        {
            Assembly Library = typeof(RuntimeInfo).Assembly;
            string[] Lines = RuntimeInfo.Describe(Library).Split(Environment.NewLine);

            Assert.That(Lines, Has.Length.EqualTo(2));
            string? Informational = Library.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            Assert.That(Lines[0], Is.EqualTo("CampertronLibrary " + Informational));
            Assert.That(Lines[1], Is.EqualTo("Framework => " + RuntimeInformation.FrameworkDescription + " (" + RuntimeInformation.ProcessArchitecture + ")"));
        }

        // Every project targets net10.0, so the tests must run on .NET 10 and the library must be built for it.
        [Test]
        public void TestsAndLibrary_RunOnDotNet10()
        {
            Assert.That(RuntimeInformation.FrameworkDescription, Does.StartWith(".NET 10."));
            Assert.That(Environment.Version.Major, Is.EqualTo(10));
            string? TargetFramework = typeof(RuntimeInfo).Assembly
                .GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName;
            Assert.That(TargetFramework, Is.EqualTo(".NETCoreApp,Version=v10.0"));
        }
    }
}
