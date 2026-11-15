using System.Text.Json;
namespace Town.Core;
public sealed record RecipeIngredient(string Kind,string Label,int Amount);
public sealed record RecipeDefinition(string Id,string Title,string? Tool,RecipeIngredient[] Ingredients,RecipeIngredient Result,RecipeIngredient[]? Byproducts,string Description);
public static class RecipeBook
{
    public static readonly IReadOnlyList<RecipeDefinition> All=Load();
    private static RecipeDefinition[] Load()
    {
        using var stream=typeof(RecipeBook).Assembly.GetManifestResourceStream("Town.Core.Data.recipes.json")??throw new InvalidOperationException("Recipe data missing.");
        return JsonSerializer.Deserialize<RecipeDefinition[]>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Recipe data invalid.");
    }
}
public sealed record ProcessingRule(string Building,int Ticks,(string Item,int Amount)[] Inputs,(string Item,int Amount)[] Outputs);
public sealed partial class World
{
    public static readonly IReadOnlyDictionary<string,ProcessingRule> Processing=new Dictionary<string,ProcessingRule>{
        ["mill-wheat"]=new("mill",200,[("wheat",2)],[("flour",1)]),
        ["mill-rice"]=new("mill",200,[("rice",2)],[("milledRice",1)]),
        ["bake-bread"]=new("bakery",300,[("flour",2),("wood",1)],[("bread",2)]),
        ["roast-carrot"]=new("oven",200,[("vegetable",2)],[("roastedCarrot",1)]),
        ["grow-sapling"]=new("nursery",300,[("treeSeed",1)],[("sapling",1)]),
        ["make-fertilizer"]=new("compostBarrel",300,[("vegetable",2)],[("fertilizer",1)])};
}
