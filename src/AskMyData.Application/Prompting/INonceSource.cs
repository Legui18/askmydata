namespace AskMyData.Application.Prompting;

/// <summary>Supplies the per-request nonce that delimits the Untrusted_Section (KD12).</summary>
public interface INonceSource
{
    /// <summary>Returns a fresh nonce: 32 lowercase hex characters in production.</summary>
    string Next();
}
