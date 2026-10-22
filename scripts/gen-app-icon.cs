// The builder's application icon (the product disc, design-prompt §5).
// Usage: dotnet run scripts/gen-app-icon.cs -- src/Inlay.Builder/Inlay.ico
//        dotnet run scripts/gen-app-icon.cs -- --png 150 Square150x150Logo.png   (MSIX tile assets)
#:project ../src/Inlay.Imaging/Inlay.Imaging.csproj
#:property PublishAot=false
if (args is ["--png", var size, var output])
{
    var full = Path.GetTempFileName();
    Inlay.Imaging.DiscIcon.WriteDefaultPng(full);
    using var image = new ImageMagick.MagickImage(full);
    image.Resize(uint.Parse(size), uint.Parse(size));
    image.Write(output, ImageMagick.MagickFormat.Png);
    File.Delete(full);
}
else Inlay.Imaging.DiscIcon.Write(args[0]);
