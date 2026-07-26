using Copeland.Authoring.Food.Copeland;

string summary = RecipeBook.BuildDailySummary(" lentil stew ", 4, 560);
double[] portions = Planning.DoublePortions([1, 2, 3]);
double slotTotal = Planning.SumCookingSlots(3);
double inferredPlanPortions = Planning.PlannedPortions(4);
double explicitPlanPortions = Planning.ExplicitPlannedPortions(5);

var pantry = PantryRun.Start();
pantry.SendAdd(2);
pantry.SendAdd(3);
pantry.SendClose();

Console.WriteLine(summary);
Console.WriteLine(string.Join(",", portions));
Console.WriteLine(slotTotal);
Console.WriteLine($"{inferredPlanPortions}:{explicitPlanPortions}");
Console.WriteLine($"{pantry.State}:{pantry.Revision}");
