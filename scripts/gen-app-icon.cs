// The builder's application icon (the product disc, design-prompt §5).
// Usage: dotnet run scripts/gen-app-icon.cs -- src/Inlay.Builder/Inlay.ico
#:project ../src/Inlay.Imaging/Inlay.Imaging.csproj
#:property PublishAot=false
Inlay.Imaging.DiscIcon.Write(args[0]);
