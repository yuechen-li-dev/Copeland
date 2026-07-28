// Imported structural fragment used by the console dogfood proof.
export template BaseProject(): ProjectTree {
    emit(textFile("Copeland.Template.Console.csproj", `<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
`));
}
