using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Butler.OnePasswordExtensions;

public static class SecretsLoader
{
    private static string _baseUrlKey = ConfigurationStringDefaultKeys.BaseUrl;
    private static string _tokenKey = ConfigurationStringDefaultKeys.Token;
    private static string _vaultIdKey = ConfigurationStringDefaultKeys.VaultId;

    public static void SetConfigurationKeys(string baseUrlKey, string tokenKey, string vaultIdKey)
    {
        ArgumentNullException.ThrowIfNull(baseUrlKey, nameof(baseUrlKey));
        ArgumentNullException.ThrowIfNull(tokenKey, nameof(tokenKey));
        ArgumentNullException.ThrowIfNull(vaultIdKey, nameof(vaultIdKey));

        _baseUrlKey = baseUrlKey;
        _tokenKey = tokenKey;
        _vaultIdKey = vaultIdKey;
    }

    public static async Task LoadSecrets(IConfigurationManager configurationManager, string filterTag, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(configurationManager, nameof(configurationManager));
        ArgumentNullException.ThrowIfNull(filterTag, nameof(filterTag));

        logger ??= NullLogger.Instance;

        var baseUrl = configurationManager[_baseUrlKey];
        var token = configurationManager[_tokenKey];
        var vaultId = configurationManager[_vaultIdKey];

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(vaultId))
        {
            var errorMessage = "Secrets are not configured correctly. Please check your configuration.";

            logger?.LogError(errorMessage);
            throw new OnePasswordSetupException(errorMessage);
        }

        using(var httpClient = new HttpClient())
        {
            httpClient.BaseAddress = new Uri(baseUrl);
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            
            var items = await GetVaultItemsAsync(httpClient, vaultId, filterTag, logger);

            var fullItems = new List<ItemDetail>();

            foreach (var item in items)
            {
                var itemDetail = await GetItemDetailsAsync(httpClient, item.Id, null, logger);

                if(itemDetail != null)
                {
                    fullItems.Add(itemDetail);
                }
            }
            
            // Add all retrieved secrets to the configuration
            foreach (var item in fullItems)
            {
                var secretsDictionary = GetSecretDictionaryFromFields(item, logger);
                
                foreach (var kvp in secretsDictionary)
                {
                    configurationManager[kvp.Key] = kvp.Value;
                    logger.LogDebug("Added secret with key: {Key}", kvp.Key);
                }
            }
            
            logger.LogInformation("Successfully loaded {Count} secrets from 1Password vault", fullItems.Count);
        }
    }

    /// <summary>
    /// Get all items from the vault that have the specified tag
    /// </summary>
    /// <param name="httpClient"></param>
    /// <param name="vaultId"></param>
    /// <param name="filterTag"></param>
    /// <param name="logger"></param>
    /// <returns></returns>
    private static async Task<IEnumerable<ItemDetail>> GetVaultItemsAsync(HttpClient httpClient, string vaultId, string filterTag, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient, nameof(httpClient));
        ArgumentNullException.ThrowIfNullOrWhiteSpace(vaultId, nameof(vaultId));
        ArgumentNullException.ThrowIfNullOrWhiteSpace(filterTag, nameof(filterTag));

        var itemListUrl = $"/v1/vaults/{vaultId}/items?tags={filterTag}";
        var response = await httpClient.GetAsync(itemListUrl);

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var items = JsonSerializer.Deserialize<ItemDetail[]>(content);

        return items ?? Enumerable.Empty<ItemDetail>();
    }


    private static async Task<ItemDetail?> GetItemDetailsAsync(HttpClient httpClient, string itemId, Func<ItemDetail, bool>? filter, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient, nameof(httpClient));
        ArgumentNullException.ThrowIfNullOrWhiteSpace(itemId, nameof(itemId));

        var itemDetailUrl = $"/v1/items/{itemId}";
        var response = await httpClient.GetAsync(itemDetailUrl);

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var itemDetail = JsonSerializer.Deserialize<ItemDetail>(content);

        if(itemDetail == null)
        {
            var errorMessage = $"Failed to get item details for item with id {itemId}";

            logger?.LogError(errorMessage);
            throw new OnePasswordSetupException(errorMessage);
        }

        // If the filter is provided and the item does not pass the filter, return null
        if(filter != null && !filter(itemDetail))
        {
            return null;
        }

        return itemDetail;
    }

    private static Dictionary<string, string> GetSecretDictionaryFromFields(ItemDetail item, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(item, nameof(item));
        
        var secretDictionary = new Dictionary<string, string>();

        if(item.Fields == null || item.Fields.Length == 0)
        {
            var errorMessage = $"Item with id {item.Id} does not have any fields";

            logger?.LogWarning(errorMessage);
            return secretDictionary;
        }

        foreach (var field in item.Fields)
        {
            if (field.Type == "T" || field.Type == "P")
            {
                secretDictionary[field.Label] = field.Value;
            }
            else if (field.Type == "A")
            {
                var section = field.Section;
                if (section != null)
                {
                    secretDictionary[section.Label] = field.Value;
                }
            }
        }

        return secretDictionary;
    }
}
