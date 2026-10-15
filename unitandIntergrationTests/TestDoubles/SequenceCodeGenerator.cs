using UrlShortener.Api.Services;

namespace UrlShortener.Tests.TestDoubles;

/// <summary>Returns a scripted sequence of codes, used to force collisions deterministically.</summary>
public sealed class SequenceCodeGenerator(params string[] codes) : IShortCodeGenerator
{
    private readonly Queue<string> _codes = new(codes);

    public int Calls { get; private set; }

    public string Generate(int length)
    {
        Calls++;
        return _codes.Count > 1 ? _codes.Dequeue() : _codes.Peek();
    }
}
