namespace NexLauncher.Services.Authentication;

/// <summary>Platform key protection; injectable to test storage without live credentials.</summary>
public interface IAccountDataProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}
