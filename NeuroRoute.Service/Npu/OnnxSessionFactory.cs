using Microsoft.ML.OnnxRuntime;

namespace NeuroRoute.Service.Npu;

public sealed class OnnxSessionFactory : IDisposable
{
    private readonly string _modelPath;
    private readonly object _lock = new();
    private InferenceSession? _session;
    private bool _disposed;

    public OnnxSessionFactory(string modelPath)
    {
        _modelPath = modelPath;
    }

    public InferenceSession GetOrCreateSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_session is null)
        {
            lock (_lock)
            {
                _session ??= new InferenceSession(_modelPath);
            }
        }
        return _session;
    }

    public void ResetSession()
    {
        lock (_lock)
        {
            _session?.Dispose();
            _session = null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            lock (_lock)
            {
                _session?.Dispose();
                _disposed = true;
            }
        }
    }
}
