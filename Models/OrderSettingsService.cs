namespace CSE325_visioncoders.Services;

using System.Threading.Tasks;
using CSE325_visioncoders.Models;

public interface IOrderSettingsService
{
    Task<OrderViewSettings> GetAsync();
    Task UpdateAsync(OrderViewSettings s);
}

public class OrderSettingsService : IOrderSettingsService
{
    private OrderViewSettings _settings = new();

    public Task<OrderViewSettings> GetAsync() => Task.FromResult(_settings);

    public Task UpdateAsync(OrderViewSettings s)
    {
        _settings = s;
        return Task.CompletedTask;
    }
}