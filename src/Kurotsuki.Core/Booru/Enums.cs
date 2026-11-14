namespace Kurotsuki.Core.Booru;

// Member names serialise as SCREAMING_SNAKE_CASE (JsonNamingPolicy.SnakeCaseUpper) so config.json
// stays byte-compatible with the JavaFX app, which wrote the Java enum names.
public enum BooruEngine
{
    GelbooruFamily,
    DanbooruSoftware,
    MoebooruShimmie,
    Szurubooru,
    E621,
    Hydrus,
}

public enum BooruFlavor
{
    Moebooru,
    Shimmie,
}

public enum BooruTemplate
{
    Gelbooru,
    Safebooru,
    Danbooru,
    Yandere,
    Konachan,
    E621,
    Hydrus,
}

public enum TagCategory
{
    Artist,
    Copyright,
    Character,
    General,
    Meta,
    Other,
}

public static class EnumInfo
{
    public static string Label(this BooruEngine engine) => engine switch
    {
        BooruEngine.GelbooruFamily => "Gelbooru Family",
        BooruEngine.DanbooruSoftware => "Danbooru Software",
        BooruEngine.MoebooruShimmie => "Moebooru / Shimmie Family",
        BooruEngine.Szurubooru => "Szurubooru",
        BooruEngine.E621 => "e621 / e621ng",
        BooruEngine.Hydrus => "Hydrus Network",
        _ => engine.ToString(),
    };

    public static string Label(this BooruTemplate template) => template switch
    {
        BooruTemplate.Gelbooru => "Gelbooru",
        BooruTemplate.Safebooru => "Safebooru",
        BooruTemplate.Danbooru => "Danbooru",
        BooruTemplate.Yandere => "yande.re",
        BooruTemplate.Konachan => "Konachan",
        BooruTemplate.E621 => "e621",
        BooruTemplate.Hydrus => "Hydrus Network",
        _ => template.ToString(),
    };

    public static BooruEngine Engine(this BooruTemplate template) => template switch
    {
        BooruTemplate.Gelbooru or BooruTemplate.Safebooru => BooruEngine.GelbooruFamily,
        BooruTemplate.Danbooru => BooruEngine.DanbooruSoftware,
        BooruTemplate.Yandere or BooruTemplate.Konachan => BooruEngine.MoebooruShimmie,
        BooruTemplate.E621 => BooruEngine.E621,
        BooruTemplate.Hydrus => BooruEngine.Hydrus,
        _ => throw new ArgumentOutOfRangeException(nameof(template)),
    };

    public static BooruFlavor? Flavor(this BooruTemplate template) => template switch
    {
        BooruTemplate.Yandere or BooruTemplate.Konachan => BooruFlavor.Moebooru,
        _ => null,
    };

    public static string DefaultBaseUrl(this BooruTemplate template) => template switch
    {
        BooruTemplate.Gelbooru => "https://gelbooru.com",
        BooruTemplate.Safebooru => "https://safebooru.org",
        BooruTemplate.Danbooru => "https://danbooru.donmai.us",
        BooruTemplate.Yandere => "https://yande.re",
        BooruTemplate.Konachan => "https://konachan.com",
        BooruTemplate.E621 => "https://e621.net",
        BooruTemplate.Hydrus => "http://127.0.0.1:45869",
        _ => throw new ArgumentOutOfRangeException(nameof(template)),
    };

    public static TagCategory TagCategoryFromName(string name) => name switch
    {
        "artist" => TagCategory.Artist,
        "copyright" => TagCategory.Copyright,
        "character" => TagCategory.Character,
        "general" => TagCategory.General,
        "meta" => TagCategory.Meta,
        _ => TagCategory.Other,
    };
}
