using System;
using System.Linq;
using System.Security.Cryptography;

namespace GraficaCaixa;

public static class OwnerSecurity
{
	public static OwnerPinCredential Create(string pin)
	{
		if (pin.Length < 6 || pin.Any((char x) => !char.IsDigit(x)))
		{
			throw new InvalidOperationException("Use um PIN numérico com pelo menos 6 dígitos.");
		}
		byte[] bytes = RandomNumberGenerator.GetBytes(16);
		return new OwnerPinCredential
		{
			Salt = bytes,
			Hash = Rfc2898DeriveBytes.Pbkdf2(pin, bytes, 210000, HashAlgorithmName.SHA256, 32)
		};
	}

	public static OwnerSession Authenticate(OwnerPinCredential credential, string pin, TimeSpan? idleTimeout = null)
	{
		if (!CryptographicOperations.FixedTimeEquals(Rfc2898DeriveBytes.Pbkdf2(pin, credential.Salt, credential.Iterations, HashAlgorithmName.SHA256, 32), credential.Hash))
		{
			throw new UnauthorizedAccessException("PIN do proprietário inválido.");
		}
		return new OwnerSession(idleTimeout ?? TimeSpan.FromMinutes(10L));
	}

	public static void Require(OwnerSession? session)
	{
		if (session == null || !session.IsActive)
		{
			throw new UnauthorizedAccessException("Autenticação do proprietário obrigatória.");
		}
		session.Touch();
	}
}
