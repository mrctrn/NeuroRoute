namespace NeuroRoute.Service.Npu;

public sealed record FlmStatus(
    string Status,
    string? Message,
    string ModelTag,
    string Host,
    int Port,
    int CtxLen,
    string Pmode,
    int? Pid,
    DateTime? StartedAt
);
