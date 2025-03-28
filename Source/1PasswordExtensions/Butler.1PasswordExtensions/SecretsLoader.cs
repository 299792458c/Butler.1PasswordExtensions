using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

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

    public static async Task LoadSecrets(IConfigurationManager configurationManager, string filterTag, string sectionName, ILogger logger)
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

            logger.LogError(errorMessage);
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
                var secretsDictionary = GetSecretDictionaryFromFields(item,sectionName, logger);
                
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
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultId, nameof(vaultId));
        ArgumentException.ThrowIfNullOrWhiteSpace(filterTag, nameof(filterTag));

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
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId, nameof(itemId));

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

    private static Dictionary<string, string> GetSecretDictionaryFromFields(ItemDetail item, string sectionName, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(item, nameof(item));        

        var secretDictionary = new Dictionary<string, string>();        

        if(SectionNameIsNotEmpty(sectionName) && NotExistsSectionNameInItem(item, sectionName))
        {
            return secretDictionary;    
        }        
        
        if(item.Fields == null || item.Fields.Length == 0)
        {
            var errorMessage = $"Item with id {item.Id} does not have any fields";

            logger?.LogWarning(errorMessage);
            return secretDictionary;
        }

        foreach (var field in item.Fields)
        {
            var key = $"{item.Title}:{field.Label}";
            var value = field.Value;

            if(string.IsNullOrWhiteSpace(key))
                continue;            

            secretDictionary.Add(key, value);
        }

        return secretDictionary;

        #region Local Methods
        bool SectionNameIsNotEmpty(string sectionName)
        {
            return string.IsNullOrWhiteSpace(sectionName) == false;
        }

        bool NotExistsSectionNameInItem(ItemDetail item, string sectionName)
        {
            return item.Sections.Any(section => section.Label == sectionName);
        } 
        #endregion
    }

    private static void SetJsonConfiguration(IConfigurationBuilder configuration, string jsonMarkdownString, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(configuration, nameof(configuration));
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonMarkdownString, nameof(jsonMarkdownString));

        if(IsNotJsonMarkdownString(jsonMarkdownString))
        {
            logger?.LogWarning($"the parameter '{nameof(jsonMarkdownString)}' is not Json Markdown");
            return;
        }

        try
        {
            string jsonString = jsonMarkdownString.Substring(7, jsonMarkdownString.Length - 10).Trim();

            using (var jsonObject = JsonDocument.Parse(jsonString))
            using (var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonString)))
            {
                jsonStream.Position = 0;
                configuration.AddJsonStream(jsonStream);
            }
        }
        catch (Exception ex)
        {
            var errorMessage = "Failed to parse JSON configuration string";
            logger?.LogError(errorMessage);
            throw new OnePasswordSetupException(errorMessage, ex);
        }

        #region Local Methods
        bool IsNotJsonMarkdownString(string targetString)
        {
            return (targetString.StartsWith("```json") && targetString.EndsWith("```")) == false;
        } 
        #endregion
    }
}
