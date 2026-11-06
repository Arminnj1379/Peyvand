namespace Messenger.Application.Abstractions;

/// <summary>Generates storage identity strings (ObjectId in Infrastructure).</summary>
public interface IIdGenerator
{
    string NewId();
}
