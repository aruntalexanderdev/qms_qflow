using System.Reflection;

namespace Qms.Domain;

/// <summary>Marker used by tests and tooling to locate the Domain assembly.</summary>
public static class DomainAssembly
{
    public static Assembly Assembly => typeof(DomainAssembly).Assembly;
}
