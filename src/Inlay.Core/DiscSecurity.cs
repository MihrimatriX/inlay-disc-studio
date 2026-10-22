namespace Inlay.Core;

public static class DiscSecurity
{
    /// <summary>§10. The builder writes it as a meta tag into every disc page; the launcher additionally answers
    /// every request outside disc.local with 403, so pages cannot reach the network even without it.</summary>
    public const string ContentSecurityPolicy =
        "default-src 'self' https://disc.local; script-src 'self' 'unsafe-inline' https://disc.local; " +
        "style-src 'self' 'unsafe-inline' https://disc.local; " +
        "img-src 'self' data: blob: https://disc.local; media-src https://disc.local blob:; connect-src https://disc.local; " +
        "frame-src 'none'; object-src 'none'";
}
