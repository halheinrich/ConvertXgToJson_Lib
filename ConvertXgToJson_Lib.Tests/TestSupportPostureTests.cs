using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Xml.Linq;
using ConvertXgToJson_Lib.Models;
using ConvertXgToJson_Lib.TestSupport;

namespace ConvertXgToJson_Lib.Tests;

/// <summary>
/// The declarations that keep the test-support assembly out of products, and
/// the direction of the dependency between it and the library. It makes
/// malformed files for tests, so it must never reach a product, and the
/// library must get no door to it. Adapted from BgDataTypes_Lib.Tests' class of
/// this name: that library references no project at all, while this one
/// references others, so the pin here is on its own test support alone.
/// </summary>
public class TestSupportPostureTests
{
    private static readonly Assembly TestSupport = typeof(XgFileEdits).Assembly;

    private static XDocument StagedProject(string fileName) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Posture", fileName));

    // In any project that declares the browser platform, the analyzer reports
    // a use of this assembly as CA1416, an error under TreatWarningsAsErrors.
    // This pins the declaration, not the analyzer's verdict, which belongs to
    // the referencing project's build.
    [Fact]
    public void TheTestSupportAssembly_DeclaresItselfUnsupportedInTheBrowser()
    {
        var declaration = TestSupport.GetCustomAttributes<UnsupportedOSPlatformAttribute>().Should().ContainSingle().Subject;

        declaration.PlatformName.Should().Be("browser");
    }

    // Trimmable is a promise about a shipped surface; this assembly makes none.
    [Fact]
    public void TheTestSupportAssembly_DoesNotDeclareItselfTrimmable()
    {
        TestSupport.GetCustomAttributes<AssemblyMetadataAttribute>().Should().NotContain(a => a.Key == "IsTrimmable");
    }

    [Fact]
    public void TheLibrary_DoesNotReferenceTheTestSupportProject()
    {
        var references = StagedProject("ConvertXgToJson_Lib.csproj").Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")!.Value)
            .ToList();

        references.Should().NotBeEmpty("this reads the library's project file, which references BgDataTypes_Lib");
        references.Should().NotContain(reference => reference.Contains("ConvertXgToJson_Lib.TestSupport"));
    }

    // The edits see what any consumer sees: the library names this suite as a
    // friend and never the test support.
    [Fact]
    public void TheLibrary_GrantsTheTestSupportAssemblyNoInternals()
    {
        var friends = typeof(XgFile).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName);

        friends.Should().Equal("ConvertXgToJson_Lib.Tests");
    }

    [Fact]
    public void TheTestSupportProject_ReferencesOnlyTheLibrary_AndNoPackage()
    {
        var project = StagedProject("ConvertXgToJson_Lib.TestSupport.csproj");

        project.Descendants("ProjectReference").Should().ContainSingle()
            .Which.Attribute("Include")!.Value.Should().Be(@"..\ConvertXgToJson_Lib\ConvertXgToJson_Lib.csproj");
        project.Descendants("PackageReference").Should().BeEmpty();
    }

    // Consumers reach it by project reference from their test projects; it is
    // never a package.
    [Fact]
    public void TheTestSupportProject_IsNotPackable()
    {
        StagedProject("ConvertXgToJson_Lib.TestSupport.csproj").Descendants("IsPackable").Should().ContainSingle()
            .Which.Value.Should().Be("false");
    }

    // No test framework rides along into a consumer's test project.
    [Fact]
    public void TheTestSupportAssembly_ReferencesNoTestFramework()
    {
        TestSupport.GetReferencedAssemblies().Should().NotContain(
            a => a.Name!.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
                || a.Name.StartsWith("nunit", StringComparison.OrdinalIgnoreCase)
                || a.Name.StartsWith("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase));
    }
}
