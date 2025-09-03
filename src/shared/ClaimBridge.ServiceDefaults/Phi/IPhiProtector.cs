namespace ClaimBridge.ServiceDefaults.Phi;

/// <summary>Application-level encryption for PHI columns (member ids, DOB).</summary>
public interface IPhiProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);

    /// <summary>Deterministic keyed hash, used for lookups and duplicate checks without decrypting.</summary>
    string Hash(string value);
}
