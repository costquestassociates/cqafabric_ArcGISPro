namespace CqaFabricPro;

internal sealed record IdentifyContext(string ApiKey, string Vintage, string Layer);

internal static class IdentifySession
{
    public static IdentifyContext? Current { get; set; }
}
