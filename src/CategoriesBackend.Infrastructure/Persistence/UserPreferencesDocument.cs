using CategoriesBackend.Core.Models;
using Google.Cloud.Firestore;

namespace CategoriesBackend.Infrastructure.Persistence;

[FirestoreData]
internal class UserPreferencesDocument
{
    [FirestoreProperty] public List<string> SavedCategories { get; set; } = [];

    public static UserPreferencesDocument From(UserPreferences up) => new() { SavedCategories = [..up.SavedCategories] };

    public UserPreferences ToUserPreferences(string playerId) => new()
    {
        PlayerId = playerId,
        SavedCategories = [..SavedCategories],
    };
}
