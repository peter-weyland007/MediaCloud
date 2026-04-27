using Xunit;

public sealed class IntegrationDeleteConfirmationSourceTests
{
    [Fact]
    public void Integrations_page_uses_confirmation_dialog_before_delete_request_is_sent()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var pagePath = Path.GetFullPath(Path.Combine(repoRoot, "apps/web/Components/Pages/Integrations.razor"));
        var content = File.ReadAllText(pagePath);

        Assert.Contains("OpenDeleteIntegrationDialog(item)", content);
        Assert.Contains("<MudDialog Class=\"integration-dialog integration-delete-dialog\" @bind-Visible=\"_deleteIntegrationDialogVisible\"", content);
        Assert.Contains("Delete integration instance?", content);
        Assert.Contains("ConfirmDeleteIntegrationAsync", content);
        Assert.DoesNotContain("OnClick=\"@(() => DeleteIntegrationAsync(item))\"", content);
    }
}
