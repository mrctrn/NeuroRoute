using Microsoft.Extensions.Options;
using NeuroRoute.Service.Models;

namespace NeuroRoute.Service.Routing;

public sealed class NpuPlanner
{
    private readonly ITokenizer _tokenizer;
    private readonly NeuroRouteOptions _options;

    public NpuPlanner(ITokenizer tokenizer, IOptions<NeuroRouteOptions> options)
    {
        _tokenizer = tokenizer;
        _options = options.Value;
    }

    public async Task<NpuPlan> CreatePlanAsync(
        IReadOnlyList<ChatMessage> messages,
        Func<string, Task<NpuPlan>> classifyAsync)
    {
        var fullText = string.Join("\n", messages.Select(m => m.Content));
        var fullTokens = _tokenizer.CountTokens(fullText);

        if (fullTokens <= _options.NpuLimit)
        {
            var npuPlan = await classifyAsync(fullText);

            npuPlan.EstimatedTokens = fullTokens;

            if (!string.IsNullOrWhiteSpace(npuPlan.CompressedPrompt))
            {
                npuPlan.RoutingCase = "C";
            }
            else if (npuPlan.NeedsGpu && !string.IsNullOrWhiteSpace(npuPlan.NotesForGpu))
            {
                npuPlan.RoutingCase = "D";
                npuPlan.CompressedPrompt = npuPlan.NotesForGpu + "\n" + fullText;
            }
            else
            {
                npuPlan.RoutingCase = "A";
            }

            return npuPlan;
        }
        else
        {
            var truncatedWords = _tokenizer.TruncateToLastNTokens(fullText, _options.NpuSlice);
            var npuInput = string.Join(" ", truncatedWords);

            var npuPlan = await classifyAsync(npuInput);
            npuPlan.NeedsGpu = true;
            npuPlan.EstimatedTokens = fullTokens;
            npuPlan.CompressedPrompt = fullText;
            npuPlan.RoutingCase = "B";

            return npuPlan;
        }
    }
}
