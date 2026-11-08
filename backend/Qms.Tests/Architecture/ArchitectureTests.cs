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

    // ---- STORY-005 AC-34

    [Fact]
    public void Application_references_entity_framework_but_not_infrastructure()
    {
        var references = typeof(Result).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToList();

        references.ShouldContain("Microsoft.EntityFrameworkCore");
        references.ShouldNotContain(Infrastructure);
        references.ShouldNotContain(Api);
        XDocument.Load(Path.Combine(FindBackendDirectory(), Application, Application + ".csproj"))
            .Descendants("PackageReference").Select(e => (string?)e.Attribute("Include")).ShouldContain("Microsoft.EntityFrameworkCore");
    }

    [Fact]
    public void The_database_context_implements_the_application_port_and_handlers_use_the_port()
    {
        typeof(IQmsDbContext).IsAssignableFrom(typeof(Qms.Infrastructure.Persistence.QmsDbContext)).ShouldBeTrue();

        var handlersUsingTheConcreteContext = Types.InAssembly(typeof(Result).Assembly)
            .That().ImplementInterface(typeof(ICommandHandler<,>)).Or().ImplementInterface(typeof(IQueryHandler<,>))
            .Should().NotHaveDependencyOn("Qms.Infrastructure.Persistence.QmsDbContext")
            .GetResult();
        handlersUsingTheConcreteContext.IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Controllers_hold_no_business_logic_dependencies()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().ResideInNamespace("Qms.Api.Controllers")
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Qms.Domain", "Qms.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Every_controller_action_declares_a_permission_or_is_explicitly_exempt()
    {
        // Controllers that rely on the authentication-only fallback policy on purpose (own session endpoints).
        var authenticationOnly = new HashSet<string> { nameof(Qms.Api.Controllers.AuthController) };
        var controllers = typeof(Program).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsPublic: true } && typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(type))
            .ToList();

        controllers.Count.ShouldBeGreaterThanOrEqualTo(3);
        foreach (var controller in controllers.Where(type => !authenticationOnly.Contains(type.Name)))
        {
            var actions = controller.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);
            actions.ShouldNotBeEmpty(controller.Name);
            foreach (var action in actions)
            {
                var protectedByPermission = action.IsDefined(typeof(Qms.Api.Auth.RequirePermissionAttribute), inherit: true)
                    || controller.IsDefined(typeof(Qms.Api.Auth.RequirePermissionAttribute), inherit: true);
                var anonymous = action.IsDefined(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: true)
                    || controller.IsDefined(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), inherit: true);
                (protectedByPermission || anonymous).ShouldBeTrue($"{controller.Name}.{action.Name} needs [RequirePermission] or [AllowAnonymous]");
            }
        }
    }

    [Fact]
    public void The_role_map_grants_the_module_view_whenever_it_grants_any_other_action_of_the_module()
    {
        foreach (var role in Qms.Application.Auth.Roles.All)
        {
            var granted = Qms.Application.Auth.RolePermissionMap.For(role);
            foreach (var permission in granted)
            {
                var module = permission[..permission.LastIndexOf('.')];
                granted.ShouldContain(module + ".view", $"{role} holds {permission}");
            }
        }
    }

    [Fact]
    public void Every_granted_permission_is_in_the_catalog()
    {
        foreach (var role in Qms.Application.Auth.Roles.All)
        {
            foreach (var permission in Qms.Application.Auth.RolePermissionMap.For(role))
            {
                Qms.Application.Auth.Permissions.All.ShouldContain(permission);
            }
        }
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
