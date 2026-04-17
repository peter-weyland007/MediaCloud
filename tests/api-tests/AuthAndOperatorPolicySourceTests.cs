using Xunit;

public sealed class AuthAndOperatorPolicySourceTests
{
    [Fact]
    public void Api_defines_operator_policy_and_logout_route()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var programPath = Path.GetFullPath(Path.Combine(repoRoot, "apps/api/Program.cs"));
        var content = File.ReadAllText(programPath);

        Assert.Contains(".AddPolicy(\"OperatorOnly\", p => p.RequireRole(\"Admin\", \"User\"));", content);
        Assert.Contains("app.MapPost(\"/api/auth/logout\", () => Results.Ok()).RequireAuthorization();", content);
        Assert.Contains("RequireAuthorization(\"OperatorOnly\")", content);
    }
}
