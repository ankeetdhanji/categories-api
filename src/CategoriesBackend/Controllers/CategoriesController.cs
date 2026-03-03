using CategoriesBackend.Core.Interfaces;
using CategoriesBackend.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace CategoriesBackend.Controllers;

[ApiController]
[Route("api")]
public class CategoriesController(IUserPreferencesRepository userPreferencesRepository) : ControllerBase
{
    /// <summary>Returns the system default category list.</summary>
    [HttpGet("categories/defaults")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetDefaults() =>
        Ok(new { categories = GameSettings.DefaultCategories });

    /// <summary>Returns a player's saved categories, or 404 if none saved.</summary>
    [HttpGet("users/{playerId}/categories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserCategories(string playerId, CancellationToken ct)
    {
        var prefs = await userPreferencesRepository.GetAsync(playerId, ct);
        return prefs is null ? NotFound() : Ok(new { categories = prefs.SavedCategories });
    }

    /// <summary>Saves a player's preferred category list.</summary>
    [HttpPut("users/{playerId}/categories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveUserCategories(string playerId, [FromBody] SaveCategoriesRequest request, CancellationToken ct)
    {
        await userPreferencesRepository.SaveAsync(
            new UserPreferences { PlayerId = playerId, SavedCategories = request.Categories }, ct);
        return Ok();
    }
}

public record SaveCategoriesRequest(List<string> Categories);
