using System.ComponentModel;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Codai.Configuration;
using Codai.Core.Abstractions;
using Codai.Providers;
using Codai.Providers.Accounts;

namespace Codai.Application;

public sealed class AccountConnectionWorkflow(
    IUserInterface ui,
    CodaiConfigurationManager manager,
    string configurationFile,
    string configurationDirectory,
    AccountCredentialStore store,
    AccountAuthorizationClient authorization,
    Func<AccountSession?, CancellationToken, Task<AccountSession?>>? authorize = null)
{
    public async Task<bool> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var accounts = await store.ListAsync(cancellationToken).ConfigureAwait(false);
            AccountSession? previous = null;
            if (accounts.Count > 0)
            {
                var selected = await ui.ChooseAsync("Codai / Account", accounts.Select(account =>
                    new SessionMenuItem(account.Email ?? "Saved account", "Registration " + account.Id[..8], account.Id))
                    .Append(new SessionMenuItem("Add account", "Authorize a different account or workspace", "new")).ToArray(), cancellationToken).ConfigureAwait(false);
                if (selected is null)
                {
                    return false;
                }
                previous = accounts.FirstOrDefault(account => account.Id == selected);
                if (previous is not null)
                {
                    var action = await ui.ChooseAsync("Codai / Account connection",
                    [
                        new("Continue", "Authorize this account in your browser", "continue"),
                        new("Sign out", "End this account's saved session", "out"),
                    ], cancellationToken).ConfigureAwait(false);
                    if (action == "out")
                    {
                        await SignOutAsync(previous, cancellationToken).ConfigureAwait(false);
                        return false;
                    }
                    if (action != "continue")
                    {
                        return false;
                    }
                }
                else if (selected != "new")
                {
                    return false;
                }
            }
            var session = await ui.RunWithProgressAsync("Account authorization", async token =>
                authorize is not null
                    ? await authorize(previous, token).ConfigureAwait(false)
                    : await authorization.AuthorizeAsync(await store.GetHostIdAsync(token).ConfigureAwait(false), previous, token).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                ui.WriteInfo("Account authorization was declined. Existing connection is unchanged.");
                return false;
            }
            var models = await ui.RunWithProgressAsync("Account models", token => authorization.ListModelsAsync(session, token), cancellationToken).ConfigureAwait(false);
            if (models.Count == 0)
            {
                ui.WriteError("Account returned no selectable models. Check plan access, then retry or use your own provider.");
                return false;
            }
            while (true)
            {
                var chosen = await ui.ChooseAsync("Codai / Account model", models.Select(model =>
                    new SessionMenuItem(model.DisplayName, model.Id, model.Id)).ToArray(), cancellationToken).ConfigureAwait(false);
                var model = models.FirstOrDefault(model => model.Id == chosen);
                if (model is null)
                {
                    return false;
                }
                var save = await ui.ChooseAsync("Codai / Save account connection",
                [
                    new("Save", model.DisplayName + ": Use account plan; credentials stay in protected user storage", "save"),
                    new("Cancel", "Keep existing connection unchanged", "cancel"),
                ], cancellationToken).ConfigureAwait(false);
                if (save is null)
                {
                    continue;
                }
                if (save != "save")
                {
                    return false;
                }
                using (await store.LockAsync(session.Id, cancellationToken).ConfigureAwait(false))
                {
                    await store.SaveAsync(session, cancellationToken).ConfigureAwait(false);
                }
                await manager.SetValuesAsync(configurationFile, new Dictionary<string, string>
                {
                    ["ai_provider_config.provider"] = ProviderCatalog.OpenAi,
                    ["ai_provider_config.base_url"] = AccountAuthorizationClient.Resource,
                    ["ai_provider_config.model"] = model.Id,
                    ["ai_provider_config.account"] = session.Id,
                    ["ai_provider_config.api_key"] = string.Empty,
                    ["ai_provider_config.stream"] = "true",
                }, configurationDirectory, cancellationToken).ConfigureAwait(false);
                ui.WriteSuccess("Account plan connected. Run 'codai code' to start. Manage plan usage: https://chatgpt.com/settings/usage");
                return true;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ui.WriteInfo("Account sign-in cancelled or timed out. Existing connection is unchanged.");
            return false;
        }
        catch (ProviderConfigurationException exception)
        {
            ui.WriteError(exception.Message);
            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or HttpListenerException or JsonException or
            CryptographicException or Win32Exception or InvalidOperationException)
        {
            ui.WriteError("Account connection could not complete. Check your network and browser, then retry.");
            return false;
        }
    }

    private async Task SignOutAsync(AccountSession account, CancellationToken cancellationToken)
    {
        using var gate = await store.LockAsync(account.Id, cancellationToken).ConfigureAwait(false);
        account = await store.ReadAsync(account.Id, cancellationToken).ConfigureAwait(false) ?? account;
        var revoked = false;
        try
        {
            revoked = await ui.RunWithProgressAsync("Account sign-out", token => authorization.RevokeAsync(account, token), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await store.SaveAsync(new AccountSession
            {
                ClientId = account.ClientId, Subject = account.Subject, Email = account.Email,
                AccessToken = string.Empty, RefreshToken = string.Empty, IdToken = string.Empty, Scopes = [],
            }, CancellationToken.None).ConfigureAwait(false);
            ui.WriteInfo(revoked ? "Account signed out." : "Signed out locally; remote revocation was not confirmed. Disconnect Codai in account settings.");
        }
    }
}