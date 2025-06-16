using Microsoft.Extensions.Options;

namespace SampleService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfiguration _configuration;
    private readonly DbConnectionOptions _options;

    public Worker(ILogger<Worker> logger, IOptions<DbConnectionOptions> options, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _options = options.Value;
        
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("Worker running");
        Console.WriteLine(_options.DefaultConnection);
    }
}