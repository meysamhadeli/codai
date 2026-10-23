using System.Text.Json;
using Codai.Configuration;
using Codai.Core.Abstractions;

namespace Codai.Application;

public sealed class ConnectionSetupWorkflow(
    IUserInterface ui,
    CodaiConfigurationManager manager,
    string configurationFile,
    string configurationDirectory,
    AiProviderOptions current,
    Func<ProviderDescriptor, string, string?, CancellationToken, Task<IReadOnlyList<string>>>? discoverModels = null,
    Func<CancellationToken, Task<bool>>? signIn = null)
{
    public async Task<bool> RunAsync(CancellationToken cancellationToken = default)
    {
        var step = Step.Connection;
        var category = string.Empty;
        ProviderDescriptor? provider = null;
        var endpoint = string.Empty;
        var credential = string.Empty;
        var model = string.Empty;
        List<string>? models = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (step)
            {
                case Step.Connection:
                    var connection = await ui.ChooseAsync("Codai / Connect a model",
                    [
                        new("Use your own provider", "API credentials or local models", "provider"),
                        new("Continue with ChatGPT", "Use your account plan for eligible requests", "account"),
                    ], cancellationToken).ConfigureAwait(false);
                    if (connection == "account")
                    {
                        if (signIn is null)
                        {
                            ui.WriteError("Account connection is unavailable in this host.");
                        }
                        else if (await signIn(cancellationToken).ConfigureAwait(false))
                        {
                            return true;
                        }
                        break;
                    }
                    if (connection != "provider")
                    {
                        return false;
                    }
                    step = Step.ProviderKind;
                    break;
                case Step.ProviderKind:
                    category = await ui.ChooseAsync("Codai / Your provider",
                    [
                        new("Hosted provider", "Connect using a provider credential", "hosted"),
                        new("Local server", "Models running on this machine", "local"),
                    ], cancellationToken).ConfigureAwait(false) ?? string.Empty;
                    if (category is not ("local" or "hosted"))
                    {
                        step = Step.Connection;
                        break;
                    }
                    step = Step.Provider;
                    break;
                case Step.Provider:
                    var providers = ProviderCatalog.All.Where(item => category == "local" ? !item.RequiresApiKey : item.RequiresApiKey).ToArray();
                    var selected = await ui.ChooseAsync("Codai / Provider",
                        providers.Select(item => new SessionMenuItem(item.Name, string.Empty, item.Name)).ToArray(), cancellationToken).ConfigureAwait(false);
                    provider = providers.FirstOrDefault(item => item.Name == selected);
                    if (provider is null)
                    {
                        step = Step.ProviderKind;
                        break;
                    }
                    endpoint = provider.Name == current.Provider && !string.IsNullOrWhiteSpace(current.BaseUrl)
                        ? current.BaseUrl : provider.DefaultBaseUrl.Contains('{', StringComparison.Ordinal) ? string.Empty : provider.DefaultBaseUrl;
                    step = Step.Endpoint;
                    break;
                case Step.Endpoint:
                    var address = await ui.ReadValueAsync("Codai / Server address", endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (address is null)
                    {
                        step = Step.Provider;
                        break;
                    }
                    if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
                        uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.Host.Contains('{', StringComparison.Ordinal))
                    {
                        ui.WriteError("Enter an HTTP or HTTPS server address without credentials, query, or fragment.");
                        break;
                    }
                    endpoint = address.Trim().TrimEnd('/');
                    credential = provider!.Name == current.Provider &&
                        string.Equals(endpoint, (current.BaseUrl ?? provider.DefaultBaseUrl).TrimEnd('/'), StringComparison.Ordinal)
                        ? current.ApiKey ?? string.Empty : string.Empty;
                    models = null;
                    step = Step.Credential;
                    break;
                case Step.Credential:
                    var optional = category == "local";
                    var key = await ui.ReadValueAsync(optional ? "Codai / Credential (optional)" : "Codai / Provider credential",
                        credential, secret: true, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (key is null)
                    {
                        step = Step.Endpoint;
                        break;
                    }
                    if (!optional && string.IsNullOrWhiteSpace(key))
                    {
                        ui.WriteError("This provider requires a credential.");
                        break;
                    }
                    if (key.Any(char.IsControl))
                    {
                        ui.WriteError("Credential cannot contain control characters.");
                        break;
                    }
                    if (key.Length > 0 && new Uri(endpoint).Scheme != "https" && !new Uri(endpoint).IsLoopback)
                    {
                        ui.WriteError("Use HTTPS when sending credentials to a remote endpoint.");
                        step = Step.Endpoint;
                        break;
                    }
                    credential = key;
                    models = null;
                    step = Step.Model;
                    break;
                case Step.Model:
                    if (models is null)
                    {
                        models = [];
                        if (discoverModels is not null)
                        {
                            try
                            {
                                var available = await ui.RunWithProgressAsync("Loading models...",
                                    token => discoverModels(provider!, endpoint, credential, token), cancellationToken).ConfigureAwait(false);
                                models.AddRange(available.Where(IsModelIdentifier));
                            }
                            catch (Exception exception) when (exception is HttpRequestException or JsonException ||
                                exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                            {
                                ui.WriteInfo("Model listing unavailable. A default or custom model can still be selected.");
                            }
                        }
                        if (provider!.Name == current.Provider && IsModelIdentifier(current.Model))
                        {
                            models.Add(current.Model);
                        }
                        models.Add(provider!.DefaultModel);
                        models = models.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                    }
                    var choices = models.Select(item => new SessionMenuItem(item, item == provider!.DefaultModel ? "Provider default" : "Available model", "model:" + item))
                        .Append(new SessionMenuItem("Custom model ID", "Use a model or deployment identifier", "manual")).ToArray();
                    var choice = await ui.ChooseAsync("Codai / Model", choices, cancellationToken).ConfigureAwait(false);
                    if (choice == "manual")
                    {
                        step = Step.CustomModel;
                    }
                    else if (choice is not null && models.Contains(choice.StartsWith("model:", StringComparison.Ordinal) ? choice[6..] : string.Empty, StringComparer.Ordinal))
                    {
                        model = choice[6..];
                        step = Step.Save;
                    }
                    else
                    {
                        step = Step.Credential;
                    }
                    break;
                case Step.CustomModel:
                    var identifier = await ui.ReadValueAsync("Codai / Custom model ID", model, cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (identifier is null)
                    {
                        step = Step.Model;
                    }
                    else if (!IsModelIdentifier(identifier.Trim()))
                    {
                        ui.WriteError("Enter a nonempty model identifier without whitespace.");
                    }
                    else
                    {
                        model = identifier.Trim();
                        step = Step.Save;
                    }
                    break;
                case Step.Save:
                    var action = await ui.ChooseAsync("Codai / Save connection",
                    [
                        new("Save " + model, credential.Length > 0 ? "Credential will be stored locally as plaintext" : "Save provider and model settings", "save"),
                        new("Change model", "Return to model selection", "back"),
                        new("Discard setup", "Keep existing configuration unchanged", "cancel"),
                    ], cancellationToken).ConfigureAwait(false);
                    if (action is null or "back")
                    {
                        step = Step.Model;
                        break;
                    }
                    if (action != "save")
                    {
                        return false;
                    }
                    await manager.SetValuesAsync(configurationFile, new Dictionary<string, string>
                    {
                        ["ai_provider_config.provider"] = provider!.Name,
                        ["ai_provider_config.base_url"] = endpoint,
                        ["ai_provider_config.api_key"] = credential,
                        ["ai_provider_config.account"] = string.Empty,
                        ["ai_provider_config.model"] = model,
                    }, configurationDirectory, cancellationToken).ConfigureAwait(false);
                    ui.WriteSuccess("Connection settings saved. Run 'codai code' to open your workspace.");
                    return true;
            }
        }
    }

    private static bool IsModelIdentifier(string? value) => !string.IsNullOrWhiteSpace(value) &&
        !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) && value.Length <= 512;

    private enum Step { Connection, ProviderKind, Provider, Endpoint, Credential, Model, CustomModel, Save }
}
