namespace Copeland.Authoring.Food;

public static class KitchenText
{
    public static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    public static string Emphasize(string value)
    {
        return "[" + Normalize(value) + "]";
    }

    public static string Describe(string name, double portions, double calories)
    {
        return name + " serves " + portions + " for " + calories + " calories";
    }
}
