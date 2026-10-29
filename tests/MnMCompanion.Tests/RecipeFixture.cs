namespace MnMCompanion.Tests;

/// <summary>A small stand-in for the wiki's /api/tradeskills document, in its shape: skills with groups of recipes, and skills with none.</summary>
internal static class RecipeFixture
{
    public const string Json = """
    {
      "tradeskills": [
        { "id": "ts-mining", "name": "Mining", "station": "", "recipeGroups": [] },
        {
          "id": "ts-blacksmithing", "name": "Blacksmithing", "station": "Anvil", "tools": "Smithing Pliers, Smithing Hammer", "trainer": "Blacksmith Musafar",
          "recipeGroups": [
            { "name": "Components & Utilities", "recipes": [
              { "trivial": "5", "result": "Copper Chain Links", "result_qty": 1,
                "ingredients": [ { "qty": 6, "name": "Copper Bar" }, { "qty": 1, "name": "Smithing Pliers" } ],
                "extra": { "Notes": "Used in armor and Jewelcrafting." } },
              { "trivial": "5", "result": "Copper Fasteners", "result_qty": 1, "ingredients": [ { "name": "Copper Bar", "qty": 2 } ] },
              { "trivial": 60, "result": "Iron Fasteners", "result_qty": 4, "ingredients": [ { "name": "Iron Bar", "qty": 2 } ] },
              { "trivial": "5+", "result": "Bronze Rivets", "result_qty": 1, "ingredients": [ { "name": "Bronze Bar", "qty": 1, "or": "Zinc Bar" } ] },
              { "trivial": "?", "result": "Mystery Hinge", "result_qty": 1, "ingredients": [] },
              { "trivial": "-", "result": "Odd   Spaced Name", "result_qty": 1 }
            ] },
            { "name": "Weapons & Shields", "recipes": [
              { "trivial": "100", "result": "Iron Longsword", "result_qty": 1,
                "ingredients": [ { "name": "Iron Bar", "qty": 8 }, { "name": "Iron Fasteners", "qty": 2 } ],
                "extra": { "Station": "Anvil", "Slot": "Primary", "Notes": "-" } },
              { "trivial": "110", "result": "Iron Whammer", "result_qty": 1, "ingredients": [ { "name": "Iron Bar", "qty": 9 } ] }
            ] }
          ]
        },
        {
          "id": "ts-jewelcrafting", "name": "Jewelcrafting", "recipeGroups": [
            { "name": "Jewelry Catalog", "recipes": [
              { "trivial": "20", "result": "Jade Ring", "result_qty": 1,
                "ingredients": [ { "name": "Chipped Jade", "qty": 1 } ],
                "extra": { "Mold": "Crude Ring Mold", "AC": "-", "STR": "2", "STA": "-", "HP": "0", "WIS": "1", "Stats": "-" } }
            ] }
          ]
        },
        {
          "id": "ts-enchanting", "name": "Enchanting", "recipeGroups": [
            { "name": "Materials", "recipes": [
              { "trivial": "25", "result": "Powder", "result_qty": 1, "ingredients": [],
                "extra": { "Disenchants Into": "Arcane Powder x4�x5", "Stats": "+2 CHA, +1 WIS" } }
            ] }
          ]
        }
      ]
    }
    """;

    private static readonly Lazy<MnMCompanion.Recipes.RecipeBook> Parsed = new(() => MnMCompanion.Recipes.RecipeBook.TryParse(Json)!);

    /// <summary>The fixture, parsed (once).</summary>
    public static MnMCompanion.Recipes.RecipeBook Book => Parsed.Value;
}
