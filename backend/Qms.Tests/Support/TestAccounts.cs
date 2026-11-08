namespace Qms.Tests.Support;

/// <summary>Throw-away credentials of the test hosts. They exist only in the in-memory configuration of a test run.</summary>
public static class TestAccounts
{
    public const string JwtSigningKey = "test-only-signing-key-0123456789-abcdefghij-KLMNOP";
    public const string SuperAdminUserName = "test.superadmin";
    public const string SuperAdminPassword = "Sup3r-Test-Passw0rd!";
    public const string CompanyAdminUserName = "test.companyadmin";
    public const string CompanyAdminPassword = "Adm1n-Test-Passw0rd!";
}
