namespace UrlShortener.Api.Services;

public interface IShortCodeGenerator
{
    string Generate(int length);
}
