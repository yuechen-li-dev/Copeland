using Copeland.Authoring.Food.Copeland;

string summary = Main.Run(" lentil stew ", 4, 560);
double[] portions = Main.DoubledPortions([1, 2, 3]);
double slotTotal = Main.CookingSlotTotal(3);
double plannedPortions = Main.PlanPortions(4);

Console.WriteLine(summary);
Console.WriteLine(string.Join(",", portions));
Console.WriteLine(slotTotal);
Console.WriteLine(plannedPortions);
