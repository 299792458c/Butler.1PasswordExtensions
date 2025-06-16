using System.Diagnostics;
using Butler.OnePasswordExtensions;
using SampleService;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging();

var loggerFactory = builder.Services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger("Startup");

// 로컬 개발환경을 기본적으로 디버거가 연결되어 있다고 가정
if (Debugger.IsAttached || builder.Environment.IsDevelopment())
{
    // 로컬 개발환경에서 UserSecrets를 로드
    await LoadUserSecretsAsync(builder.Configuration);
    logger.LogInformation("User secrets loaded for development environment");
}

await SecretsLoader.LoadSecretsAsync(builder.Configuration, filterTag: string.Empty, sectionName: "configurations", logger: logger);

builder.Services.AddOptions<DbConnectionOptions>().Bind(builder.Configuration.GetSection("ConnectionStrings"));

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();

async Task LoadUserSecretsAsync(IConfiguration configuration)
{
    var url = configuration.GetValue<string>(ConfigurationStringDefaultKeys.BaseUrl);
    var token = configuration.GetValue<string>(ConfigurationStringDefaultKeys.Token);
    var vaultId = configuration.GetValue<string>(ConfigurationStringDefaultKeys.VaultId);

    if (string.IsNullOrWhiteSpace(url))
    {
        throw new OnePasswordSetupException(nameof(url), new ArgumentNullException());
    }

    if (string.IsNullOrWhiteSpace(token))
    {
        throw new OnePasswordSetupException(nameof(token), new ArgumentNullException());
    }
    
    if(string.IsNullOrWhiteSpace(vaultId))
    {
        throw new OnePasswordSetupException(nameof(vaultId), new ArgumentNullException());
    }

    using var httpClient = new HttpClient();
    httpClient.BaseAddress = new Uri(url);
    httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

    try
    {
        // 1Password Connect API: GET /v1/vaults/{vaultId}
        var response = await httpClient.GetAsync($"/v1/vaults/{vaultId}");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogError("Vault가 존재하지 않습니다");
            throw new OnePasswordSetupException("VaultNotFound", new Exception("Vault가 존재하지 않습니다."));
        }
        else if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            logger.LogInformation("1Password Connect API 응답: {Content}", content);
        }
        else
        {
            logger.LogError("1Password Connect API 요청 중 오류 발생: 비정상 응답 코드 {StatusCode}", response.StatusCode);
            throw new OnePasswordSetupException("ApiRequestError");
        }
    }
    catch (HttpRequestException ex)
    {
        logger.LogError("1Password Connect API 요청 중 오류 발생: {Message}", ex.Message);
        throw new OnePasswordSetupException("ApiRequestError", ex);
    }
}

public class DbConnectionOptions
{
    public string? DefaultConnection { get; set; }
    public string? PatientConnection { get; set; }
    public string? IdentityConnection { get; set; }
    public string? PaymentConnection { get; set; }
}