using System.Xml.Linq;
using NetArchTest.Rules;
using Qms.Application.Common;
using Qms.Domain;

namespace Qms.Tests.Architecture;

public sealed class ArchitectureTests
{
    private const string Domain = "Qms.Domain";
    private const string Application = "Qms.Application";
    private const string Infrastructure = "Qms.Infrastructure";
    private const string Api = "Qms.Api";

    [Fact]
    public void Domain_does_not_depend_on_other_layers()
    {
        var result = Types.InAssembly(DomainAssembly.Assembly)
            .ShouldNot().HaveDependencyOnAny(Application, Infrastructure, Api)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(typeof(Result).Assembly)
            .ShouldNot().HaveDependencyOnAny(Infrastructure, Api)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_api()
    {
        var result = Types.InAssembly(typeof(Qms.Infrastructure.DependencyInjection).Assembly)
            .ShouldNot().HaveDependencyOn(Api)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue();
    }

    [Theory]
    [InlineData(Domain, new string[0])]
    [InlineData(Application, new[] { Domain })]
    [InlineData(Infrastructure, new[] { Application })]
    [InlineData(Api, new[] { Infrastructure })]
    public void Project_references_follow_clean_architecture(string project, string[] allowedReferences)
    {
        var csproj = Path.Combine(FindBackendDirectory(), project, project + ".csproj");

        var references = XDocument.Load(csproj)
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(((string)element.Attribute("Include")!).Replace('\\', '/')))
            .ToArray();

        references.OrderBy(name => name).ShouldBe(allowedReferences.OrderBy(name => name));
    }

    [Fact]
    public void Controllers_do_not_contain_use_case_handlers()
    {
        var handlerTypesInApi = Types.InAssembly(typeof(Program).Assembly)
            .That().ImplementInterface(typeof(ICommandHandler<,>))
            .Or().ImplementInterface(typeof(IQueryHandler<,>))
            .GetTypes();

        handlerTypesInApi.ShouldBeEmpty();
    }

    private static string FindBackendDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Qms.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Qms.slnx not found above the test output.");
    }
}
