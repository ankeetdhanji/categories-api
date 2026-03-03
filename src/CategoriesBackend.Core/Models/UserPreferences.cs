namespace CategoriesBackend.Core.Models;

public class UserPreferences
{
    public string PlayerId { get; set; } = string.Empty;
    public List<string> SavedCategories { get; set; } = [];
}
