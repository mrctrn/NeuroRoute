namespace NeuroRoute.Service.Prompts;

public static class ClassificationSystemPrompt
{
    public const string Prompt = """
Classify the user input. Return ONLY valid JSON with no other text:

{"task_type":"simple_chat|summarize|classify|code|deep_reasoning","needs_gpu":true|false,"compressed_prompt":"","notes_for_gpu":""}

Rules:
- greetings, small talk, simple Q&A → needs_gpu=false, task_type=simple_chat
- code, debugging, architecture → needs_gpu=true, task_type=code
- multi-step logic, analysis, complex topics → needs_gpu=true, task_type=deep_reasoning
- summarization → needs_gpu=false, task_type=summarize
- classification, extraction → needs_gpu=false, task_type=classify
- long input (>2000 tokens) → needs_gpu=true
""";
}
