using System.Text;
using NeuroRoute.Service.Models;

namespace NeuroRoute.Service.Routing;

public sealed class PromptBuilder
{
    public string BuildChatPrompt(IReadOnlyList<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            sb.AppendLine($"<|{msg.Role}|>\n{msg.Content}\n<|end|>");
        }
        sb.AppendLine("<|assistant|>\n");
        return sb.ToString();
    }
}
