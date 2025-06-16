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

    public static async Task LoadSecretsAsync(IConfigurationManager configurationManager, string filterTag, string sectionName, ILogger? logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configurationManager, nameof(configurationManager));
        //ArgumentNullException.ThrowIfNull(filterTag, nameof(filterTag));

        logger ??= NullLogger.Instance;

        var baseUrl = configurationManager[_baseUrlKey];
        var token = configurationManager[_tokenKey];
        var vaultId = configurationManager[_vaultIdKey];

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(vaultId))
        {
            const string errorMessage = "Secrets are not configured correctly. Please check your configuration.";

            logger.LogError(errorMessage);
            throw new OnePasswordSetupException(errorMessage);
        }

        using(var httpClient = new HttpClient())
        {
            httpClient.BaseAddress = new Uri(baseUrl);
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            
            // 이렇게 얻는 아이템은 상세 내역이 포함되어 있지 않음
            var items = await GetVaultItemsAsync(httpClient, vaultId, filterTag, logger);
            // 상세 내역을 저장할 리스트
            var fullItems = new List<ItemDetail>();

            foreach (var item in items)
            {
                var itemDetail = await GetItemDetailsAsync(httpClient, vaultId, item.Id, null, logger);

                if(itemDetail != null)
                {
                    fullItems.Add(itemDetail);
                }
            }

            // Add all retrieved secrets to the configuration
            foreach (var item in fullItems)
            {
                Console.WriteLine(item.Title);
                
                // 1. Process fields
                var secretsDictionary = GetSecretDictionaryFromFields(item, sectionName, logger);
                
                foreach (var kvp in secretsDictionary)
                {
                    configurationManager[kvp.Key] = kvp.Value;
                    logger.LogDebug("Added secret with key: {Key}", kvp.Key);
                }

                var itemNotes = item.Fields.SingleOrDefault(field => field.Purpose == "NOTES");

                // 2. Process notes if available and in JSON markdown format
                if (itemNotes != null && !string.IsNullOrWhiteSpace(itemNotes.Value))
                {
                    // Check if the notes are in JSON markdown format
                    if (IsJsonMarkdownString(itemNotes.Value))
                    {
                        try
                        {
                            var newJsonConfigurationRoot  = SetJsonConfiguration(itemNotes.Value, logger);
                            
                            if(newJsonConfigurationRoot == null)
                            {
                                logger.LogWarning("Failed to parse JSON configuration from notes in item: {Title}", item.Title);
                                continue;
                            }
                            
                            configurationManager.AddConfiguration(newJsonConfigurationRoot);
                            
                            logger.LogDebug("Added JSON configuration from notes in item: {Title}", item.Title);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to process notes as JSON configuration in item: {Title}", item.Title);
                        }
                    }
                }
            }

            // Build and add the JSON configuration from notes
            //var jsonConfig = configBuilder.Build();

            //foreach (var jsonSource in configBuilder.Sources)
            //{
            //    //configurationManager.AddJsonStream(jsonSource.Build())
            //}

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
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private static async Task<IEnumerable<ItemDetail>> GetVaultItemsAsync(HttpClient httpClient, string vaultId, string filterTag, ILogger? logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClient, nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultId, nameof(vaultId));
        // ArgumentException.ThrowIfNullOrWhiteSpace(filterTag, nameof(filterTag));

        if (string.IsNullOrWhiteSpace(filterTag))
        {
            filterTag = string.Empty;
        }

        var itemListUrl = $"/v1/vaults/{vaultId}/items?tags={filterTag}";
        
        try
        {
            var response = await httpClient.GetAsync(itemListUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var items = JsonSerializer.Deserialize<ItemDetail[]>(content);

            if (items == null || items.Length == 0)
            {
                logger?.LogWarning("No items found with tag {FilterTag} in vault {VaultId}", filterTag, vaultId);
                return [];
            }
            
            logger?.LogDebug("Found {Count} items with tag {FilterTag} in vault {VaultId}", items.Length, filterTag, vaultId);
            return items;
        }
        catch (HttpRequestException ex)
        {
            var errorMessage = $"Failed to retrieve items with tag {filterTag} from vault {vaultId}";
            logger?.LogError(ex, errorMessage);
            throw new OnePasswordSetupException(errorMessage, ex);
        }
        catch (JsonException ex)
        {
            var errorMessage = $"Failed to deserialize response when retrieving items from vault {vaultId}";
            logger?.LogError(ex, errorMessage);
            throw new OnePasswordSetupException(errorMessage, ex);
        }
    }

    private static async Task<ItemDetail?> GetItemDetailsAsync(HttpClient httpClient, string vaultId, string itemId, Func<ItemDetail, bool>? filter, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient, nameof(httpClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId, nameof(itemId));

        var itemDetailUrl = $"/v1/vaults/{vaultId}/items/{itemId}";
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

    private static Dictionary<string, string> GetSecretDictionaryFromFields(ItemDetail item, string sectionName, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(item, nameof(item));        

        var secretDictionary = new Dictionary<string, string>();        

        if(SectionNameIsNotEmpty(sectionName) && !ExistsSectionNameInItem(item, sectionName))
        {
            return secretDictionary;    
        }        
        
        if(NoFieldsInItem(item))
        {
            logger?.LogWarning("Item with id {ItemId} does not have any fields", item.Id);
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

        bool ExistsSectionNameInItem(ItemDetail item, string sectionName)
        {
            try
            {
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (item.Sections == null)
                {
                    return false;
                }
                
                var existsMatchedSection = item.Sections.Any
                (
                    section =>
                        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                        section.Label != null &&
                        section.Label.Equals(sectionName, StringComparison.CurrentCultureIgnoreCase)
                );
                return existsMatchedSection;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
            
            
        } 
        
        bool NoFieldsInItem(ItemDetail item)
        {
            return item.Fields.Length == 0;
        }
        
        #endregion
    }

    private static IConfigurationRoot? SetJsonConfiguration(string jsonMarkdownString, ILogger? logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonMarkdownString, nameof(jsonMarkdownString));
        
        IConfigurationBuilder configuration = new ConfigurationBuilder();
        
        if (IsJsonMarkdownString(jsonMarkdownString) == false)
        {
            logger?.LogWarning($"the parameter '{nameof(jsonMarkdownString)}' is not Json Markdown");
            return null;
        }

        try
        {
            string jsonString = jsonMarkdownString.Substring(7, jsonMarkdownString.Length - 10).Trim();

            // ReSharper disable once ConvertToUsingDeclaration
            using (var jsonObject = JsonDocument.Parse(jsonString))
            using (var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(jsonString)))
            {
                jsonStream.Position = 0;
                configuration.AddJsonStream(jsonStream);

                return configuration.Build();
            }
        }
        catch (Exception ex)
        {
            var errorMessage = "Failed to parse JSON configuration string";
            logger?.LogError(errorMessage);
            throw new OnePasswordSetupException(errorMessage, ex);
        }
    }

    private static bool IsJsonMarkdownString(string targetString)
    {
        return (targetString.Trim().StartsWith("```json") && targetString.EndsWith("```"));
    }
}
