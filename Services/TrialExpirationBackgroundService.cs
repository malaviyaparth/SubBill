namespace SubBill.Services
{
    public class TrialExpirationBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TrialExpirationBackgroundService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

        public TrialExpirationBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<TrialExpirationBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("TrialExpirationBackgroundService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var subscriptionService = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();

                    var expiredCount = await subscriptionService.ProcessExpiredTrialsAsync();
                    if (expiredCount > 0)
                    {
                        _logger.LogInformation("TrialExpirationBackgroundService: Processed {Count} expired trial subscriptions.", expiredCount);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in TrialExpirationBackgroundService while checking expired trials.");
                }

                try
                {
                    await Task.Delay(_checkInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("TrialExpirationBackgroundService stopped.");
        }
    }
}
