namespace NeuroRoute.Service.Routing;

public sealed class ApproximateTokenizer : ITokenizer
{
    public int CountTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return (int)(text.Length / 3.7) + 1;
    }

    public IReadOnlyList<string> TruncateToLastNTokens(string text, int n)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= n)
            return words;

        return words[^n..];
    }
}
