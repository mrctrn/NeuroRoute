namespace NeuroRoute.Service.Routing;

public interface ITokenizer
{
    int CountTokens(string text);
    IReadOnlyList<string> TruncateToLastNTokens(string text, int n);
}
