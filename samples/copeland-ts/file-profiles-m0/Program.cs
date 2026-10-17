int answer = Copeland.FileProfiles.M0.Copeland.Main.answer();
if (answer != 42)
{
    throw new InvalidOperationException($"Expected 42, got {answer}.");
}
Console.WriteLine($"PROFILE_DOTNET_PASSED answer={answer}");
