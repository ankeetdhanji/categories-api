using CategoriesBackend.Core.Models;

namespace CategoriesBackend.Core.Interfaces;

public interface IRoundManager
{
    Task StartRoundAsync(string gameId, CancellationToken ct = default);
    Task SubmitAnswersAsync(string gameId, string playerId, Dictionary<string, string> answers, CancellationToken ct = default);
    Task EndRoundAsync(string gameId, CancellationToken ct = default);
    Task<Round> GetCurrentRoundAsync(string gameId, CancellationToken ct = default);
}
